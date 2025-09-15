using UnityEngine;

public class EmotionEffect_Sad : MonoBehaviour
{
    [Header("Targets")]
    public Transform player;                     // 玩家的 Transform
    public float searchRadius = 12f;             // 搜索最近敌人的半径

    [Header("Providers & Prefabs")]
    public MonoBehaviour lineProviderSource;     // 绑定 DummyComfortLineProvider（或你的 AI Provider）
    IComfortLineProvider _provider;

    [Header("HUD Status (optional)")]
    public HUDPlayerUI hud;                      // 可选：HUD
    public Sprite statusIcon;                    // Sad 图标
    public string statusId = "sad";              // 状态唯一ID
    public bool iconPersistentWhileSad = true;   // Sad 期间常驻
    public float statusDuration = 2f;            // 非常驻时的显示时长

    bool _applied;                                // 进入Sad后是否已触发过一次（避免重复）

    void Awake()
    {
        if (!player)
        {
            var pc = FindObjectOfType<PlayerController2D>();
            if (pc) player = pc.transform;
        }
        _provider = lineProviderSource as IComfortLineProvider;
        if (_provider == null && lineProviderSource != null)
            Debug.LogWarning("[EmotionEffect_Sad] 绑定的 lineProviderSource 未实现 IComfortLineProvider。");
    }

    // 外部调用：HUDPlayerUI / DummyEmotionFeed 等在切情绪时调用
    public void OnEmotionChanged(EmotionType type)
    {
        // 离开 Sad：清图标并复位
        if (type != EmotionType.Sad)
        {
            ClearStatusIcon();
            _applied = false;
            return;
        }

        // 进入/保持 Sad：先显示图标
        ShowStatusIcon();

        // 仅在刚进入 Sad 时触发安慰流程，避免多次生成
        if (_applied) return;
        _applied = true;

        if (!player) return;
        var enemy = FindNearestEnemy(player.position, searchRadius);
        if (!enemy) return;

        // 确保有安慰代理
        var agent = enemy.GetComponent<EnemyComfortAgent>();
        if (!agent) agent = enemy.gameObject.AddComponent<EnemyComfortAgent>();

        // 准备台词
        string line = _provider != null ? _provider.GetLine() : "你并不孤单，我在。";

        // 找气泡：优先找子节点里的 SpeechBubble2D
        if (!agent.bubble)
        {
            var bubble = enemy.GetComponentInChildren<SpeechBubble2D>(true);
            if (bubble) agent.bubble = bubble;
        }

        // 常用依赖补齐
        if (!agent.anim && enemy.anim) agent.anim = enemy.anim;

        agent.StartComfort(player, line);
    }

    void ShowStatusIcon()
    {
        if (!hud || !statusIcon) return;
        float dura = iconPersistentWhileSad ? 0f : Mathf.Max(0f, statusDuration);
        hud.ShowStatus(statusId, statusIcon, dura); // duration=0 → 常驻，等离开Sad时清
    }

    void ClearStatusIcon()
    {
        if (hud) hud.ClearStatus(statusId);
    }

    EnemySkeleton2D FindNearestEnemy(Vector3 center, float radius)
    {
        EnemySkeleton2D best = null;
        float bestDist = float.MaxValue;
        var all = FindObjectsOfType<EnemySkeleton2D>();
        foreach (var e in all)
        {
            if (!e || !e.gameObject.activeInHierarchy) continue;
            float d = Mathf.Abs(e.transform.position.x - center.x); // 只比较水平更贴合2D
            if (d < bestDist && Vector2.Distance(e.transform.position, center) <= radius)
            {
                best = e; bestDist = d;
            }
        }
        return best;
    }
}
