// Assets/Scripts/Puzzle/PuzzleController2D.cs
using System.Collections;
using UnityEngine;

public class PuzzleController2D : MonoBehaviour
{
    [Header("Refs")]
    public Lever2D[] levers;

    [Header("Portal Options")]
    public bool usePreplacedPortal = true;     // A: 预放一个 Portal；B: 取消勾选则实例化
    public Portal2D preplacedPortal;           // 方式A：场景里放一个，初始 SetActive=false
    public GameObject portalPrefab;            // 方式B：实例化的预制体（内含 Portal2D）
    public Transform portalSpawn;              // 出现位置（不设用本物体位置）
    public float portalFadeIn = 0.5f;          // 渐显时间

    [Header("Target Pattern")]
    [Tooltip("用 0/1 表示每个拉杆目标状态：Level01 用 \"1\"；三杆可用 \"101\"")]
    public string targetPattern = "1";

    [Header("Options")]
    public bool lockLeversOnSolved = true;

    bool solved;

    void Start()
    {
        foreach (var lv in levers)
            if (lv) lv.OnLeverToggled += OnLeverChanged;

        // 初始检查一次
        Evaluate();

        // 方式A：确保预放 Portal 默认隐藏
        if (usePreplacedPortal && preplacedPortal)
            preplacedPortal.gameObject.SetActive(false);
    }

    void OnDestroy()
    {
        foreach (var lv in levers)
            if (lv) lv.OnLeverToggled -= OnLeverChanged;
    }

    void OnLeverChanged(int id, bool on)
    {
        Evaluate();
    }

    void Evaluate()
    {
        if (solved) return;

        int n = Mathf.Min(targetPattern.Length, levers.Length);
        for (int i = 0; i < n; i++)
        {
            if (!levers[i]) return;
            bool targetOn = targetPattern[i] == '1';
            if (levers[i].IsOn != targetOn) return;
        }

        // 达成
        solved = true;

        if (lockLeversOnSolved)
        {
            foreach (var lv in levers)
            {
                if (!lv) continue;
                lv.enabled = false;
                var c = lv.GetComponent<Collider2D>();
                if (c) c.enabled = false;
                if (lv.prompt) lv.prompt.SetActive(false);
            }
        }

        StartCoroutine(ShowPortalRoutine());
    }

    IEnumerator ShowPortalRoutine()
    {
        Portal2D portal = null;
        Transform spawnAt = portalSpawn ? portalSpawn : transform;

        if (usePreplacedPortal && preplacedPortal)
        {
            portal = preplacedPortal;
            portal.transform.position = spawnAt.position;
            portal.gameObject.SetActive(true);
        }
        else
        {
            if (!portalPrefab)
            {
                Debug.LogError("PuzzleController2D: 未指定 portalPrefab。");
                yield break;
            }
            var go = Instantiate(portalPrefab, spawnAt.position, Quaternion.identity);
            portal = go.GetComponent<Portal2D>();
            if (!portal)
            {
                Debug.LogError("Portal 预制体上缺少 Portal2D 组件。");
                yield break;
            }
        }

        // 渐显（对自身与子物体的所有 SpriteRenderer）
        if (portal)
        {
            var srs = portal.GetComponentsInChildren<SpriteRenderer>(true);
            var targetColors = new Color[srs.Length];

            for (int i = 0; i < srs.Length; i++)
            {
                targetColors[i] = srs[i].color;
                var c = targetColors[i];
                srs[i].color = new Color(c.r, c.g, c.b, 0f);
            }

            float t = 0f;
            while (t < portalFadeIn)
            {
                t += Time.deltaTime;
                float a = portalFadeIn <= 0f ? 1f : Mathf.Clamp01(t / portalFadeIn);
                for (int i = 0; i < srs.Length; i++)
                {
                    var c = targetColors[i];
                    srs[i].color = new Color(c.r, c.g, c.b, a * c.a);
                }
                yield return null;
            }

            portal.Unlock(); // 解锁并在内部延迟启用触发
        }
    }
}
