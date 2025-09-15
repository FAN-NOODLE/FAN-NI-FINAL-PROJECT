using UnityEngine;

[RequireComponent(typeof(Rigidbody2D), typeof(CapsuleCollider2D), typeof(Animator))]
public class PlayerController2D : MonoBehaviour
{
    [Header("Movement Settings")]
    public float maxSpeed = 7f;
    public float acceleration = 50f;
    public float deceleration = 60f;
    public float jumpForce = 12f;
    public float coyoteTime = 0.1f;
    public float jumpBuffer = 0.1f;

    [Header("Ground Check")]
    public Transform groundCheck;
    public float groundRadius = 0.15f;
    public LayerMask groundMask;

    // Animator 参数哈希
    static readonly int SpeedHash = Animator.StringToHash("Speed");
    static readonly int GroundHash = Animator.StringToHash("IsGrounded");
    static readonly int YVelHash   = Animator.StringToHash("YVel");
    static readonly int AttackHash = Animator.StringToHash("Attack");

    Rigidbody2D rb;
    Animator anim;

    float lastGroundedTime;
    float lastJumpPressedTime;
    bool isGrounded;

    // —— 新增：复用缓冲，避免 GC；并且能过滤触发器
    readonly Collider2D[] _groundHits = new Collider2D[6];

    void Awake()
    {
        rb = GetComponent<Rigidbody2D>();
        anim = GetComponent<Animator>();
        rb.freezeRotation = true;
    }

    void Update()
    {
        float move = Input.GetAxisRaw("Horizontal");
        if (move != 0)
        {
            var s = transform.localScale;
            float sizeX = Mathf.Abs(s.x);
            s.x = (move > 0 ? +sizeX : -sizeX);
            transform.localScale = s;
        }

        UpdateGroundStatus();
        HandleJumpInput();

        if (Input.GetButtonDown("Fire1"))
            anim.SetTrigger(AttackHash);
    }

    void FixedUpdate()
    {
        HandleHorizontalMovement();
        UpdateAnimationParameters();
    }

    void UpdateGroundStatus()
    {
        // 只在 groundMask 内检测，并过滤掉 Trigger 碰撞体
        int count = Physics2D.OverlapCircleNonAlloc(
            groundCheck.position, groundRadius, _groundHits, groundMask);

        bool groundedNow = false;
        for (int i = 0; i < count; i++)
        {
            var c = _groundHits[i];
            if (c != null && !c.isTrigger) { groundedNow = true; break; }
        }

        isGrounded = groundedNow;

        // Coyote
        if (isGrounded) lastGroundedTime = coyoteTime;
        else            lastGroundedTime -= Time.deltaTime;
    }

    void HandleJumpInput()
    {
        if (Input.GetButtonDown("Jump"))
            lastJumpPressedTime = jumpBuffer;
        else
            lastJumpPressedTime -= Time.deltaTime;

        if (lastJumpPressedTime > 0 && lastGroundedTime > 0)
        {
            // 先清纵向速度，避免“叠跳”越跳越高
            rb.velocity = new Vector2(rb.velocity.x, 0f);
            rb.velocity = new Vector2(rb.velocity.x, jumpForce);
            lastJumpPressedTime = 0;
            lastGroundedTime = 0;
        }
    }

    void HandleHorizontalMovement()
    {
        float move = Input.GetAxisRaw("Horizontal");
        float targetSpeed = move * maxSpeed;
        float speedDiff = targetSpeed - rb.velocity.x;
        float accelRate = (Mathf.Abs(targetSpeed) > 0.01f) ? acceleration : deceleration;
        float movement = speedDiff * accelRate * Time.fixedDeltaTime;
        rb.AddForce(new Vector2(movement, 0), ForceMode2D.Force);

        if (Mathf.Abs(rb.velocity.x) > maxSpeed)
            rb.velocity = new Vector2(Mathf.Sign(rb.velocity.x) * maxSpeed, rb.velocity.y);
    }

    void UpdateAnimationParameters()
    {
        anim.SetFloat(SpeedHash, Mathf.Abs(rb.velocity.x));
        anim.SetBool(GroundHash, isGrounded);
        anim.SetFloat(YVelHash, rb.velocity.y);
    }

    void OnDrawGizmosSelected()
    {
        if (groundCheck)
        {
            Gizmos.color = isGrounded ? Color.green : Color.red;
            Gizmos.DrawWireSphere(groundCheck.position, groundRadius);
        }
    }
}

