using UnityEngine;
using UnityEngine.UI;
using UnityEngine.SceneManagement;
using TMPro;
using System.Collections;

public class MainMenuController : MonoBehaviour
{
    [Header("Scenes")]
    public string firstLevelScene = "Level01";

    [Header("Buttons")]
    public Button playBtn;
    public Button optionsBtn;
    public Button quitBtn;

    [Header("Options UI")]
    public GameObject optionsPanel;
    public Toggle emotionToggle;
    public Slider volumeSlider;
    public Button optionsBackBtn;

    [Header("Fader")]
    public CanvasGroup fader;
    public float fadeDuration = 0.5f;

    const string PREF_EMOTION = "EmotionEnabled";
    const string PREF_VOLUME = "MasterVolume";

    void Awake()
    {
        // 绑定按钮事件
        playBtn.onClick.AddListener(OnPlay);
        optionsBtn.onClick.AddListener(ShowOptions);
        quitBtn.onClick.AddListener(OnQuit);
        optionsBackBtn.onClick.AddListener(HideOptions);

        // 加载设置
        bool emotionOn = PlayerPrefs.GetInt(PREF_EMOTION, 1) == 1;
        float vol = PlayerPrefs.GetFloat(PREF_VOLUME, 1f);

        emotionToggle.isOn = emotionOn;
        emotionToggle.onValueChanged.AddListener(OnEmotionToggle);

        volumeSlider.value = vol;
        volumeSlider.onValueChanged.AddListener(OnVolumeChanged);
        ApplyVolume(vol);

        // 淡入效果
        if (fader)
        {
            fader.alpha = 1f;
            StartCoroutine(Fade(1f, 0f, fadeDuration));
        }
    }

    // ========== 按钮事件 ==========
    void OnPlay()
    {
        // 销毁主菜单相机
        Camera menuCamera = Camera.main;
        if (menuCamera != null && menuCamera.gameObject.scene.name == "MainMenu")
        {
            Destroy(menuCamera.gameObject);
        }
        
        StartCoroutine(FadeAndLoad(firstLevelScene));
    }

    void ShowOptions()
    {
        if (optionsPanel) optionsPanel.SetActive(true);
    }

    void HideOptions()
    {
        if (optionsPanel) optionsPanel.SetActive(false);
    }

    void OnQuit()
    {
        #if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
        #else
        Application.Quit();
        #endif
    }

    // ========== 设置事件 ==========
    void OnEmotionToggle(bool on)
    {
        PlayerPrefs.SetInt(PREF_EMOTION, on ? 1 : 0);
        PlayerPrefs.Save();
    }

    void OnVolumeChanged(float v)
    {
        PlayerPrefs.SetFloat(PREF_VOLUME, v);
        PlayerPrefs.Save();
        ApplyVolume(v);
    }

    void ApplyVolume(float v)
    {
        AudioListener.volume = v;
    }

    // ========== 协程 ==========
    IEnumerator Fade(float from, float to, float dur)
    {
        if (!fader) yield break;
        float t = 0f;
        fader.blocksRaycasts = true;
        while (t < dur)
        {
            t += Time.unscaledDeltaTime;
            fader.alpha = Mathf.Lerp(from, to, t / dur);
            yield return null;
        }
        fader.alpha = to;
        fader.blocksRaycasts = to > 0.001f;
    }

    IEnumerator FadeAndLoad(string scene)
    {
        yield return Fade(fader.alpha, 1f, fadeDuration);
        SceneManager.LoadScene(scene);
    }
}
