using UnityEngine;
using UnityEngine.SceneManagement;
using System;
using System.Collections;
using System.Collections.Generic;

public class PlayerHealth : MonoBehaviour
{
    [Header("Base Health")]
    public int maxHP = 5;                      // 基础上限
    public float invulnTime = 0.6f;            // 受击后无敌时间
    public bool reloadSceneOnDeath = true;     // 死亡后重载关卡
    public SpriteRenderer[] renderers;         // 受击闪烁用，可留空自动找

    // —— 对外属性与事件 —— //
    public int HP => hp;
    public int MaxHP => maxHP + TotalMaxHpModifier;
    public event Action<int, int> OnHPChanged; // (cur, max)

    // —— 内部状态 —— //
    int hp;
    bool invuln;
    bool dead;
    Animator anim;
    static readonly int DeadHash = Animator.StringToHash("IsDead");

    // 临时 MaxHP 修正（按来源ID叠加）
    readonly Dictionary<string, int> _maxHpMods = new Dictionary<string, int>();
    int TotalMaxHpModifier
    {
        get
        {
            int sum = 0;
            foreach (var kv in _maxHpMods) sum += kv.Value;
            return sum;
        }
    }

    void Awake()
    {
        anim = GetComponent<Animator>();
        if (renderers == null || renderers.Length == 0)
            renderers = GetComponentsInChildren<SpriteRenderer>(true);

        // 初始化当前血量为“当前总上限”
        hp = Mathf.Max(1, MaxHP);
        RaiseHPEvent();
    }

    // ========= 外部可调用 API =========

    public void TakeDamage(int dmg, Vector3 from)
    {
        if (dead || invuln) return;

        hp -= Mathf.Max(1, dmg);
        hp = Mathf.Clamp(hp, 0, MaxHP);
        RaiseHPEvent();

        StartCoroutine(IFramesFlash());

        if (hp <= 0)
        {
            Die();
        }
        else
        {
            StartCoroutine(InvulnFor(invulnTime));
        }
    }

    public void Heal(int amount)
    {
        if (dead || amount <= 0) return;
        int before = hp;
        hp = Mathf.Min(hp + amount, MaxHP);
        if (hp != before) RaiseHPEvent();
    }

    /// <summary>
    /// 增加临时 MaxHP 修正（可叠加/覆盖同 ID）。可选：立即把当前血量抬到新上限。
    /// </summary>
    public void AddMaxHPModifier(string sourceId, int bonus, bool healToNewMax = false)
    {
        if (string.IsNullOrEmpty(sourceId)) sourceId = "temp";
        _maxHpMods[sourceId] = bonus;

        // 上限变化后，必要时抬高当前血量或截断
        int newMax = MaxHP;
        if (healToNewMax) hp = newMax;
        else hp = Mathf.Clamp(hp, 0, newMax);

        RaiseHPEvent();
    }

    /// <summary>
    /// 移除临时 MaxHP 修正。若当前血量超过新上限，会被截断。
    /// </summary>
    public void RemoveMaxHPModifier(string sourceId)
    {
        if (string.IsNullOrEmpty(sourceId)) sourceId = "temp";
        if (_maxHpMods.Remove(sourceId))
        {
            hp = Mathf.Clamp(hp, 0, MaxHP);
            RaiseHPEvent();
        }
    }

    // ========= 私有工具 =========

    void RaiseHPEvent() => OnHPChanged?.Invoke(HP, MaxHP);

    IEnumerator InvulnFor(float t)
    {
        invuln = true;
        yield return new WaitForSeconds(t);
        invuln = false;
    }

    IEnumerator IFramesFlash()
    {
        float t = 0f, dur = 0.2f;
        while (t < dur)
        {
            t += Time.deltaTime;
            foreach (var r in renderers) if (r) r.enabled = !r.enabled;
            yield return new WaitForSeconds(0.06f);
        }
        foreach (var r in renderers) if (r) r.enabled = true;
    }

    void Die()
    {
        if (dead) return;
        dead = true;

        if (anim) anim.SetBool(DeadHash, true);

        var rb = GetComponent<Rigidbody2D>();
        if (rb) { rb.velocity = Vector2.zero; rb.isKinematic = true; }

        var controller = GetComponent<PlayerController2D>();
        if (controller) controller.enabled = false;

        if (reloadSceneOnDeath)
        {
            StartCoroutine(ReloadSoon());
        }
        else
        {
            Debug.Log("Player Dead");
        }
    }

    IEnumerator ReloadSoon()
    {
        yield return new WaitForSeconds(0.8f);
        Scene scene = SceneManager.GetActiveScene();
        SceneManager.LoadScene(scene.buildIndex);
    }
}


