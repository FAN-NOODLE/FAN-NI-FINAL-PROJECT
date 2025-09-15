// Assets/Scripts/Emotion/GeminiComfortLineProvider.cs
using UnityEngine;
using UnityEngine.Networking;
using System.Collections;
using Newtonsoft.Json;
using System;

public class GeminiComfortLineProvider : MonoBehaviour, IComfortLineProvider
{
    [Header("Gemini API")]
    [Tooltip("在 GCP 控制台获取的 API Key")]
    public string apiKey = "PASTE_YOUR_KEY_HERE";
    [Tooltip("模型名")]
    public string model = "gemini-2.0-flash";

    [Header("Behavior")]
    [Tooltip("两次请求之间的最小间隔（秒），避免频繁命中配额/限流")]
    public float minRequestInterval = 3f;
    [Tooltip("英文或中文提示的上限字符，过长会截断")]
    public int maxChars = 80;
    [Tooltip("兜底文案（网络失败或还没拿到时使用）")]
    [TextArea] public string fallbackLine = "你并不孤单，我在。";

    [Header("Prompt（可按需改）")]
    [TextArea(3,6)]
    public string systemInstruction = 
        "You are a gentle companion. Provide very brief, warm reassurance in English. Do not mention AI or use negative words. Respond in 5 words or less, like a short phrase such as It will be okay.Do not use quotation marks";

    // —— 缓存 —— //
    string _lastLine;
    float  _lastReqTime = -999f;
    Coroutine _fetchCo;

    void Start()
    {
        // 开场先拉一次
        Prefetch();
    }

    public void Prefetch()
    {
        if (Time.unscaledTime - _lastReqTime < minRequestInterval) return;
        if (_fetchCo != null) StopCoroutine(_fetchCo);
        _fetchCo = StartCoroutine(FetchOne());
    }

    public string GetLine()
    {
        // 优先返回缓存，同时尽量触发一次预取
        if (string.IsNullOrWhiteSpace(_lastLine))
            Prefetch();
        return string.IsNullOrWhiteSpace(_lastLine) ? fallbackLine : _lastLine;
    }

    IEnumerator FetchOne()
    {
        _lastReqTime = Time.unscaledTime;

        if (string.IsNullOrEmpty(apiKey))
        {
            Debug.LogWarning("[GeminiComfortLineProvider] API Key 未设置，返回兜底文案。");
            yield break;
        }

        string url = $"https://generativelanguage.googleapis.com/v1beta/models/{model}:generateContent?key={apiKey}";

        // Gemini 请求体
        var reqBody = new GenerateContentRequest
        {
            contents = new[]{
                new Content{
                    parts = new[]{ new Part{ text = systemInstruction } }
                }
            }
        };

        string json = JsonConvert.SerializeObject(reqBody);
        using (var uwr = new UnityWebRequest(url, "POST"))
        {
            byte[] bodyRaw = System.Text.Encoding.UTF8.GetBytes(json);
            uwr.uploadHandler   = new UploadHandlerRaw(bodyRaw);
            uwr.downloadHandler = new DownloadHandlerBuffer();
            uwr.SetRequestHeader("Content-Type", "application/json");
            // 也可以用 Header：X-goog-api-key，但 Query 参数更直观
            // uwr.SetRequestHeader("X-goog-api-key", apiKey);

            yield return uwr.SendWebRequest();

            if (uwr.result != UnityWebRequest.Result.Success)
            {
                Debug.LogWarning($"[GeminiComfortLineProvider] HTTP Error: {uwr.error}");
                yield break;
            }

            try
            {
                var resp = JsonConvert.DeserializeObject<GenerateContentResponse>(uwr.downloadHandler.text);
                string text = ExtractFirstText(resp);
                if (!string.IsNullOrWhiteSpace(text))
                {
                    text = text.Replace("\n", "").Replace("\r", "").Trim();
                    if (text.Length > maxChars) text = text.Substring(0, maxChars);
                    _lastLine = text;
                    // Debug.Log("[GeminiComfortLineProvider] line = " + _lastLine);
                }
                else
                {
                    Debug.LogWarning("[GeminiComfortLineProvider] 响应中没有可用文本，保留原缓存。");
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[GeminiComfortLineProvider] Parse Error: " + ex.Message);
            }
        }
    }

    // —— 响应结构（按 v1beta 简化） —— //
    [Serializable]
    class GenerateContentRequest
    {
        public Content[] contents;
    }

    [Serializable]
    class Content
    {
        public Part[] parts;
    }

    [Serializable]
    class Part
    {
        public string text;
    }

    [Serializable]
    class GenerateContentResponse
    {
        public Candidate[] candidates;
    }

    [Serializable]
    class Candidate
    {
        public Content content;
        public string finishReason;
        public float safetyRatings; // 忽略细节
    }

    static string ExtractFirstText(GenerateContentResponse resp)
    {
        if (resp?.candidates == null || resp.candidates.Length == 0) return null;
        var c = resp.candidates[0];
        if (c?.content?.parts == null || c.content.parts.Length == 0) return null;
        return c.content.parts[0].text;
    }
}
