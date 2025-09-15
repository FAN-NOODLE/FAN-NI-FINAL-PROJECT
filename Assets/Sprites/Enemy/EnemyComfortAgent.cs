using UnityEngine;
using System.Collections;
using System.Collections.Generic;

[RequireComponent(typeof(Rigidbody2D))]
public class EnemyComfortAgent : MonoBehaviour
{
    [Header("Refs")]
    public EnemySkeleton2D enemyAI;
    public SpeechBubble2D bubble;
    public Animator anim;
    public Rigidbody2D rb;
    public Collider2D hitCollider;

    [Header("Motion Settings")]
    public float approachDistance = 1.5f;   // 到玩家的水平停止距离
    public float slowDownDistance = 3.0f;   // 接近时减速的范围
    public float moveSpeed = 2.8f;          // 目标巡航速度
    public float maxSpeed = 3.5f;           // 最大水平速度钳制
    public float idleTransitionTime = 0.5f; // 停下后稳定时间

    [Header("Speaking")]
    public float holdSeconds = 3.0f;
    public bool typewriter = true;

    [Header("Fade Out")]
    public float fadeDuration = 0.8f;

    [Header("Collision Handling")]
    public bool disableCollisionDuringMove = true; // 移动期间忽略“敌人 vs 玩家”的碰撞

    [Header("Animator Params / States")]
    public string speedParam = "Speed";            // 注意：大写 S
    public string isChasingParam = "isChasing";    // 你的 Running→Walking 过渡所用布尔
    public string idleStateName = "Skeleton_Idle"; // 你的 Idle 状态名（可改成实际名字）
    public string walkingStateName = "Skeleton_Walking";

    private int _speedHash;
    private int _isChasingHash;

    private bool _working;
    private bool _hasStopped;

    // 记录忽略的碰撞对（敌人 vs 玩家），方便恢复
    private readonly List<(Collider2D a, Collider2D b)> _ignoredPairs = new();

    void Awake()
    {
        _speedHash = Animator.StringToHash(speedParam);
        _isChasingHash = Animator.StringToHash(isChasingParam);
    }

    void Reset()
    {
        rb = GetComponent<Rigidbody2D>();
        if (!enemyAI) enemyAI = GetComponent<EnemySkeleton2D>();
        if (!anim && enemyAI) anim = enemyAI.anim;
        if (!hitCollider) hitCollider = GetComponent<Collider2D>();
    }

    public void StartComfort(Transform player, string line)
    {
        if (_working) return;
        StartCoroutine(ComfortRoutine(player, line));
    }

    IEnumerator ComfortRoutine(Transform player, string line)
    {
        _working = true;
        _hasStopped = false;

        // 0) 仅忽略“敌人 vs 玩家”的碰撞（地面照常碰撞，避免掉图）
        if (disableCollisionDuringMove && player != null)
        {
            var playerCols = player.GetComponentsInChildren<Collider2D>(true);
            var enemyCols  = GetComponentsInChildren<Collider2D>(true);
            _ignoredPairs.Clear();
            foreach (var ec in enemyCols)
            {
                if (!ec || !ec.enabled) continue;
                foreach (var pc in playerCols)
                {
                    if (!pc || !pc.enabled) continue;
                    Physics2D.IgnoreCollision(ec, pc, true);
                    _ignoredPairs.Add((ec, pc));
                }
            }
        }

        // 1) 暂停原 AI，并立刻关追击（触发 Running→Walking）
        if (enemyAI) enemyAI.enabled = false;
        if (anim)
        {
            if (_isChasingHash != 0) anim.SetBool(_isChasingHash, false);
        }

        // 2) 移动到玩家前面（仅按水平距离）
        while (player != null && !_hasStopped)
        {
            float horizontalDistance = Mathf.Abs(player.position.x - transform.position.x);

            // 已到达停止距离
            if (horizontalDistance <= approachDistance)
            {
                _hasStopped = true;
                break;
            }

            float dirX = Mathf.Sign(player.position.x - transform.position.x);

            // 让敌人面向玩家
            var s = transform.localScale;
            if ((dirX > 0 && s.x < 0) || (dirX < 0 && s.x > 0)) { s.x *= -1; transform.localScale = s; }

            // 接近时按比例减速
            float currentSpeed = moveSpeed;
            if (horizontalDistance < slowDownDistance)
            {
                float factor = Mathf.Clamp01(horizontalDistance / slowDownDistance);
                currentSpeed = Mathf.Lerp(0.5f, moveSpeed, factor);
            }

            // 写刚体速度（限制最大水平速度）
            if (rb)
            {
                var v = new Vector2(dirX * currentSpeed, rb.velocity.y);
                v.x = Mathf.Clamp(v.x, -maxSpeed, maxSpeed);
                rb.velocity = v;
            }

            // 写 Animator 参数：保持 isChasing=false，并实时写 Speed（大写 S）
            if (anim)
            {
                if (_isChasingHash != 0) anim.SetBool(_isChasingHash, false);
                float animSpeed = rb ? Mathf.Abs(rb.velocity.x) : Mathf.Abs(currentSpeed);
                anim.SetFloat(_speedHash, animSpeed);
            }

            yield return new WaitForFixedUpdate();
        }

        // 3) 恢复敌人与玩家的碰撞
        if (disableCollisionDuringMove && _ignoredPairs.Count > 0)
        {
            foreach (var p in _ignoredPairs)
            {
                if (p.a && p.b) Physics2D.IgnoreCollision(p.a, p.b, false);
            }
            _ignoredPairs.Clear();
        }

        // 4) 完全停住 + 动画参数写停
        if (rb) rb.velocity = new Vector2(0f, rb.velocity.y);
        if (anim)
        {
            if (_isChasingHash != 0) anim.SetBool(_isChasingHash, false); // 防抖：别让它又进 Running
            anim.SetFloat(_speedHash, 0f);                                // 关键：满足 Speed < 0.05
        }

        // 5) 稳定一点时间，保证过渡到 Walking→Idle
        yield return new WaitForSeconds(idleTransitionTime);

        // 保险：若仍未进 Idle，可直接 CrossFade（可按需保留/删除）
        if (anim && !string.IsNullOrEmpty(idleStateName))
        {
            // 如果控制器 Transition 较苛刻，这一步能强制到 Idle
            anim.CrossFade(idleStateName, 0.1f);
        }

        // 6) 显示气泡（若有）
        if (bubble)
        {
            // 轻微位置微调，避免紧贴
            if (player)
            {
                float adjust = Mathf.Sign(transform.position.x - player.position.x) * 0.2f;
                transform.position = new Vector3(transform.position.x + adjust, transform.position.y, transform.position.z);
            }

            bubble.Show(line, holdSeconds, typewriter);
            yield return new WaitForSeconds(holdSeconds + 0.5f);
        }
        else
        {
            yield return new WaitForSeconds(holdSeconds);
        }

        // 7) 淡出并销毁
        if (hitCollider) hitCollider.enabled = false;
        var srs = GetComponentsInChildren<SpriteRenderer>(true);
        float t = 0f;
        while (t < fadeDuration)
        {
            t += Time.deltaTime;
            float a = 1f - (t / fadeDuration);
            foreach (var sr in srs)
            {
                if (!sr) continue;
                var c = sr.color; c.a = a; sr.color = c;
            }
            yield return null;
        }
        Destroy(gameObject);
    }

    // 可视化调试
    void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.red;
        Vector3 leftStop = transform.position + Vector3.left * approachDistance;
        Vector3 rightStop = transform.position + Vector3.right * approachDistance;
        Gizmos.DrawLine(leftStop + Vector3.up, leftStop + Vector3.down);
        Gizmos.DrawLine(rightStop + Vector3.up, rightStop + Vector3.down);

        Gizmos.color = Color.yellow;
        Vector3 leftSlow = transform.position + Vector3.left * slowDownDistance;
        Vector3 rightSlow = transform.position + Vector3.right * slowDownDistance;
        Gizmos.DrawLine(leftSlow + Vector3.up, leftSlow + Vector3.down);
        Gizmos.DrawLine(rightSlow + Vector3.up, rightSlow + Vector3.down);
    }
}
