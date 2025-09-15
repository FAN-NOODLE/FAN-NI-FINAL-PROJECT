// Assets/Scripts/Emotion/EmotionPuller.cs
using System.Collections;
using UnityEngine;
using UnityEngine.Networking;

public class EmotionPuller : MonoBehaviour
{
    [Header("Server")]
    public string serverBase = "http://192.168.3.100:8000";
    public string latestPath = "/latest";

    [Header("Polling")]
    [Tooltip("每隔多久向后端请求一次 /latest")]
    public float pullIntervalSec = 0.5f;

    [Header("Last Result (read-only)")]
    public string lastLabel = "";
    [Range(0f,1f)]
    public float lastConfidence = 0f;

    Coroutine _loop;

    void OnEnable()
    {
        if (_loop != null) StopCoroutine(_loop);
        _loop = StartCoroutine(PullLoop());
    }

    void OnDisable()
    {
        if (_loop != null) StopCoroutine(_loop);
        _loop = null;
    }

    IEnumerator PullLoop()
    {
        var wait = new WaitForSeconds(pullIntervalSec);
        while (true)
        {
            string url = serverBase.TrimEnd('/') + latestPath;
            using (var uwr = UnityWebRequest.Get(url))
            {
                uwr.downloadHandler = new DownloadHandlerBuffer();
                yield return uwr.SendWebRequest();

                if (uwr.result == UnityWebRequest.Result.Success)
                {
                    // —— 方式A：用 JsonUtility（无需安装包）——
                    try
                    {
                        var resp = JsonUtility.FromJson<PredictResponse>(uwr.downloadHandler.text);
                        if (resp != null && !string.IsNullOrEmpty(resp.label))
                        {
                            lastLabel = resp.label;
                            lastConfidence = resp.confidence;
                        }
                    }
                    catch { /* 忽略一次性解析错误 */ }

                    // // —— 方式B（可选）：若你已安装 Newtonsoft.Json —— 
                    // try
                    // {
                    //     var resp = Newtonsoft.Json.JsonConvert.DeserializeObject<PredictResponse>(uwr.downloadHandler.text);
                    //     if (resp != null && !string.IsNullOrEmpty(resp.label))
                    //     {
                    //         lastLabel = resp.label;
                    //         lastConfidence = resp.confidence;
                    //     }
                    // }
                    // catch { }
                }
                else
                {
                    // 只在偶发失败时安静忽略；若需调试可打印：
                    // Debug.LogWarning($"[EmotionPuller] {uwr.result}: {uwr.error}");
                }
            }
            yield return wait;
        }
    }
}
