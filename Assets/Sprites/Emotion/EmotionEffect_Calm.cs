using UnityEngine;
using System.Collections;
using System.Collections.Generic;

[RequireComponent(typeof(AudioListener))]
public class EmotionEffect_Calm : MonoBehaviour
{
    [Header("Default Music")]
    public AudioClip defaultBGM;

    [Header("Calm Music Pool")]
    public List<AudioClip> calmMusicPool = new List<AudioClip>();
    public bool shuffleOnStart = true;

    [Header("Audio Routing")]
    public UnityEngine.Audio.AudioMixerGroup outputGroup;

    [Header("Fade Settings")]
    public float fadeDuration = 1.5f;
    [Range(0f, 1f)] public float targetVolume = 0.65f;

    [Header("Calm Status")]
    public Sprite calmStatusIcon;
    public string calmStatusId = "calm_status";

    [Header("References")]
    public HUDPlayerUI hud;
    public MonoBehaviour emotionManager;

    // ---------------- Rain FX ----------------
    [Header("Rain FX (optional)")]
    [Tooltip("场景里现成的雨效果根对象（包含一个或多个ParticleSystem）。进入Calm时启用，离开时禁用。")]
    public GameObject rainRoot;                         // 推荐：把雨的粒子都放到一个空物体下
    [Tooltip("如果你更想精细控制，可直接指定若干粒子系统；留空则自动在rainRoot下收集。")]
    public ParticleSystem[] rainParticles;              // 可为空，运行时会从rainRoot里收集
    [Tooltip("是否把雨效果跟随玩家（把rainRoot设为玩家的子物体）。")]
    public bool rainFollowPlayer = true;
    [Tooltip("跟随玩家时，雨效果相对玩家的偏移。")]
    public Vector3 rainLocalOffset = new Vector3(0, 2f, 0f);
    [Tooltip("雨开始/结束的粒子渐变时长。")]
    public float rainFadeIn = 0.4f;
    public float rainFadeOut = 0.4f;
    [Tooltip("当粒子发射率是常量时，用于淡入/淡出到这个目标值（每秒）。如果你的粒子用的是曲线/随机，此项仅作近似。")]
    public float rainTargetRateOverTime = 80f;

    // ---- internal ----
    AudioSource _a, _b;
    AudioSource _current, _standby;
    bool _isCalm = false;
    List<AudioClip> _shuffledCalmMusic = new List<AudioClip>();
    int _currentCalmMusicIndex = 0;

    DummyEmotionFeed _typedFeed;
    Coroutine _rainCo;

    void Awake()
    {
        // 初始化双AudioSource
        _a = gameObject.AddComponent<AudioSource>();
        _b = gameObject.AddComponent<AudioSource>();
        foreach (var s in new[] { _a, _b })
        {
            s.loop = true;
            s.playOnAwake = false;
            s.spatialBlend = 0f;
            s.volume = 0f;
            if (outputGroup) s.outputAudioMixerGroup = outputGroup;
        }
        _current = _a; _standby = _b;

        PrepareCalmMusicPool();

        if (!hud) hud = FindObjectOfType<HUDPlayerUI>(true);

        // Rain FX初始化
        InitRainRefs();
    }

    void Start()
    {
        // 播放默认BGM
        if (defaultBGM)
        {
            _current.clip = defaultBGM;
            _current.volume = targetVolume;
            _current.Play();
        }

        // 订阅情绪事件
        if (!emotionManager)
        {
            var feed = FindObjectOfType<DummyEmotionFeed>(true);
            if (feed) emotionManager = feed;
        }
        _typedFeed = emotionManager as DummyEmotionFeed;
        if (_typedFeed != null)
        {
            _typedFeed.OnEmotionChanged += OnEmotionChanged_Typed;
        }
    }

    void OnDestroy()
    {
        if (_typedFeed != null) _typedFeed.OnEmotionChanged -= OnEmotionChanged_Typed;
    }

    // ========= Emotion入口 =========
    void OnEmotionChanged_Typed(DummyEmotionFeed.EmotionEvent e)
    {
        var label = e.label?.ToLowerInvariant();
        EmotionType type = label switch
        {
            "excited" => EmotionType.Excited,
            "happy"   => EmotionType.Happy,
            "anxious" => EmotionType.Anxious,
            "sad"     => EmotionType.Sad,
            "calm"    => EmotionType.Calm,
            _         => EmotionType.Calm
        };
        OnEmotionChanged(type);
    }

    // 公开方法：外部系统可以调用
    public void OnEmotionChanged(EmotionType newEmotion)
    {
        if (newEmotion == EmotionType.Calm)
        {
            EnterCalm();
        }
        else if (_isCalm)
        {
            ExitCalm();
        }
    }

    // ========= Calm逻辑 =========
    void EnterCalm()
    {
        if (_isCalm)
        {
            // 已在Calm，再次进入可换下一首
            PlayRandomCalmMusic();
            // 确保雨效处于ON状态
            SetRain(true);
            return;
        }

        _isCalm = true;

        if (hud && calmStatusIcon)
            hud.ShowStatus(calmStatusId, calmStatusIcon, 0f);

        PlayRandomCalmMusic();
        SetRain(true);
    }

    void ExitCalm()
    {
        _isCalm = false;

        if (hud) hud.ClearStatus(calmStatusId);

        if (defaultBGM) CrossfadeTo(defaultBGM);
        else
        {
            StopAllCoroutines();
            StartCoroutine(FadeOutCurrentMusic());
        }

        SetRain(false);
    }

    // ========= Rain FX控制 =========
    void InitRainRefs()
    {
        // 如果没显式指定粒子数组，从 rainRoot 下抓取
        if (rainParticles == null || rainParticles.Length == 0)
        {
            if (rainRoot)
                rainParticles = rainRoot.GetComponentsInChildren<ParticleSystem>(true);
        }

        // 初始放置/父子关系
        if (rainRoot && rainFollowPlayer)
        {
            var player = FindPlayerTransform();
            if (player)
            {
                rainRoot.transform.SetParent(player, worldPositionStays: false);
                rainRoot.transform.localPosition = rainLocalOffset;
            }
        }

        // 初始禁用（默认不下雨），防止开场就播放
        ToggleRainRoot(false, instant: true);
        SetParticlesRate(0f, instant:true);
    }

    Transform FindPlayerTransform()
    {
        var pc = FindObjectOfType<PlayerController2D>(true);
        if (pc) return pc.transform;
        var tagGo = GameObject.FindGameObjectWithTag("Player");
        return tagGo ? tagGo.transform : null;
    }

    void SetRain(bool on)
    {
        if (_rainCo != null) StopCoroutine(_rainCo);
        _rainCo = StartCoroutine(RainRoutine(on));
    }

    IEnumerator RainRoutine(bool turnOn)
    {
        if (rainRoot == null && (rainParticles == null || rainParticles.Length == 0))
            yield break;

        if (turnOn)
        {
            ToggleRainRoot(true, instant:false);
            yield return FadeParticlesRate(toRate: rainTargetRateOverTime, dur: Mathf.Max(0.01f, rainFadeIn));
            // Play确保在激活后开始发射
            PlayParticles();
        }
        else
        {
            yield return FadeParticlesRate(toRate: 0f, dur: Mathf.Max(0.01f, rainFadeOut));
            StopParticles();                // 停止并清理
            ToggleRainRoot(false, instant:false);
        }
    }

    void ToggleRainRoot(bool active, bool instant)
    {
        if (rainRoot)
        {
            rainRoot.SetActive(active);
        }
        else if (rainParticles != null)
        {
            // 没有根对象时，逐个启停Renderer（保证可见性）
            foreach (var ps in rainParticles)
            {
                var rend = ps.GetComponent<Renderer>();
                if (rend) rend.enabled = active;
            }
        }
        // instant参数暂留（如果需要做更复杂的开关动画可用）
    }

    void PlayParticles()
    {
        if (rainParticles == null) return;
        foreach (var ps in rainParticles)
        {
            if (!ps) continue;
            if (!ps.isPlaying) ps.Play(true);
        }
    }

    void StopParticles()
    {
        if (rainParticles == null) return;
        foreach (var ps in rainParticles)
        {
            if (!ps) continue;
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        }
    }

    IEnumerator FadeParticlesRate(float toRate, float dur)
    {
        if (rainParticles == null || rainParticles.Length == 0) yield break;

        // 记录每个粒子系统当前的常量rate（如果是曲线/随机，取当前常量近似）
        float[] startRates = new float[rainParticles.Length];
        for (int i = 0; i < rainParticles.Length; i++)
        {
            var ps = rainParticles[i];
            if (!ps) { startRates[i] = 0f; continue; }

            var em = ps.emission;
            var rate = em.rateOverTime;
            startRates[i] = rate.constant;   // 近似：仅支持常量模式
        }

        float t = 0f;
        while (t < dur)
        {
            t += Time.unscaledDeltaTime;
            float k = Mathf.Clamp01(t / dur);
            for (int i = 0; i < rainParticles.Length; i++)
            {
                var ps = rainParticles[i];
                if (!ps) continue;
                var em = ps.emission;
                float v = Mathf.Lerp(startRates[i], toRate, k);
                em.rateOverTime = v; // 仅在常量模式下安全；若你用曲线，请手工改用Preset A/B
            }
            yield return null;
        }

        SetParticlesRate(toRate, instant:true);
    }

    void SetParticlesRate(float rate, bool instant)
    {
        if (rainParticles == null) return;
        foreach (var ps in rainParticles)
        {
            if (!ps) continue;
            var em = ps.emission;
            em.rateOverTime = rate; // 仅常量模式
        }
    }

    // ========= 音乐逻辑 =========
    void PrepareCalmMusicPool()
    {
        _shuffledCalmMusic = new List<AudioClip>(calmMusicPool);
        if (shuffleOnStart && _shuffledCalmMusic.Count > 1)
        {
            for (int i = _shuffledCalmMusic.Count - 1; i > 0; i--)
            {
                int r = Random.Range(0, i + 1);
                ( _shuffledCalmMusic[i], _shuffledCalmMusic[r] ) = ( _shuffledCalmMusic[r], _shuffledCalmMusic[i] );
            }
        }
        Debug.Log($"Calm music pool prepared with {_shuffledCalmMusic.Count} tracks");
    }

    void PlayRandomCalmMusic()
    {
        if (_shuffledCalmMusic.Count == 0)
        {
            Debug.LogWarning("Calm music pool is empty!");
            return;
        }
        AudioClip next = GetNextCalmMusic();
        if (next) CrossfadeTo(next);
    }

    AudioClip GetNextCalmMusic()
    {
        if (_shuffledCalmMusic.Count == 0) return null;
        AudioClip next = _shuffledCalmMusic[_currentCalmMusicIndex];
        _currentCalmMusicIndex = (_currentCalmMusicIndex + 1) % _shuffledCalmMusic.Count;
        return next;
    }

    public void PlayNextCalmTrack()
    {
        if (_isCalm) PlayRandomCalmMusic();
    }

    public void ShuffleCalmMusicPool()
    {
        PrepareCalmMusicPool();
        _currentCalmMusicIndex = 0;
        Debug.Log("Calm music pool shuffled");
    }

    void CrossfadeTo(AudioClip clip)
    {
        if (!clip) return;
        if (_current.clip == clip && _current.isPlaying) return;
        StopAllCoroutines();
        StartCoroutine(FadeRoutine(clip));
    }

    IEnumerator FadeRoutine(AudioClip next)
    {
        _standby.clip = next;
        _standby.time = 0f;
        _standby.volume = 0f;
        _standby.Play();

        float t = 0f;
        float startVol = _current.volume;
        while (t < fadeDuration)
        {
            t += Time.unscaledDeltaTime;
            float k = Mathf.Clamp01(t / fadeDuration);
            _current.volume = Mathf.Lerp(startVol, 0f, k);
            _standby.volume = Mathf.Lerp(0f, targetVolume, k);
            yield return null;
        }

        _current.Stop();
        _current.volume = 0f;

        var tmp = _current; _current = _standby; _standby = tmp;
    }

    IEnumerator FadeOutCurrentMusic()
    {
        float startVol = _current.volume;
        float t = 0f;
        while (t < fadeDuration)
        {
            t += Time.unscaledDeltaTime;
            _current.volume = Mathf.Lerp(startVol, 0f, t / fadeDuration);
            yield return null;
        }
        _current.Stop();
        _current.volume = 0f;
    }

    // ---- 调试 ----
    [ContextMenu("Test Random Calm Music")]
    public void TestRandomCalmMusic()
    {
        if (calmMusicPool.Count > 0) PlayRandomCalmMusic();
        else Debug.LogWarning("No calm music in pool!");
    }

    [ContextMenu("Shuffle Music Pool")]
    public void DebugShufflePool()
    {
        ShuffleCalmMusicPool();
    }
}
  