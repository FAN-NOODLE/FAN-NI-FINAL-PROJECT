// Assets/Scripts/Enemy/EnemySkeleton2D.cs
using UnityEngine;
using System.Collections;

[RequireComponent(typeof(Rigidbody2D), typeof(CapsuleCollider2D))]
public class EnemySkeleton2D : MonoBehaviour
{
    [Header("Refs")]
    public Transform player;                 // 可留空，Awake 自动找 PlayerController2D
    public Transform groundCheckFront;       // 可留空（本脚本也会用计算点）
    public Transform wallCheck;              // 可留空
    public Transform attackPoint;            // 可选：若指定则作为攻击盒中心的参考点

    [Header("Layers")]
    public LayerMask groundMask;             // 只勾 Ground
    public LayerMask playerMask;             // 只勾 Player


    // —— Attack state flags（加在字段区）——
bool attackAnimPlaying;   // 正在播攻击动画（由动画事件控制）
bool attackDidHit;        // 本次攻击是否已结算过伤害


    [Header("Patrol Turn Detection")]
    public float groundProbeAhead = 0.28f;
    public float groundProbeDown = 0.25f;
    public float groundProbeRadius = 0.08f;
    public float wallProbeDist = 0.14f;
    public float turnCooldown = 0.25f;
    float _nextTurnAllowedTime = 0f;

    [Header("Movement")]
    public float walkSpeed = 1.6f;
    public float runSpeed = 3.2f;
    public float aggroRange = 6f;
    public float idleTurnWait = 0.2f;

    [Header("Attack")]
    [Tooltip("发动攻击前的起手时间（秒）")]
    public float attackWindup = 0.25f;
    [Tooltip("两次攻击之间的冷却（秒）")]
    public float attackCooldown = 1.2f;
    [Tooltip("造成的伤害")]
    public int attackDamage = 1;

    [Header("Attack Hitbox (front box)")]
    [Tooltip("攻击盒的尺寸（宽 x 高）")]
    public Vector2 attackBoxSize = new Vector2(1.2f, 1.4f);
    [Tooltip("相对根节点的偏移（前 x，上 y）。会自动随朝向取±x")]
    public Vector2 attackBoxOffset = new Vector2(0.7f, 0.2f);
    [Tooltip("用于‘是否该出手’的预判盒（可略小/略大于命中盒）。留 0 使用同尺寸")]
    public Vector2 precheckBoxSize = Vector2.zero;

    [Header("Hit Reaction")]
    public float hitStun = 0.22f;
    public float knockbackPower = 7f;
    public float knockbackAngleDeg = 35f;
    public bool clampUpwardVelocity = true;
    public float maxUpwardVelocity = 12f;

    public Color flashColor = Color.white;
    public float flashTime = 0.1f;
    public int flashCount = 2;

    [Header("Anim")]
    public Animator anim;                    // 拖 Visual/skeleton 上的 Animator（或留空自动找）

    // 内部
    Rigidbody2D rb;
    CapsuleCollider2D body;
    bool facingRight = true;
    bool isChasing, isAttacking, isDead, inHitStun;
    float _nextAttackTime;

    static readonly int SpeedHash  = Animator.StringToHash("Speed");
    static readonly int DeadHash   = Animator.StringToHash("IsDead");
    static readonly int ChaseHash  = Animator.StringToHash("IsChasing");
    // static readonly int HitHash = Animator.StringToHash("Hit");

    SpriteRenderer[] srs;
    Color[] baseCols;

    void Awake()
    {
        rb = GetComponent<Rigidbody2D>();
        body = GetComponent<CapsuleCollider2D>();

        // 物理/动画稳定：插值 + 跟随物理帧
        rb.freezeRotation = true;
        rb.interpolation = RigidbodyInterpolation2D.Interpolate;

        if (!anim) anim = GetComponentInChildren<Animator>(true);
        if (anim)
        {
            anim.updateMode = AnimatorUpdateMode.AnimatePhysics;
            anim.applyRootMotion = false;
        }

        if (!player)
        {
            var pc = FindObjectOfType<PlayerController2D>();
            if (pc) player = pc.transform;
        }

        // 缓存渲染器用于闪烁
        srs = GetComponentsInChildren<SpriteRenderer>(true);
        baseCols = new Color[srs.Length];
        for (int i = 0; i < srs.Length; i++) baseCols[i] = srs[i].color;

        // 碰撞体水平居中，避免“前方空气墙”
        if (body) body.offset = new Vector2(0f, body.offset.y);
    }

    bool HasAnimator => anim && anim.runtimeAnimatorController != null;
    int FaceSign => facingRight ? +1 : -1;

    void Update()
    {
        if (isDead) return;
        if (!player)
        {
            var pc = FindObjectOfType<PlayerController2D>();
            if (pc) player = pc.transform;
        }
        if (!player) return;

        float dx = player.position.x - transform.position.x;
        float distX = Mathf.Abs(dx);

        isChasing = distX <= aggroRange;

        // 面向玩家（非攻击/硬直）
        if (isChasing && !isAttacking && !inHitStun)
        {
            if ((dx > 0 && !facingRight) || (dx < 0 && facingRight)) Flip();
        }

        // 使用“前方预判盒”判断是否该出手（代替仅看水平距离）
        if (!isAttacking && !inHitStun && Time.time >= _nextAttackTime && PlayerInPrecheckBox())
        {
            StartCoroutine(DoAttack());
        }

        if (HasAnimator)
        {
            anim.SetBool(ChaseHash, isChasing);
            anim.SetFloat(SpeedHash, Mathf.Abs(rb.velocity.x));
        }
    }

    void FixedUpdate()
    {
        if (isDead) { rb.velocity = new Vector2(0f, rb.velocity.y); return; }
        if (isAttacking || inHitStun) return; // 攻击/硬直期间不移动

        if (isChasing)
        {
            float dir = Mathf.Sign(player.position.x - transform.position.x);
            rb.velocity = new Vector2(dir * runSpeed, rb.velocity.y);
        }
        else
        {
            if (NeedTurnAround())
            {
                rb.velocity = new Vector2(0f, rb.velocity.y);
                StartCoroutine(TurnAfter(idleTurnWait));
            }
            else
            {
                float dir = facingRight ? 1f : -1f;
                rb.velocity = new Vector2(dir * walkSpeed, rb.velocity.y);
            }
        }
    }

    IEnumerator DoAttack()
{
    isAttacking = true;
    attackDidHit = false;
    attackAnimPlaying = true;

    if (HasAnimator)
    {
        // 随机两种攻击；若没有 Attack2，可只触发 Attack
        if (Random.value < 0.5f) anim.SetTrigger("Attack");
        else anim.SetTrigger("Attack2");
    }

    // 等待动画结束事件
    yield return new WaitUntil(() => attackAnimPlaying == false);

    _nextAttackTime = Time.time + attackCooldown;
    isAttacking = false;
}

// 动画开头事件（可选）：第一帧触发
public void Anim_AttackBegin()
{
    attackDidHit = false;
    attackAnimPlaying = true;
}

// 命中帧事件：在你想结算伤害的关键帧调用
public void Anim_AttackHit()
{
    if (attackDidHit || isDead) return; // 防多次命中
    attackDidHit = true;

    var hits = Physics2D.OverlapBoxAll(GetAttackCenter(), attackBoxSize, 0f, playerMask);
    foreach (var h in hits)
    {
        var ph = h.GetComponentInParent<PlayerHealth>();
        if (ph) ph.TakeDamage(attackDamage, transform.position);
    }
}

// 动画结束事件：最后一帧或收势动作末尾触发
public void Anim_AttackEnd()
{
    Debug.Log("Anim_AttackEnd called");
    attackAnimPlaying = false;
}

    // —— 巡逻掉头 —— //
    bool NeedTurnAround()
    {
        if (Time.time < _nextTurnAllowedTime) return false;

        Vector3 aheadBase = groundCheckFront
            ? groundCheckFront.position
            : transform.position + new Vector3(FaceSign * groundProbeAhead, 0f, 0f);

        Vector3 downPoint = aheadBase + Vector3.down * groundProbeDown;
        Vector2 fwd = facingRight ? Vector2.right : Vector2.left;

        bool hitWall = wallCheck
            ? Physics2D.Raycast(wallCheck.position, fwd, wallProbeDist, groundMask)
            : Physics2D.Raycast(aheadBase, fwd, wallProbeDist, groundMask);

        bool hasGroundAhead = Physics2D.OverlapCircle(downPoint, groundProbeRadius, groundMask);

        bool needTurn = hitWall || !hasGroundAhead;
        if (needTurn) _nextTurnAllowedTime = Time.time + turnCooldown;
        return needTurn;
    }

    IEnumerator TurnAfter(float t)
    {
        yield return new WaitForSeconds(t);
        if (!isChasing && !isAttacking && !isDead && !inHitStun) Flip();
    }

    void Flip()
    {
        facingRight = !facingRight;

        // 只翻根节点缩放（不改碰撞体 offset），并清水平速度避免“闪一下”
        var s = transform.localScale; s.x *= -1f; transform.localScale = s;
        rb.velocity = new Vector2(0f, rb.velocity.y);

        _nextTurnAllowedTime = Time.time + turnCooldown; // 避免立刻又翻回去
    }

    public void Die()
    {
        if (isDead) return;
        isDead = true;
        if (HasAnimator) anim.SetBool(DeadHash, true);
        rb.velocity = Vector2.zero;
        rb.isKinematic = true;
        if (body) body.enabled = false;
        Destroy(gameObject, 2f);
    }

    // —— 受击入口：由 EnemyHealth.TakeDamage(...) 调用 —— //
    public void HurtKnockback(Vector2 from)
    {
        if (isDead) return;
        StopCoroutineSafe(_hitFlashCo);
        StartCoroutine(HitRoutine(from));
    }

    Coroutine _hitFlashCo;
    IEnumerator HitRoutine(Vector2 from)
    {
        inHitStun = true;
        isAttacking = false;

        // if (HasAnimator) anim.SetTrigger(HitHash);

        _hitFlashCo = StartCoroutine(FlashCo());

        // 角度击退：远离伤害源，按角度抬升
        float sign = Mathf.Sign(transform.position.x - from.x);
        float ang = knockbackAngleDeg * Mathf.Deg2Rad;
        Vector2 dir = new Vector2(sign * Mathf.Cos(ang), Mathf.Sin(ang)).normalized;

        rb.velocity = new Vector2(rb.velocity.x, Mathf.Min(rb.velocity.y, 0f));
        rb.AddForce(dir * knockbackPower, ForceMode2D.Impulse);

        if (clampUpwardVelocity && rb.velocity.y > maxUpwardVelocity)
            rb.velocity = new Vector2(rb.velocity.x, maxUpwardVelocity);

        yield return new WaitForSeconds(hitStun);
        inHitStun = false;
    }

    IEnumerator FlashCo()
    {
        for (int n = 0; n < flashCount; n++)
        {
            SetFlash(true);
            yield return new WaitForSeconds(flashTime * 0.5f);
            SetFlash(false);
            yield return new WaitForSeconds(flashTime * 0.5f);
        }
    }

    void SetFlash(bool on)
    {
        if (srs == null) return;
        for (int i = 0; i < srs.Length; i++)
        {
            if (!srs[i]) continue;
            var baseC = baseCols[i];
            var c = on ? flashColor : baseC;
            srs[i].color = new Color(c.r, c.g, c.b, baseC.a);
        }
    }

    void StopCoroutineSafe(Coroutine co)
    {
        if (co != null) StopCoroutine(co);
    }

    // —— 前方攻击盒工具 —— //
    Vector2 GetAttackCenter()
    {
        // 优先使用 attackPoint 的位置；否则用根 + 偏移（随朝向镜像）
        if (attackPoint) return attackPoint.position;
        Vector2 offset = new Vector2(FaceSign * attackBoxOffset.x, attackBoxOffset.y);
        return (Vector2)transform.position + offset;
    }

    Vector2 GetHitboxSize()
    {
        return (precheckBoxSize == Vector2.zero) ? attackBoxSize : attackBoxSize;
    }

    bool PlayerInPrecheckBox()
    {
        Vector2 center = GetAttackCenter();
        Vector2 size = (precheckBoxSize == Vector2.zero) ? attackBoxSize : precheckBoxSize;
        return Physics2D.OverlapBox(center, size, 0f, playerMask) != null;
    }

    void OnDrawGizmosSelected()
    {
        // 攻击盒可视化
        Gizmos.color = new Color(1f, 0.4f, 0.2f, 0.6f);
        Vector2 center = Application.isPlaying ? GetAttackCenter()
            : (Vector2)transform.position + new Vector2(((transform.localScale.x >= 0f) ? +1f : -1f) * attackBoxOffset.x, attackBoxOffset.y);
        Vector2 size = attackBoxSize;
        Gizmos.DrawWireCube(center, size);

        // 巡逻探测可视化
        int sign = Application.isPlaying ? (facingRight ? 1 : -1) : (transform.localScale.x >= 0 ? 1 : -1);
        Vector3 aheadBase = (groundCheckFront ? groundCheckFront.position : transform.position + new Vector3(sign * groundProbeAhead, 0f, 0f));
        Vector3 downPoint = aheadBase + Vector3.down * groundProbeDown;

        Gizmos.color = Color.cyan; // 墙探测
        Gizmos.DrawLine(aheadBase, aheadBase + new Vector3(sign * wallProbeDist, 0f, 0f));

        Gizmos.color = Color.yellow; // 地面重叠圆
        Gizmos.DrawWireSphere(downPoint, groundProbeRadius);
    }
}

