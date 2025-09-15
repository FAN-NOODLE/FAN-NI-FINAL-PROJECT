using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System;
using System.Reflection;
using System.Collections;
using System.Collections.Generic;

/// <summary>
/// 屏幕中心情绪提示：
/// - 每种情绪支持多条文案，随机抽取（可避免连续重复）
/// - 使用 CanvasGroup 淡入/停留/淡出
/// - 背景 Image 自动根据文字尺寸自适应（白色半透明，可自定义）
/// - 颜色可按情绪染色
/// - 事件订阅：优先强类型 DummyEmotionFeed.OnEmotionChanged，其次反射兜底
/// </summary>
public class EmotionToastUI : MonoBehaviour
{
    [Header("UI Refs")]
    public TextMeshProUGUI messageText;   // 居中的 TMP 文本
    public CanvasGroup group;              // 淡入淡出
    public Image backdrop;                 // 半透明背景（建议在同一物体下做一个 Image）


    [Header("Layout Constraints")]
public float maxTextWidth = 520f;     // 文案最大显示宽度（像素，按你的 HUD 尺寸调整）
public bool enableWordWrap = true;   

    [Header("Emotion Source")]
    public MonoBehaviour emotionManager;   // 不填会自动找 DummyEmotionFeed
    [Range(0f,1f)] public float minConfidence = 0.65f;
    public bool onlyWhenChanged = true;    // 仅在情绪标签变化时弹出

    [Header("Messages (multiple lines per emotion)")]
    [TextArea] public string[] excitedMsgs = {
        "兴奋让你的攻击力提高了！",
        "你热血沸腾，伤害翻倍！",
        "火力全开：每次攻击都更狠了！"
    };
    [TextArea] public string[] happyMsgs = {
        "快乐眷顾你，幸运加成已生效！",
        "心情明媚，前路似乎更顺了。",
        "好心情，隐藏机制悄然倾斜！"
    };
    [TextArea] public string[] anxiousMsgs = {
        "焦虑被接住：提升生命上限并小幅回复。",
        "深呼吸……系统已加强你的生命值。",
        "别慌，体魄强化已就位。"
    };
    [TextArea] public string[] sadMsgs = {
        "别难过：敌人停手了，给你一点缓冲。",
        "你被温柔对待，危机暂缓。",
        "世界放慢了脚步，给你时间整理心情。"
    };
    [TextArea] public string[] calmMsgs = {
        "平静流淌，舒缓乐章响起。",
        "风平浪静，节奏回归均衡。",
        "安宁环绕，你可以从容探索。"
    };

    [Tooltip("若数组为空，使用这一行作为兜底文案")]
    public string fallbackExcited = "兴奋让你的攻击力提高了！";
    public string fallbackHappy   = "快乐点亮了你的运气！";
    public string fallbackAnxious = "焦虑被理解：提高上限并小幅回血。";
    public string fallbackSad     = "别难过，敌人停手了。";
    public string fallbackCalm    = "平静环绕，音乐舒缓。";

    [Header("Picker Options")]
    public bool avoidImmediateRepeat = true;      // 避免同情绪连续抽到同一句
    private System.Random _rng = new System.Random();
    private Dictionary<string,int> _lastPickIndex = new Dictionary<string,int>();

    [Header("Colors (optional)")]
    public bool tintByEmotion = true;             // 文案颜色按情绪染色
    public bool tintBackdropByEmotion = false;    // 背景是否也按情绪轻微染色
    [Range(0f,1f)] public float backdropTintStrength = 0.25f;

    public Color excitedColor = new Color(1.00f, 0.60f, 0.20f);
    public Color happyColor   = new Color(1.00f, 0.85f, 0.20f);
    public Color anxiousColor = new Color(0.35f, 0.75f, 1.00f);
    public Color sadColor     = new Color(0.55f, 0.60f, 1.00f);
    public Color calmColor    = new Color(0.70f, 0.95f, 0.85f);
    public Color defaultTextColor = Color.white;

    [Header("Backdrop (Auto-size)")]
    public bool autoSizeBackdrop = true;                       // 背景自动适配文字
    public Vector2 backdropPadding = new Vector2(40f, 20f);    // 左右/上下留白
    public Color backdropBaseColor = new Color(1f, 1f, 1f, 0.4f); // 白色半透明

    [Header("Animation")]
    public float fadeIn = 0.15f;
    public float hold   = 1.20f;
    public float fadeOut= 0.35f;

    // —— 订阅缓存（强类型优先，反射兜底）——
    private DummyEmotionFeed _typedFeed;
    private EventInfo _reflEvent;
    private Delegate _reflHandler;

    private string _lastLabel = null;
    private Coroutine _showCo;

    // ================= 生命周期 =================
    void Awake()
{
    if (!group) group = GetComponent<CanvasGroup>();
    if (!group) group = gameObject.AddComponent<CanvasGroup>();
    if (!messageText) messageText = GetComponentInChildren<TextMeshProUGUI>(true);

    if (backdrop)
    {
        backdrop.color = backdropBaseColor;
        backdrop.raycastTarget = false;
    }

    // ★ 关键：给文本设定最大宽度 + 开启换行，禁止 Best Fit / Auto Size
    if (messageText)
    {
        messageText.enableWordWrapping = enableWordWrap;
        messageText.enableAutoSizing = false; // 避免字体大小自动缩放导致测量抖动

        var rt = messageText.rectTransform;
        rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
        rt.pivot = new Vector2(0.5f, 0.5f);
        // 固定一个最大宽度（高度由内容撑开）
        rt.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, maxTextWidth);
    }

    group.alpha = 0f;
    if (messageText) messageText.text = "";
}


    void Start()
    {
        TryHookEmotionEvents();
    }

    void OnDestroy()
    {
        TryUnsubscribe();
    }

    // ================= 事件订阅 =================
    void TryHookEmotionEvents()
    {
        if (!emotionManager)
        {
            var feed = FindObjectOfType<DummyEmotionFeed>(true);
            if (feed) emotionManager = feed;
        }
        if (!emotionManager) return;

        // 强类型优先
        _typedFeed = emotionManager as DummyEmotionFeed;
        if (_typedFeed != null)
        {
            _typedFeed.OnEmotionChanged += OnEmotionTyped;
            return;
        }

        // 反射兜底
        _reflEvent = emotionManager.GetType().GetEvent("OnEmotionChanged");
        if (_reflEvent == null) return;

        _reflHandler = Delegate.CreateDelegate(_reflEvent.EventHandlerType, this,
            nameof(OnEmotionReflected));
        _reflEvent.AddEventHandler(emotionManager, _reflHandler);
    }

    void TryUnsubscribe()
    {
        if (_typedFeed != null)
        {
            _typedFeed.OnEmotionChanged -= OnEmotionTyped;
            _typedFeed = null;
        }
        if (_reflEvent != null && _reflHandler != null && emotionManager)
        {
            _reflEvent.RemoveEventHandler(emotionManager, _reflHandler);
        }
        _reflEvent = null;
        _reflHandler = null;
    }

    // 强类型回调
    void OnEmotionTyped(DummyEmotionFeed.EmotionEvent e)
    {
        HandleLabelConfidence(e.label, e.confidence);
    }

    // 反射回调
    void OnEmotionReflected(object boxed)
    {
        if (TryExtractLabelConfidence(boxed, out var label, out var conf))
            HandleLabelConfidence(label, conf);
    }

    static bool TryExtractLabelConfidence(object obj, out string label, out float confidence)
    {
        label = null; confidence = 1f;
        if (obj == null) return false;

        const BindingFlags F = BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase;
        var t = obj.GetType();

        var fLabel = t.GetField("label", F);
        if (fLabel != null) label = fLabel.GetValue(obj) as string;
        if (label == null)
        {
            var pLabel = t.GetProperty("label", F) ?? t.GetProperty("Label", F);
            if (pLabel != null) label = pLabel.GetValue(obj) as string;
        }

        var fConf = t.GetField("confidence", F);
        if (fConf != null) confidence = Convert.ToSingle(fConf.GetValue(obj));
        else
        {
            var pConf = t.GetProperty("confidence", F) ?? t.GetProperty("Confidence", F);
            if (pConf != null) confidence = Convert.ToSingle(pConf.GetValue(obj));
        }

        return !string.IsNullOrEmpty(label);
    }

    // ================= 逻辑处理 =================
    void HandleLabelConfidence(string label, float confidence)
    {
        if (string.IsNullOrEmpty(label)) return;
        if (confidence < minConfidence) return;
        if (onlyWhenChanged && string.Equals(_lastLabel, label, StringComparison.OrdinalIgnoreCase)) return;

        _lastLabel = label;

        string msg = PickMessage(label);
        if (string.IsNullOrEmpty(msg)) return;

        Color textCol = GetColorFor(label);
        Color bgCol   = ComputeBackdropColor(label);

        Show(msg, textCol, bgCol);
    }

    string PickMessage(string labelAnyCase)
    {
        string l = labelAnyCase.ToLowerInvariant();
        string[] pool = null;
        string fallback = "";

        switch (l)
        {
            case "excited": pool = excitedMsgs; fallback = fallbackExcited; break;
            case "happy":   pool = happyMsgs;   fallback = fallbackHappy;   break;
            case "anxious": pool = anxiousMsgs; fallback = fallbackAnxious; break;
            case "sad":     pool = sadMsgs;     fallback = fallbackSad;     break;
            case "calm":    pool = calmMsgs;    fallback = fallbackCalm;    break;
            default:        return "";
        }

        if (pool == null || pool.Length == 0)
            return fallback;

        int lastIdx = -1;
        _lastPickIndex.TryGetValue(l, out lastIdx);

        int pick = 0;
        if (pool.Length == 1) pick = 0;
        else
        {
            const int maxTries = 4;
            int tries = 0;
            do
            {
                pick = _rng.Next(0, pool.Length);
                tries++;
            }
            while (avoidImmediateRepeat && pick == lastIdx && tries < maxTries);
        }

        _lastPickIndex[l] = pick;
        return pool[pick];
    }

    Color GetColorFor(string labelAnyCase)
    {
        if (!tintByEmotion || messageText == null) return defaultTextColor;
        string l = labelAnyCase.ToLowerInvariant();
        switch (l)
        {
            case "excited": return excitedColor;
            case "happy":   return happyColor;
            case "anxious": return anxiousColor;
            case "sad":     return sadColor;
            case "calm":    return calmColor;
            default:        return defaultTextColor;
        }
    }

    Color ComputeBackdropColor(string labelAnyCase)
    {
        if (!backdrop) return backdropBaseColor;
        if (!tintBackdropByEmotion) return backdropBaseColor;

        Color emo = GetColorFor(labelAnyCase);
        // 只用颜色的色相/亮度做轻微调和，保留半透明基底
        Color mixed = Color.Lerp(backdropBaseColor, new Color(emo.r, emo.g, emo.b, backdropBaseColor.a), backdropTintStrength);
        mixed.a = backdropBaseColor.a; // 固定 alpha 由基底控制
        return mixed;
    }

    // ================= 显示与动画 =================
    public void Show(string msg, Color textColor, Color backgroundColor)
    {
        if (!messageText || !group) return;

        if (_showCo != null) StopCoroutine(_showCo);
        _showCo = StartCoroutine(ShowCo(msg, textColor, backgroundColor));
    }

    IEnumerator ShowCo(string msg, Color textColor, Color backgroundColor)
    {
        // 设置文字与颜色
        messageText.text = msg;
        messageText.color = textColor;

        // 背景颜色与自适配
        if (backdrop)
        {
            backdrop.color = backgroundColor;
            if (autoSizeBackdrop) UpdateBackdropSize();
        }

        // 淡入
        float t = 0f;
        while (t < fadeIn)
        {
            t += Time.unscaledDeltaTime;
            group.alpha = Mathf.Lerp(0f, 1f, t / fadeIn);
            yield return null;
        }
        group.alpha = 1f;

        // 停留
        yield return new WaitForSecondsRealtime(hold);

        // 淡出
        t = 0f;
        while (t < fadeOut)
        {
            t += Time.unscaledDeltaTime;
            group.alpha = Mathf.Lerp(1f, 0f, t / fadeOut);
            yield return null;
        }
        group.alpha = 0f;

        messageText.text = "";
        _showCo = null;
    }

    void UpdateBackdropSize()
{
    if (!backdrop || !messageText) return;

    var rtText = messageText.rectTransform;

    // 强制更新网格和布局，确保拿到最新的渲染尺寸
    messageText.ForceMeshUpdate();
    LayoutRebuilder.ForceRebuildLayoutImmediate(rtText);

    // TextMesh Pro 的实际渲染边界（本地坐标尺寸）
    Vector2 boundsSize = messageText.textBounds.size;

    // 水平方向：不要超过我们给定的最大显示宽度
    float renderWidth  = Mathf.Min(boundsSize.x, rtText.rect.width);
    // 若内容很短，boundsSize.x 可能非常小，这里兜底至少用当前 Rect 的最小需要宽度
    if (renderWidth <= 0f) renderWidth = Mathf.Min(messageText.preferredWidth, rtText.rect.width);

    // 垂直方向：使用实际渲染的高度
    float renderHeight = Mathf.Max(boundsSize.y, messageText.preferredHeight);

    // 设置背景尺寸（加上左右/上下留白）
    var rtBg = backdrop.rectTransform;
    rtBg.anchorMin = rtBg.anchorMax = new Vector2(0.5f, 0.5f);
    rtBg.pivot = new Vector2(0.5f, 0.5f);
    rtBg.anchoredPosition = Vector2.zero;
    rtBg.sizeDelta = new Vector2(renderWidth + backdropPadding.x, renderHeight + backdropPadding.y);
}

}



