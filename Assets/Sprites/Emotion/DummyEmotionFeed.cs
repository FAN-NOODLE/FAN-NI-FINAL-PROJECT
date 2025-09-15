using UnityEngine;
using System;

[DefaultExecutionOrder(-100)]
public class DummyEmotionFeed : MonoBehaviour
{
    public struct EmotionEvent { public string label; public float confidence; }
    public event Action<EmotionEvent> OnEmotionChanged;

    [Header("Emotion Settings")]
    [Range(0f,1f)] public float confidence = 0.9f;
    public bool emitOnStart = true;
    public string startLabel = "Calm";
    
    [Header("Auto Update Settings")]
    public bool enableAutoUpdate = true;
    public float updateInterval = 10f; // 可在Inspector中调整
    
    private EmotionPuller emotionPuller;
    private float timer = 0f;

    void Start()
    {
        // 获取EmotionPuller组件
        emotionPuller = GetComponent<EmotionPuller>();
        if (emotionPuller == null)
        {
            // 如果没有找到，尝试在场景中查找
            emotionPuller = FindObjectOfType<EmotionPuller>();
        }
        
        if (emitOnStart) Emit(startLabel);
        
        // 输出自动更新设置
        if (enableAutoUpdate)
        {
            Debug.Log($"[DummyEmotionFeed] Auto update enabled, interval: {updateInterval}s");
        }
    }

    void Update()
    {
        // 每间隔时间自动更新情绪状态
        if (enableAutoUpdate)
        {
            timer += Time.deltaTime;
            if (timer >= updateInterval)
            {
                timer = 0f;
                UpdateEmotionFromPuller();
            }
        }
        
        // 保留原有的键盘输入功能
        if (Input.GetKeyDown(KeyCode.Alpha1) || Input.GetKeyDown(KeyCode.Keypad1)) Emit("Excited");
        if (Input.GetKeyDown(KeyCode.Alpha2) || Input.GetKeyDown(KeyCode.Keypad2)) Emit("Happy");
        if (Input.GetKeyDown(KeyCode.Alpha3) || Input.GetKeyDown(KeyCode.Keypad3)) Emit("Anxious");
        if (Input.GetKeyDown(KeyCode.Alpha4) || Input.GetKeyDown(KeyCode.Keypad4)) Emit("Sad");
        if (Input.GetKeyDown(KeyCode.Alpha5) || Input.GetKeyDown(KeyCode.Keypad5)) Emit("Calm");
    }
    
    // 从EmotionPuller获取最新情绪数据并触发事件
    private void UpdateEmotionFromPuller()
    {
        if (emotionPuller != null && !string.IsNullOrEmpty(emotionPuller.lastLabel))
        {
            Emit(emotionPuller.lastLabel, emotionPuller.lastConfidence);
        }
        else
        {
            Debug.LogWarning("EmotionPuller not found or no emotion data available");
        }
    }

    public void Emit(string label, float? conf = null)
    {
        var e = new EmotionEvent{ label = label, confidence = conf ?? confidence };
        Debug.Log($"[DummyEmotionFeed] {e.label} ({e.confidence:0.00})");
        OnEmotionChanged?.Invoke(e);
    }
    
    // 提供外部调用的方法，用于手动触发更新
    public void ForceUpdate()
    {
        UpdateEmotionFromPuller();
    }
    
    // 重置计时器
    public void ResetTimer()
    {
        timer = 0f;
    }
}