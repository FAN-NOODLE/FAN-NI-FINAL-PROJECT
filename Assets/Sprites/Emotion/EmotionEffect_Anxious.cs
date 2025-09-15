using UnityEngine;

public class EmotionEffect_Anxious : MonoBehaviour
{
    [Header("UI Settings")]
    public HUDPlayerUI hudUI;          // HUD UI 引用
    public Sprite anxiousIcon;         // Anxious 状态的图标
    public float iconDuration = 3f;    // Icon 显示时长

    [Header("Lever Settings")]
    public Lever2D decryptionLever;    // 可选：手动绑定的 Lever
    private bool lastWasAnxious = false;

    public void OnEmotionChanged(EmotionType emotionType)
    {
        bool isAnxious = (emotionType == EmotionType.Anxious);

        if (isAnxious && !lastWasAnxious)
        {
            // ① HUD 上显示一个 Anxious 图标
            if (hudUI && anxiousIcon)
                hudUI.ShowStatus("anxious", anxiousIcon, iconDuration);

            // ② 随机触发一个 Lever 打开
            TriggerRandomLever();
        }

        lastWasAnxious = isAnxious;
    }

    private void TriggerRandomLever()
    {
        Lever2D[] levers = FindObjectsOfType<Lever2D>();
        if (levers.Length == 0) return;

        Lever2D chosen = levers[Random.Range(0, levers.Length)];
        chosen.SetState(true);

        Debug.Log($"[EmotionEffect_Anxious] Random lever {chosen.leverId} set to ON.");
    }
}

