// Assets/Scripts/Emotion/PredictResponse.cs
// 只解析我们需要的字段（label / confidence）。JsonUtility 不支持字典，probs就先略过。
[System.Serializable]
public class PredictResponse
{
    public double ts;
    public string label;
    public float confidence;
    public float quality;
}
