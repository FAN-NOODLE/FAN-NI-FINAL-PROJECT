using UnityEngine;

public enum EmotionType { Excited, Happy, Anxious, Sad, Calm }

[CreateAssetMenu(menuName = "Config/Emotion Visual Config", fileName = "EmotionVisualConfig")]
public class EmotionVisualConfig : ScriptableObject
{
    [System.Serializable]
    public struct Entry
    {
        public EmotionType type;
        public Sprite icon;
        public Color color;     // 用作边框/主色调
    }

    public Entry[] entries;

    public Sprite GetIcon(EmotionType t)
    {
        foreach (var e in entries) if (e.type == t) return e.icon;
        return null;
    }
    public Color GetColor(EmotionType t)
    {
        foreach (var e in entries) if (e.type == t) return e.color;
        return Color.white;
    }
}

