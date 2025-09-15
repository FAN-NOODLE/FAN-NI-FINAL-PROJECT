// Assets/Scripts/Emotion/EmotionEffect_Happy.cs
using UnityEngine;
using System;
using System.Reflection;

public class EmotionEffect_Happy : MonoBehaviour
{
    [Header("Sources")]
    public MonoBehaviour emotionManager;     // 可留空，自动找 DummyEmotionFeed
    public PlayerHealth playerHealth;        // 可留空，自动绑定（Tag=Player）

    [Header("Happy Config")]
    [Range(0f,1f)] public float minConfidence = 0.65f;
    public int bonusMaxHP = 2;               // 上限加多少
    public bool healToNewMax = true;         // 切入时是否抬到新上限
    public int healFlatIfNotFull = 2;        // 不抬满时，额外按值回血

    [Header("HUD Status (optional)")]
    public HUDPlayerUI hud;                  // HUD（可选）
    public Sprite statusIcon;                // 图标
    public string statusId = "happy";
    public bool iconPersistentWhileHappy = true; // ★ 新增：Happy 期间常驻
    public float statusDuration = 2f;            // 若不常驻，才使用这个时长

    DummyEmotionFeed _typedFeed;
    EventInfo _reflEvent;
    Delegate _reflHandler;

    bool _applied;
    const string MOD_ID = "emotion_happy";

    void Awake()
    {
        if (!playerHealth)
        {
            var go = GameObject.FindGameObjectWithTag("Player");
            if (go)
                playerHealth = go.GetComponentInChildren<PlayerHealth>(true) ?? go.GetComponent<PlayerHealth>();
            if (!playerHealth)
            {
                var all = FindObjectsOfType<PlayerHealth>(true);
                if (all.Length > 0) playerHealth = all[0];
            }
        }
    }

    void OnEnable()  { TryHookEmotionEvents(); }
    void OnDisable() { TryUnsubscribe(); RemoveIfApplied(); }

    void TryHookEmotionEvents()
    {
        if (!emotionManager)
        {
            var feed = FindObjectOfType<DummyEmotionFeed>(true);
            if (feed) emotionManager = feed;
        }
        if (!emotionManager) return;

        _typedFeed = emotionManager as DummyEmotionFeed;
        if (_typedFeed != null) { _typedFeed.OnEmotionChanged += OnEmotionTyped; return; }

        _reflEvent = emotionManager.GetType().GetEvent("OnEmotionChanged");
        if (_reflEvent == null) return;
        _reflHandler = Delegate.CreateDelegate(_reflEvent.EventHandlerType, this, nameof(OnEmotionReflected));
        _reflEvent.AddEventHandler(emotionManager, _reflHandler);
    }

    void TryUnsubscribe()
    {
        if (_typedFeed != null) { _typedFeed.OnEmotionChanged -= OnEmotionTyped; _typedFeed = null; }
        if (_reflEvent != null && _reflHandler != null && emotionManager)
            _reflEvent.RemoveEventHandler(emotionManager, _reflHandler);
        _reflEvent = null; _reflHandler = null;
    }

    void OnEmotionTyped(DummyEmotionFeed.EmotionEvent e) => HandleLabel(e.label, e.confidence);
    void OnEmotionReflected(object boxed)
    {
        if (TryExtract(boxed, out var label, out var conf)) HandleLabel(label, conf);
    }

    static bool TryExtract(object obj, out string label, out float conf)
    {
        label = null; conf = 1f; if (obj == null) return false;
        const BindingFlags F = BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase;
        var t = obj.GetType();
        var fL = t.GetField("label", F); if (fL != null) label = fL.GetValue(obj) as string;
        if (label == null) { var pL = t.GetProperty("label", F) ?? t.GetProperty("Label", F); if (pL != null) label = pL.GetValue(obj) as string; }
        var fC = t.GetField("confidence", F); if (fC != null) conf = Convert.ToSingle(fC.GetValue(obj));
        else { var pC = t.GetProperty("confidence", F) ?? t.GetProperty("Confidence", F); if (pC != null) conf = Convert.ToSingle(pC.GetValue(obj)); }
        return !string.IsNullOrEmpty(label);
    }

    void HandleLabel(string label, float confidence)
    {
        if (!playerHealth) return;
        bool isHappy = string.Equals(label, "happy", StringComparison.OrdinalIgnoreCase) && confidence >= minConfidence;

        if (isHappy) { if (!_applied) Apply(); }
        else { RemoveIfApplied(); }
    }

    void Apply()
    {
        playerHealth.AddMaxHPModifier(MOD_ID, bonusMaxHP, healToNewMax);
        if (!healToNewMax && healFlatIfNotFull > 0) playerHealth.Heal(healFlatIfNotFull);

        // ★ 核心：Happy 期间常驻图标（duration=0）；离开再清除
        if (hud && statusIcon)
        {
            float dura = iconPersistentWhileHappy ? 0f : Mathf.Max(0f, statusDuration);
            hud.ShowStatus(statusId, statusIcon, dura);
        }

        _applied = true;
    }

    void RemoveIfApplied()
    {
        if (!_applied || !playerHealth) return;
        playerHealth.RemoveMaxHPModifier(MOD_ID);
        if (hud) hud.ClearStatus(statusId); // ★ 离开 Happy 清图标
        _applied = false;
    }
}

