using UnityEngine;

public class GameBootstrap : MonoBehaviour
{
    const string PREF_EMOTION = "EmotionEnabled";
    const string PREF_VOLUME = "MasterVolume";

    public GameObject emotionSystemRoot;

    void Start()
    {
        // 应用音量设置
        float volume = PlayerPrefs.GetFloat(PREF_VOLUME, 1f);
        AudioListener.volume = volume;

        // 应用情绪检测设置
        if (emotionSystemRoot != null)
        {
            bool emotionEnabled = PlayerPrefs.GetInt(PREF_EMOTION, 1) == 1;
            emotionSystemRoot.SetActive(emotionEnabled);
        }
    }
}