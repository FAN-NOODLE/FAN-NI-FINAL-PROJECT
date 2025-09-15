using UnityEngine;

[RequireComponent(typeof(Camera))]
public class CameraFollow2D : MonoBehaviour
{
    [Header("Target")]
    public Transform target;

    [Header("Follow")]
    public float smoothTime = 0.15f;      // 水平平滑
    public float xOffset = 0f;            // 向前看一点（正值向右）
    [Tooltip("死区半宽：0 表示到达屏幕正中即跟随；设 0.3~0.6 可避免抖动")]
    public float deadZoneHalfWidth = 0f;  // 以屏幕中心为中心的死区半宽（世界单位）

    [Header("Bounds")]
    public BoxCollider2D levelBounds;     // 仅作边界，建议是 Trigger 的 BoxCollider2D（不参与物理）
    public bool snapToLeftOnStart = true; // 开局把相机贴到最左可视位置

    // 内部
    Camera cam;
    float fixedY, fixedZ;
    float velX;

    void Awake()
    {
        cam = GetComponent<Camera>();
        fixedY = transform.position.y;    // 只横移
        fixedZ = transform.position.z;
    }

    void Start()
    {
        if (snapToLeftOnStart && levelBounds)
        {
            var b = levelBounds.bounds;
            float halfW = cam.orthographicSize * cam.aspect;
            float minCamX = b.min.x + halfW;
            transform.position = new Vector3(minCamX, fixedY, fixedZ);
        }
    }

    void LateUpdate()
    {
        if (!target || !cam || !levelBounds) return;

        var b = levelBounds.bounds;
        float halfW = cam.orthographicSize * cam.aspect;

        // 相机中心的可移动范围
        float minCamX = b.min.x + halfW;
        float maxCamX = b.max.x - halfW;
        if (minCamX > maxCamX) { float mid = b.center.x; minCamX = maxCamX = mid; }

        // 当前相机中心X & 玩家（带前视）X
        float camX = transform.position.x;
        float playerX = target.position.x + xOffset;

        // 以“屏幕中心”为阈值：只有当玩家越过中心±死区时才移动相机
        float desiredX = camX;
        if (playerX > camX + deadZoneHalfWidth)
            desiredX = Mathf.Min(playerX - deadZoneHalfWidth, maxCamX);
        else if (playerX < camX - deadZoneHalfWidth)
            desiredX = Mathf.Max(playerX + deadZoneHalfWidth, minCamX);

        float newX = Mathf.SmoothDamp(camX, desiredX, ref velX, smoothTime);
        transform.position = new Vector3(newX, fixedY, fixedZ);
    }

    // 可视化死区与边界
    void OnDrawGizmosSelected()
    {
        if (!levelBounds || !cam) return;
        var b = levelBounds.bounds;
        Gizmos.color = Color.cyan;
        Gizmos.DrawLine(new Vector3(b.min.x, b.min.y, 0), new Vector3(b.min.x, b.max.y, 0));
        Gizmos.DrawLine(new Vector3(b.max.x, b.min.y, 0), new Vector3(b.max.x, b.max.y, 0));

        Gizmos.color = Color.yellow;
        float cx = Application.isPlaying ? transform.position.x : (Camera.main ? Camera.main.transform.position.x : transform.position.x);
        Gizmos.DrawLine(new Vector3(cx, b.min.y, 0), new Vector3(cx, b.max.y, 0));
        Gizmos.DrawLine(new Vector3(cx - deadZoneHalfWidth, b.min.y, 0), new Vector3(cx - deadZoneHalfWidth, b.max.y, 0));
        Gizmos.DrawLine(new Vector3(cx + deadZoneHalfWidth, b.min.y, 0), new Vector3(cx + deadZoneHalfWidth, b.max.y, 0));
    }
}


