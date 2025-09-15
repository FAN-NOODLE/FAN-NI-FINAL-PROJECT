using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System;
using System.Reflection;
using System.Collections;
using System.Collections.Generic;

public class HUDPlayerUI : MonoBehaviour
{
    [Header("Refs")]
    public Image emotionIcon;               // 情绪图标
    public Image hpFill;                    // 血条填充图片（会被自动设置为 Filled/Horizontal）
    public TextMeshProUGUI hpText;         // "5/5" 文字
    public Transform statusArea;            // 状态图标容器
    public GameObject statusIconPrefab;

    [Header("HP Bar Resizing")]
    public bool resizeBarWithMax = true;           // 开启后，MaxHP 变大会拉长血条
    public RectTransform hpBarContainer;           // 可选：血条外框/背景的 RectTransform
    public RectTransform hpFillRect;               // 可选：hpFill 的 RectTransform（不填自动取）
    public RectTransform hpBackgroundRect;         // 新增：血条背景的 RectTransform（用于显示被攻击减少的部分）
    public int baselineMaxHpForWidth = 5;          // 基准 MaxHP（建议填你初始上限，例如 5）
    public float baselineBarWidth = -1f;           // 基准宽度（不填会在 Awake 里自动记录）
    public float resizeDuration = 0.3f;            // 血条长度变化动画时长

    [Header("Background & Border Refs")]
    public Image avatarBackground;          // 背景板（可跟随情绪轻微变色）
    public Image fixedBorder;               // 固定边框（不会变）

    [Header("Emotion Visuals (Outline mode)")]
    public EmotionVisualConfig emotionConfig;
    [Tooltip("优先使用这里指定的 Outline；若为空会自动在 emotionIcon 上添加/获取")]
    public Outline emotionOutline;
    [Tooltip("描边粗细（像素，通常(3,-3)）")]
    public Vector2 outlineEffectDistance = new Vector2(3f, -3f);
    [Tooltip("描边颜色的平滑过渡时长（秒），0=立即切换")]
    public float emotionColorLerp = 0.2f;
    [Tooltip("将置信度映射到描边 Alpha（0~1），用于强弱表现")]
    public bool useConfidenceOnAlpha = true;
    [Range(0f, 1f)] public float minBorderAlpha = 0.45f;
    [Range(0f, 1f)] public float maxBorderAlpha = 1.0f;

    [Header("Background Color Settings")]
    public bool changeBackgroundColor = true;
    [Range(0f, 1f)] public float backgroundColorIntensity = 0.3f;
    public Color defaultBackgroundColor = new Color(0.2f, 0.2f, 0.2f, 0.8f);

    [Header("Emotion Effects")]
    public EmotionEffect_Anxious emotionEffectAnxious;  // 引用 Anxious 情绪效果脚本
    public EmotionEffect_Sad emotionEffectSad;          // 引用 Sad 情绪效果脚本

    [Header("Data Sources")]
    public MonoBehaviour emotionManager;    // 可选：你的 EmotionManager（含 OnEmotionChanged）
    public PlayerHealth playerHealth;       // 建议手动拖；留空则自动查找
    [Tooltip("自动用 Tag=Player 去绑定 PlayerHealth")]
    public bool autoBindPlayerByTag = true;
    public string playerTag = "Player";

    [Header("Update Options")]
    [Tooltip("每帧轮询一次 HP（即使有事件），双保险")]
    public bool alwaysPollHP = true;

    // —— 内部 —— //
    int lastHP = -1, lastMax = -1;
    readonly Dictionary<string, StatusIconUI> _status = new Dictionary<string, StatusIconUI>();
    Coroutine _emotionColorCo, _backgroundColorCo, _resizeBarCo;

    // —— Emotion 订阅缓存 —— //
    DummyEmotionFeed _typedFeed;
    EventInfo _reflEvent;
    Delegate _reflHandler;

    // ========== 生命周期 ==========
    void Awake()
    {
        EnsureOutline();
        EnsureHPFillIsConfigured();
        if (avatarBackground) avatarBackground.color = defaultBackgroundColor;

        // ★ 记录基准宽度
        if (!hpFillRect && hpFill) hpFillRect = hpFill.rectTransform;
        if (hpFillRect && baselineBarWidth <= 0f)
            baselineBarWidth = hpFillRect.sizeDelta.x;

        // 确保文字对齐方式正确
        if (hpText)
        {
            hpText.horizontalAlignment = HorizontalAlignmentOptions.Center;
            hpText.verticalAlignment = VerticalAlignmentOptions.Middle;
        }
    }

    void Start()
    {
        EnsurePlayerHealthBinding();
        TryHookHealthEvents();
        TryHookEmotionEvents();

        SetEmotion(EmotionType.Calm, 1f);
        RefreshHPUI(force: true);
    }

    void OnDestroy()
    {
        // 取消 HP 事件订阅
        if (playerHealth != null) playerHealth.OnHPChanged -= UpdateHP;

        // 取消 Emotion 事件订阅
        if (_typedFeed != null)
        {
            _typedFeed.OnEmotionChanged -= OnEmotionEventTyped;
            _typedFeed = null;
        }
        if (_reflEvent != null && _reflHandler != null && emotionManager)
        {
            _reflEvent.RemoveEventHandler(emotionManager, _reflHandler);
        }
        _reflEvent = null;
        _reflHandler = null;
    }

    void Update()
    {
        if (alwaysPollHP) RefreshHPUI();

        // 若要本脚本本地测试按键，解开下面注释（建议用 DummyEmotionFeed 统一发事件）
        // if (Input.GetKeyDown(KeyCode.Alpha1)) SetEmotion(EmotionType.Excited, 0.9f);
        // if (Input.GetKeyDown(KeyCode.Alpha2)) SetEmotion(EmotionType.Happy,   0.9f);
        // if (Input.GetKeyDown(KeyCode.Alpha3)) SetEmotion(EmotionType.Anxious, 0.9f);
        // if (Input.GetKeyDown(KeyCode.Alpha4)) SetEmotion(EmotionType.Sad,     0.9f);
        // if (Input.GetKeyDown(KeyCode.Alpha5)) SetEmotion(EmotionType.Calm,    0.9f);
    }

    // ========== 绑定 PlayerHealth ==========
    void EnsurePlayerHealthBinding()
    {
        if (playerHealth && playerHealth.isActiveAndEnabled) return;

        if (autoBindPlayerByTag)
        {
            var go = GameObject.FindGameObjectWithTag(playerTag);
            if (go)
            {
                var ph = go.GetComponentInChildren<PlayerHealth>(true);
                if (!ph) ph = go.GetComponent<PlayerHealth>();
                if (ph) playerHealth = ph;
            }
        }

        if (!playerHealth)
        {
            var all = FindObjectsOfType<PlayerHealth>(true);
            if (all.Length > 0) playerHealth = all[0];
        }

        if (!playerHealth)
            Debug.LogWarning("[HUDPlayerUI] 未找到 PlayerHealth。请在 HUDPlayerUI 的 Player Health 槽手动拖入，或给玩家设置 Tag=Player。");
    }

    void TryHookHealthEvents()
    {
        if (!playerHealth) return;
        // 你的 PlayerHealth 需有 public event Action<int,int> OnHPChanged
        playerHealth.OnHPChanged += UpdateHP;
    }

    // ========== HP 刷新 ==========
    public void RefreshHPUI(bool force = false)
    {
        EnsureHPFillIsConfigured();
        if (!playerHealth)
        {
            EnsurePlayerHealthBinding();
            if (!playerHealth) return;
        }

        int cur = playerHealth.HP;
        int max = playerHealth.MaxHP;

        if (force || cur != lastHP || max != lastMax)
            UpdateHP(cur, max);
    }

    void UpdateHP(int cur, int max)
    {
        // 检查最大HP是否变化，如有变化则调整血条长度
        if (resizeBarWithMax && (max != lastMax)) 
        {
            ResizeHpBarForMax(max);
        }
        
        lastHP = cur; 
        lastMax = max;

        float pct = Mathf.Clamp01(max > 0 ? (float)cur / max : 0f);
        if (hpFill) hpFill.fillAmount = pct;
        if (hpText) hpText.text = $"{cur}/{max}";

        // 低血量背景提醒（可选）
        if (avatarBackground)
        {
            if (pct < 0.3f)
            {
                Color low = Color.Lerp(defaultBackgroundColor, Color.red, 0.5f);
                SetBackgroundColor(low, 0.25f);
            }
            else
            {
                SetBackgroundColor(defaultBackgroundColor, 0.25f);
            }
        }
    }

    // 自动把血条配置为 Filled/Horizontal/Origin Left
    void EnsureHPFillIsConfigured()
    {
        if (!hpFill) return;
        if (hpFill.type != Image.Type.Filled) hpFill.type = Image.Type.Filled;
        if (hpFill.fillMethod != Image.FillMethod.Horizontal) hpFill.fillMethod = Image.FillMethod.Horizontal;
        if (hpFill.fillOrigin != 0) hpFill.fillOrigin = 0; // Left
        if (hpFill.color.a < 0.99f) hpFill.color = new Color(hpFill.color.r, hpFill.color.g, hpFill.color.b, 1f);
    }

    public void SetBackgroundColor(Color color, float lerpDuration = 0f)
    {
        if (!avatarBackground) return;
        if (_backgroundColorCo != null) StopCoroutine(_backgroundColorCo);
        if (lerpDuration <= 0.01f) avatarBackground.color = color;
        else _backgroundColorCo = StartCoroutine(LerpBackgroundColor(color, lerpDuration));
    }

    IEnumerator LerpBackgroundColor(Color target, float dur)
    {
        Color start = avatarBackground.color;
        float t = 0f;
        while (t < dur)
        {
            t += Time.unscaledDeltaTime;
            avatarBackground.color = Color.Lerp(start, target, t / dur);
            yield return null;
        }
        avatarBackground.color = target;
    }

    // ========== Emotion（Outline 变色 + 置信度→Alpha） ==========
    public void SetEmotion(EmotionType type, float confidence)
    {
        EnsureOutline();

        if (emotionConfig && emotionIcon)
        {
            // 图标
            var icon = emotionConfig.GetIcon(type);
            if (emotionIcon)
            {
                emotionIcon.sprite = icon;
                emotionIcon.enabled = (icon != null);
            }

            // 颜色
            var target = emotionConfig.GetColor(type);

            // 置信度映射到 alpha
            if (useConfidenceOnAlpha)
            {
                float a = Mathf.Lerp(minBorderAlpha, maxBorderAlpha, Mathf.Clamp01(confidence));
                target.a = a;
            }

            // 过渡
            if (_emotionColorCo != null) StopCoroutine(_emotionColorCo);
            if (emotionColorLerp <= 0.01f) ApplyOutlineColor(target);
            else _emotionColorCo = StartCoroutine(LerpOutlineColor(target, emotionColorLerp));

            // 背景色（可选）
            if (changeBackgroundColor && avatarBackground)
            {
                Color bgTarget = Color.Lerp(defaultBackgroundColor, target,
                    backgroundColorIntensity * Mathf.Clamp01(confidence));
                if (_backgroundColorCo != null) StopCoroutine(_backgroundColorCo);
                if (emotionColorLerp <= 0.01f) avatarBackground.color = bgTarget;
                else _backgroundColorCo = StartCoroutine(LerpBackgroundColor(bgTarget, emotionColorLerp));
            }
        }
        else
        {
            if (emotionIcon) emotionIcon.enabled = false;
        }

        // 触发情绪效果
        TriggerEmotionEffects(type);
    }

    // 触发情绪效果
    private void TriggerEmotionEffects(EmotionType type)
    {
        // 触发 Anxious 情绪效果
        if (emotionEffectAnxious != null)
        {
            emotionEffectAnxious.OnEmotionChanged(type);
        }
        
        // 触发 Sad 情绪效果
        if (emotionEffectSad != null)
        {
            emotionEffectSad.OnEmotionChanged(type);
        }
    }

    public void SetEmotionByLabel(string label, float confidence)
    {
        if (string.IsNullOrEmpty(label)) return;
        label = label.ToLowerInvariant();
        EmotionType t = label switch
        {
            "excited" => EmotionType.Excited,
            "happy"   => EmotionType.Happy,
            "anxious" => EmotionType.Anxious,
            "sad"     => EmotionType.Sad,
            "calm"    => EmotionType.Calm,
            _ => EmotionType.Calm
        };
        SetEmotion(t, confidence);
    }

    // ========== Emotion 订阅：强类型优先，反射兜底 ==========
    void TryHookEmotionEvents()
    {
        // 若没手动拖引用，自动找场景里的 DummyEmotionFeed（或以后换成真正 EmotionManager）
        if (!emotionManager)
        {
            var feed = FindObjectOfType<DummyEmotionFeed>(true);
            if (feed) emotionManager = feed;
        }
        if (!emotionManager) return;

        // ① 强类型：如果是 DummyEmotionFeed，直接订阅
        _typedFeed = emotionManager as DummyEmotionFeed;
        if (_typedFeed != null)
        {
            _typedFeed.OnEmotionChanged += OnEmotionEventTyped;
            return;
        }

        // ② 反射兜底：找 OnEmotionChanged 事件（Action<T>）
        _reflEvent = emotionManager.GetType().GetEvent("OnEmotionChanged");
        if (_reflEvent == null) return;

        // 注意：目标事件的委托签名是 Action<T>，我们用 object 参数的方法来兼容装箱
        _reflHandler = Delegate.CreateDelegate(_reflEvent.EventHandlerType, this,
            nameof(OnEmotionEventReflected));
        _reflEvent.AddEventHandler(emotionManager, _reflHandler);
    }

    // —— 强类型回调（DummyEmotionFeed）——
    void OnEmotionEventTyped(DummyEmotionFeed.EmotionEvent e)
    {
        SetEmotionByLabel(e.label, e.confidence);
    }

    // —— 反射兜底回调 —— 
    void OnEmotionEventReflected(object boxed)
    {
        if (TryExtractLabelConfidence(boxed, out string label, out float conf))
            SetEmotionByLabel(label, conf);
        else
            Debug.LogWarning("[HUDPlayerUI] 无法从事件参数解析 label/confidence。");
    }

    // 解析工具：支持字段/属性、忽略大小写；失败不抛异常
    static bool TryExtractLabelConfidence(object obj, out string label, out float confidence)
    {
        label = null; confidence = 1f; // 默认 1
        if (obj == null) return false;

        const BindingFlags F = BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase;
        var t = obj.GetType();

        // label
        var fLabel = t.GetField("label", F);
        if (fLabel != null) label = fLabel.GetValue(obj) as string;
        if (label == null)
        {
            var pLabel = t.GetProperty("label", F) ?? t.GetProperty("Label", F);
            if (pLabel != null) label = pLabel.GetValue(obj) as string;
        }

        // confidence
        var fConf = t.GetField("confidence", F);
        if (fConf != null) confidence = Convert.ToSingle(fConf.GetValue(obj));
        else
        {
            var pConf = t.GetProperty("confidence", F) ?? t.GetProperty("Confidence", F);
            if (pConf != null) confidence = Convert.ToSingle(pConf.GetValue(obj));
        }

        return !string.IsNullOrEmpty(label);
    }

    IEnumerator LerpOutlineColor(Color target, float dur)
    {
        Color start = GetCurrentOutlineColor();
        float t = 0f;
        while (t < dur)
        {
            t += Time.unscaledDeltaTime;
            ApplyOutlineColor(Color.Lerp(start, target, t / dur));
            yield return null;
        }
        ApplyOutlineColor(target);
    }

    Color GetCurrentOutlineColor()
    {
        return emotionOutline ? emotionOutline.effectColor : Color.white;
    }

    void ApplyOutlineColor(Color c)
    {
        if (!emotionOutline) return;
        emotionOutline.effectColor = c;
        if (emotionOutline.effectDistance != outlineEffectDistance)
            emotionOutline.effectDistance = outlineEffectDistance;

        // 确保图标自身 alpha = 1，否则 Outline 可能被乘淡
        if (emotionIcon && emotionIcon.color.a < 0.99f)
        {
            var k = emotionIcon.color; k.a = 1f; emotionIcon.color = k;
        }
    }

    void EnsureOutline()
    {
        if (!emotionIcon) return;
        if (!emotionOutline)
        {
            emotionOutline = emotionIcon.GetComponent<Outline>();
            if (!emotionOutline) emotionOutline = emotionIcon.gameObject.AddComponent<Outline>();
        }
        emotionOutline.useGraphicAlpha = true;
        emotionOutline.effectDistance  = outlineEffectDistance;
    }

    // ========== 状态图标（可选） ==========
    public void ShowStatus(string id, Sprite icon, float durationSeconds)
    {
        if (!statusArea || !statusIconPrefab) return;
        if (_status.TryGetValue(id, out var exist))
        {
            exist.RefreshDuration(durationSeconds);
            return;
        }
        var go = Instantiate(statusIconPrefab, statusArea);
        var ui = go.GetComponent<StatusIconUI>();
        ui.Setup(id, icon, durationSeconds);
        _status[id] = ui;
    }

    public void ClearStatus(string id)
    {
        if (_status.TryGetValue(id, out var ui) && ui)
        {
            Destroy(ui.gameObject);
        }
        _status.Remove(id);
    }

    // ========== 新增：血条长度调整功能 ==========
    void ResizeHpBarForMax(int max)
    {
        if (!resizeBarWithMax || max <= 0) return;
        if (!hpFillRect) { if (hpFill) hpFillRect = hpFill.rectTransform; else return; }

        // 自动用第一次观察到的 MaxHP 作为基准（如果你没在 Inspector 填写）
        if (baselineMaxHpForWidth <= 0) baselineMaxHpForWidth = max;
        if (baselineBarWidth <= 0f) baselineBarWidth = hpFillRect.sizeDelta.x;

        float k = Mathf.Max(0.1f, (float)max / baselineMaxHpForWidth);
        float targetWidth = baselineBarWidth * k;

        // 停止之前的动画（如果有）
        if (_resizeBarCo != null) StopCoroutine(_resizeBarCo);
        
        // 启动新的动画
        _resizeBarCo = StartCoroutine(AnimateHpBarResize(targetWidth, resizeDuration));
    }

    IEnumerator AnimateHpBarResize(float targetWidth, float duration)
    {
        // 获取文字对象的 RectTransform
        RectTransform hpTextRect = hpText ? hpText.GetComponent<RectTransform>() : null;
        float startTextWidth = hpTextRect ? hpTextRect.sizeDelta.x : 0f;
        
        // 计算文字的目标宽度（使用与血条相同的宽度）
        float targetTextWidth = targetWidth;

        // 记录初始宽度
        float startWidthFill = hpFillRect.sizeDelta.x;
        float startWidthContainer = hpBarContainer ? hpBarContainer.sizeDelta.x : startWidthFill;
        float startWidthBackground = hpBackgroundRect ? hpBackgroundRect.sizeDelta.x : startWidthFill;
        
        float elapsed = 0f;
        
        while (elapsed < duration)
        {
            elapsed += Time.unscaledDeltaTime;
            float t = Mathf.Clamp01(elapsed / duration);
            
            float easedT = EaseOutQuad(t);
            float currentWidth = Mathf.Lerp(startWidthFill, targetWidth, easedT);
            float currentTextWidth = Mathf.Lerp(startTextWidth, targetTextWidth, easedT);
            
            // 更新填充条宽度
            var size = hpFillRect.sizeDelta;
            size.x = currentWidth;
            hpFillRect.sizeDelta = size;

            // 同步更新文字宽度
            if (hpTextRect)
            {
                var textSize = hpTextRect.sizeDelta;
                textSize.x = currentTextWidth;
                hpTextRect.sizeDelta = textSize;
                
                // 确保文字内容始终居中显示
                hpText.horizontalAlignment = HorizontalAlignmentOptions.Center;
                hpText.verticalAlignment = VerticalAlignmentOptions.Middle;
            }

            // 同步更新外框宽度
            if (hpBarContainer)
            {
                var s2 = hpBarContainer.sizeDelta;
                s2.x = Mathf.Lerp(startWidthContainer, targetWidth, easedT);
                hpBarContainer.sizeDelta = s2;
            }
            
            // 同步更新血条背景宽度
            if (hpBackgroundRect)
            {
                var s3 = hpBackgroundRect.sizeDelta;
                s3.x = Mathf.Lerp(startWidthBackground, targetWidth, easedT);
                hpBackgroundRect.sizeDelta = s3;
            }
            
            yield return null;
        }
        
        // 确保最终尺寸准确
        var finalSize = hpFillRect.sizeDelta;
        finalSize.x = targetWidth;
        hpFillRect.sizeDelta = finalSize;

        // 确保文字最终宽度
        if (hpTextRect)
        {
            var textFinalSize = hpTextRect.sizeDelta;
            textFinalSize.x = targetTextWidth;
            hpTextRect.sizeDelta = textFinalSize;
            
            // 最终确认文字对齐方式
            hpText.horizontalAlignment = HorizontalAlignmentOptions.Center;
            hpText.verticalAlignment = VerticalAlignmentOptions.Middle;
        }

        if (hpBarContainer)
        {
            var finalSize2 = hpBarContainer.sizeDelta;
            finalSize2.x = targetWidth;
            hpBarContainer.sizeDelta = finalSize2;
        }
        
        if (hpBackgroundRect)
        {
            var finalSize3 = hpBackgroundRect.sizeDelta;
            finalSize3.x = targetWidth;
            hpBackgroundRect.sizeDelta = finalSize3;
        }
    }
    
    // 缓动函数：二次缓出
    float EaseOutQuad(float t)
    {
        return t * (2 - t);
    }
}