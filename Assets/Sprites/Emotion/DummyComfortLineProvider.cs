// IComfortLineProvider.cs
using UnityEngine;

public interface IComfortLineProvider
{
    string GetLine();
}

// 一个临时的、本地的台词提供者，用于测试和占位
public class DummyComfortLineProvider : MonoBehaviour, IComfortLineProvider
{
    [Tooltip("本地安慰台词候选池")]
    [TextArea(2, 5)]
    public string[] localLineCandidates = new string[]
    {
       "Don't worry, I'm here with you.",
    "Take a deep breath, everything will be fine.",
    "You've done your best, take a rest.",
    "Let's take this level slowly together, no rush."
    };

    public string GetLine()
    {
        // 随机从本地候选池中选择一句
        if (localLineCandidates == null || localLineCandidates.Length == 0)
            return "...";
        return localLineCandidates[Random.Range(0, localLineCandidates.Length)];
    }
}