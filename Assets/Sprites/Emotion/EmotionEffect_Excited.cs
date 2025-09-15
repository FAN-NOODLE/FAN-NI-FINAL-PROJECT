// Assets/Scripts/Emotion/EmotionEffect_Excited.cs
using UnityEngine;
using System;
using System.Reflection;

public class EmotionEffect_Excited : MonoBehaviour
{
    [Header("Refs")]
    public MonoBehaviour emotionManager;   // 建议绑定 DummyEmotionFeed；后续可换真实 EmotionManager
    public PlayerCombat2D playerCombat;    // 玩家战斗组件
    public HUDPlayerUI hud;                // HUD（用于状态图标）
    public Sprite statusIcon;              // 兴奋状态图标

    [Header("Config")]
    public float minConfidence = 0.65f;
    public float excitedDamageMult = 2.0f;
    public string statusId = "excited_buff";
    public float hudStatusDuration = 1.5f;

    bool applied;
    float _nextHudRefresh;

    // 强类型订阅（首选）
    DummyEmotionFeed typedFeed;
    // 反射订阅（兜底）
    EventInfo reflEvent;
    Delegate reflHandler;

    void Awake()
    {
        if (!playerCombat) playerCombat = FindObjectOfType<PlayerCombat2D>(true);
        if (!hud) hud = FindObjectOfType<HUDPlayerUI>(true);
        if (!emotionManager)
        {
            // 自动找 DummyEmotionFeed，省去手动拖
            var feed = FindObjectOfType<DummyEmotionFeed>(true);
            if (feed) emotionManager = feed;
        }
        TrySubscribeEmotionEvent();
    }

    void OnDestroy()
    {
        TryUnsubscribeEmotionEvent();
    }

    void Update()
    {
        if (applied && hud && statusIcon && Time.unscaledTime >= _nextHudRefresh)
        {
            hud.ShowStatus(statusId, statusIcon, hudStatusDuration);
            _nextHudRefresh = Time.unscaledTime + hudStatusDuration * 0.6f;
        }
    }

    void TrySubscribeEmotionEvent()
    {
        if (!emotionManager)
        {
            Debug.LogWarning("[EmotionEffect_Excited] emotionManager 未绑定。");
            return;
        }

        // ① 强类型：如果就是 DummyEmotionFeed，直接订阅强类型事件（零反射，最稳）
        typedFeed = emotionManager as DummyEmotionFeed;
        if (typedFeed != null)
        {
            typedFeed.OnEmotionChanged += OnDummyEmotion;
            return;
        }

        // ② 反射兜底：找 OnEmotionChanged 事件（Action<T>），参数里解析 label/confidence
        reflEvent = emotionManager.GetType().GetEvent("OnEmotionChanged");
        if (reflEvent == null)
        {
            Debug.LogWarning("[EmotionEffect_Excited] 找不到 OnEmotionChanged 事件。");
            return;
        }

        // 注意：这里的处理函数签名必须和事件参数兼容；我们用 object 接收（值类型会装箱为 object）
        reflHandler = Delegate.CreateDelegate(reflEvent.EventHandlerType, this,
            nameof(OnEmotionEvent_Reflected));
        reflEvent.AddEventHandler(emotionManager, reflHandler);
    }

    void TryUnsubscribeEmotionEvent()
    {
        if (typedFeed != null)
        {
            typedFeed.OnEmotionChanged -= OnDummyEmotion;
            typedFeed = null;
        }
        if (reflEvent != null && reflHandler != null && emotionManager)
        {
            reflEvent.RemoveEventHandler(emotionManager, reflHandler);
        }
        reflEvent = null;
        reflHandler = null;
    }

    // —— 强类型回调（DummyEmotionFeed）——
    void OnDummyEmotion(DummyEmotionFeed.EmotionEvent e)
    {
        HandleLabelConfidence(e.label, e.confidence);
    }

    // —— 反射回调（任何结构，只要含 label/confidence 字段或属性）——
    void OnEmotionEvent_Reflected(object boxed)
    {
        if (!TryExtractLabelConfidence(boxed, out string label, out float conf))
        {
            Debug.LogWarning("[EmotionEffect_Excited] 无法从事件参数中解析 label/confidence。");
            return;
        }
        HandleLabelConfidence(label, conf);
    }

    // 解析工具：同时支持字段/属性 & 忽略大小写
    static bool TryExtractLabelConfidence(object obj, out string label, out float confidence)
    {
        label = null; confidence = 0f;
        if (obj == null) return false;

        var t = obj.GetType();
        const BindingFlags flags = BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase;

        // label
        var fLabel = t.GetField("label", flags);
        if (fLabel != null) label = fLabel.GetValue(obj) as string;
        if (label == null)
        {
            var pLabel = t.GetProperty("label", flags) ?? t.GetProperty("Label", flags);
            if (pLabel != null) label = pLabel.GetValue(obj) as string;
        }

        // confidence
        var fConf = t.GetField("confidence", flags);
        if (fConf != null) confidence = Convert.ToSingle(fConf.GetValue(obj));
        else
        {
            var pConf = t.GetProperty("confidence", flags) ?? t.GetProperty("Confidence", flags);
            if (pConf != null) confidence = Convert.ToSingle(pConf.GetValue(obj));
            else confidence = 1f; // 没有就给个默认
        }

        return !string.IsNullOrEmpty(label);
    }

    // 统一处理逻辑
    void HandleLabelConfidence(string label, float confidence)
    {
        bool excited = !string.IsNullOrEmpty(label) &&
                       label.Equals("excited", StringComparison.OrdinalIgnoreCase);

        if (excited && confidence >= minConfidence) Apply();
        else Remove();
    }

    void Apply()
    {
        if (applied) return;
        if (!playerCombat)
        {
            Debug.LogWarning("[EmotionEffect_Excited] 未找到 PlayerCombat2D。");
            return;
        }
        playerCombat.SetDamageMultiplier(excitedDamageMult);
        applied = true;

        if (hud && statusIcon)
        {
            hud.ShowStatus(statusId, statusIcon, hudStatusDuration);
            _nextHudRefresh = Time.unscaledTime + hudStatusDuration * 0.6f;
        }
    }

    void Remove()
    {
        if (!applied) return;
        if (playerCombat) playerCombat.ResetDamageMultiplier();
        applied = false;
        if (hud) hud.ClearStatus(statusId);
    }
}
