// Assets/Scripts/Portal/Portal2D.cs
using UnityEngine;
using UnityEngine.SceneManagement;
using System.Collections;

[RequireComponent(typeof(Collider2D))]
public class Portal2D : MonoBehaviour
{
    [Header("Navigation")]
    public string nextSceneName = "Level02";

    [Header("Visual")]
    public Sprite unlockedSprite;      // 可选：解锁时替换静态图；如果用Animator，可留空
    public float fadeInDuration = 0.5f; // 渐显时长（秒）

    [Header("State")]
    public bool locked = true;          // 初始是否上锁
    public float activationDelay = 0.25f; // 渐显开始后，延迟多久才允许进入

    Collider2D col;
    bool activated;

    // 缓存所有渲染器及其原始 alpha，用于按比例淡入（支持子物体）
    SpriteRenderer[] renderers;
    float[] baseAlphas;

    void Awake()
    {
        col = GetComponent<Collider2D>();
        col.isTrigger = true;
        col.enabled = false; // 解锁并延迟后才启用

        // 缓存并清零透明度（包括子物体）
        renderers = GetComponentsInChildren<SpriteRenderer>(true);
        baseAlphas = new float[renderers.Length];
        for (int i = 0; i < renderers.Length; i++)
        {
            var c = renderers[i].color;
            baseAlphas[i] = c.a <= 0f ? 1f : c.a; // 若原alpha为0，按1对待
            renderers[i].color = new Color(c.r, c.g, c.b, 0f);
        }

        ApplyVisual();
    }

    void ApplyVisual()
    {
        // 若需要，用静态图覆盖（仅主节点的SpriteRenderer）
        if (!locked && unlockedSprite)
        {
            var sr = GetComponent<SpriteRenderer>();
            if (sr) sr.sprite = unlockedSprite;
        }
    }

    public void Unlock()
    {
        locked = false;
        ApplyVisual();

        activated = false;
        CancelInvoke(nameof(EnableCollider));
        Invoke(nameof(EnableCollider), activationDelay); // 避免玩家正站在点上立即触发

        if (fadeInDuration <= 0f) SetAlpha(1f);
        else StartCoroutine(FadeInCo());
    }

    IEnumerator FadeInCo()
    {
        float t = 0f;
        while (t < fadeInDuration)
        {
            t += Time.deltaTime;
            float a = Mathf.Clamp01(t / fadeInDuration);
            SetAlpha(a);
            yield return null;
        }
        SetAlpha(1f);
    }

    // 将整棵对象的所有SpriteRenderer透明度设为 alpha（按原始alpha比例）
    public void SetAlpha(float alpha)
    {
        if (renderers == null) return;
        for (int i = 0; i < renderers.Length; i++)
        {
            var c = renderers[i].color;
            renderers[i].color = new Color(c.r, c.g, c.b, alpha * baseAlphas[i]);
        }
    }

    void EnableCollider()
    {
        activated = true;
        if (col) col.enabled = true;
    }

    void OnTriggerEnter2D(Collider2D other)
    {
        if (!activated || locked) return;

        // 更鲁棒：不依赖Tag，向上找玩家控制器
        if (other.GetComponentInParent<PlayerController2D>() != null)
        {
            SceneManager.LoadScene(nextSceneName);
        }
    }
}

