using UnityEngine;
using TMPro;
using UnityEngine.UI;
using System.Collections;

public class SpeechBubble2D : MonoBehaviour
{
    public enum BubbleRenderMode { Auto, WorldSpaceCanvas, ScreenSpaceFollow }

    [Header("Refs")]
    public RectTransform root;             // 对话框根（带 Image 背景）
    public TextMeshProUGUI text;           // 文本
    public Image background;               // 半透明白背景
    public Transform anchor;               // 绑敌人的头部/躯干
    public Vector3 worldOffset = new Vector3(0f, 1.6f, 0f);

    [Header("Style")]
    [Range(0,1)] public float bgAlpha = 0.65f;
    public float typewriterCharsPerSec = 0.5f;
    public float fadeDuration = 0.15f;

    [Header("Render Mode")]
    public BubbleRenderMode renderMode = BubbleRenderMode.Auto;

    [Header("World Space Canvas Settings")]
    public string sortingLayerName = "TEXT";
    public int sortingOrder = 500;

    Camera _cam;
    Canvas _worldCanvas;             // 本地世界空间画布
    Canvas _topCanvas;               // 场景中最顶层的屏幕画布（Overlay优先）
    CanvasGroup _canvasGroup;
    Coroutine _showCo;

    // 缓存：Screen Space 定位用
    RectTransform _topCanvasRT;
    bool _useScreenSpace;

    void Awake()
    {
        _cam = Camera.main;
        if (!_cam) Debug.LogWarning("[SpeechBubble2D] Main Camera not found.");

        // 组建 CanvasGroup
        if (root)
        {
            _canvasGroup = root.GetComponent<CanvasGroup>();
            if (!_canvasGroup) _canvasGroup = root.gameObject.AddComponent<CanvasGroup>();
            _canvasGroup.alpha = 0f;
            root.gameObject.SetActive(false);
        }

        // 找到场景里“最上层”的屏幕 Canvas（Overlay 优先）
        _topCanvas = FindTopMostCanvas(out _topCanvasRT);

        // 决定渲染模式
        _useScreenSpace = DecideScreenSpace(renderMode, _topCanvas);

        if (_useScreenSpace)
        {
            // 将 root 放到最上层屏幕 Canvas 下（不会被遮住）
            EnsureRootOnTopCanvas();
        }
        else
        {
            // 使用世界空间 Canvas（带排序）
            EnsureWorldCanvas();
        }

        // 应用背景透明度
        if (background)
        {
            var c = background.color; c.a = bgAlpha; background.color = c;
        }
    }

    void LateUpdate()
    {
        if (!root || !anchor) return;

        if (_useScreenSpace)
        {
            // 屏幕空间：用屏幕坐标跟随
            Vector3 worldPos = anchor.position + worldOffset;
            Vector3 screenPos = _cam ? _cam.WorldToScreenPoint(worldPos) : worldPos;
            if (_topCanvas)
            {
                Vector2 localPos;
                var refCam = _topCanvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : _topCanvas.worldCamera;
                if (RectTransformUtility.ScreenPointToLocalPointInRectangle(_topCanvasRT, screenPos, refCam, out localPos))
                    root.anchoredPosition = localPos;
            }
        }
        else
        {
            // 世界空间：直接放到锚点处
            root.position = anchor.position + worldOffset;
            root.rotation = Quaternion.identity;
            // 统一缩放，避免远近被变形
            var rt = root.GetComponent<RectTransform>();
            if (rt) rt.localScale = Vector3.one;
        }
    }

    public void Show(string line, float holdSeconds, bool withTypewriter = true)
    {
        if (!root) { Debug.LogError("[SpeechBubble2D] root is null"); return; }
        if (_showCo != null) StopCoroutine(_showCo);
        _showCo = StartCoroutine(ShowCo(line, holdSeconds, withTypewriter));
    }

    IEnumerator ShowCo(string line, float hold, bool useTypewriter)
    {
        // 每次显示前，确保画布配置正确
        if (_useScreenSpace) EnsureRootOnTopCanvas();
        else EnsureWorldCanvas();

        root.gameObject.SetActive(true);
        yield return Fade(0f, 1f, fadeDuration);

        if (text)
        {
            if (useTypewriter && !string.IsNullOrEmpty(line))
            {
                text.text = "";
                float t = 0f; int total = line.Length;
                while (text.text.Length < total)
                {
                    t += Time.deltaTime * typewriterCharsPerSec;
                    int count = Mathf.Clamp(Mathf.FloorToInt(t), 0, total);
                    text.text = line.Substring(0, count);
                    yield return null;
                }
            }
            else text.text = line;
        }

        yield return new WaitForSeconds(hold);

        yield return Fade(1f, 0f, fadeDuration);
        root.gameObject.SetActive(false);
    }

    IEnumerator Fade(float from, float to, float dur)
    {
        if (!_canvasGroup) yield break;
        float t = 0f;
        while (t < dur)
        {
            t += Time.deltaTime;
            _canvasGroup.alpha = Mathf.Lerp(from, to, t / dur);
            yield return null;
        }
        _canvasGroup.alpha = to;
    }

    // —— 渲染模式/画布保障 —— //

    bool DecideScreenSpace(BubbleRenderMode mode, Canvas top)
    {
        if (mode == BubbleRenderMode.ScreenSpaceFollow) return true;
        if (mode == BubbleRenderMode.WorldSpaceCanvas)  return false;
        // Auto：如果场景有 Overlay/顶层屏幕 Canvas，就用 ScreenSpace（最不容易被遮挡）
        return top != null;
    }

    void EnsureWorldCanvas()
    {
        if (!_worldCanvas)
        {
            // 查找父级是否已有 Canvas
            _worldCanvas = GetComponentInParent<Canvas>();
            if (!_worldCanvas || _worldCanvas.renderMode != RenderMode.WorldSpace)
            {
                var go = new GameObject("BubbleCanvas(World)");
                go.transform.SetParent(transform, false);
                _worldCanvas = go.AddComponent<Canvas>();
                _worldCanvas.renderMode = RenderMode.WorldSpace;
                _worldCanvas.worldCamera = _cam;
                _worldCanvas.overrideSorting = true; // 关键：启用自定义排序
                _worldCanvas.sortingLayerName = sortingLayerName;
                _worldCanvas.sortingOrder = sortingOrder;
                go.AddComponent<GraphicRaycaster>();

                var crt = _worldCanvas.GetComponent<RectTransform>();
                crt.sizeDelta = new Vector2(3, 2);
                crt.localScale = Vector3.one * 0.01f;
            }
        }
        else
        {
            _worldCanvas.renderMode = RenderMode.WorldSpace;
            _worldCanvas.worldCamera = _cam;
            _worldCanvas.overrideSorting = true;
            _worldCanvas.sortingLayerName = sortingLayerName;
            _worldCanvas.sortingOrder = sortingOrder;
        }

        if (root && root.parent != _worldCanvas.transform)
            root.SetParent(_worldCanvas.transform, false);
    }

    void EnsureRootOnTopCanvas()
    {
        if (!_topCanvas || !_topCanvasRT)
        {
            _topCanvas = FindTopMostCanvas(out _topCanvasRT);
            if (!_topCanvas) { EnsureWorldCanvas(); _useScreenSpace = false; return; }
        }

        // 将 root 移到顶层 Canvas 下
        if (root && root.parent != _topCanvas.transform)
            root.SetParent(_topCanvas.transform, false);

        // Screen Space 下用 anchoredPosition 驱动，确保可见
        root.anchorMin = root.anchorMax = new Vector2(0.5f, 0.5f);
        root.pivot = new Vector2(0.5f, 0.5f);
        root.localScale = Vector3.one; // 不用 0.01 的世界缩放
    }

    // 找到最上层的屏幕 Canvas：优先 Overlay，其次 sortingOrder 最大的 ScreenSpace-Camera
    Canvas FindTopMostCanvas(out RectTransform rt)
    {
        rt = null;
        var all = FindObjectsOfType<Canvas>(true);
        Canvas overlay = null;
        Canvas cameraSpaceTop = null;
        int bestOrder = int.MinValue;

        foreach (var c in all)
        {
            if (!c.isActiveAndEnabled) continue;
            if (c.renderMode == RenderMode.ScreenSpaceOverlay)
            {
                // Overlay 总是最顶
                overlay = c; break;
            }
            if (c.renderMode == RenderMode.ScreenSpaceCamera)
            {
                // 取 sortingOrder 最大的
                int order = c.overrideSorting ? c.sortingOrder : 0;
                if (order > bestOrder) { bestOrder = order; cameraSpaceTop = c; }
            }
        }

        var picked = overlay != null ? overlay : cameraSpaceTop;
        rt = picked ? picked.transform as RectTransform : null;
        return picked;
    }

#if UNITY_EDITOR
    void OnValidate()
    {
        if (background)
        {
            var c = background.color; c.a = bgAlpha; background.color = c;
        }
    }
#endif
}
