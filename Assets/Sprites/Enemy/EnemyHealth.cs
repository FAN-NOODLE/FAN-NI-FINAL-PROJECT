// Assets/Scripts/Enemy/EnemyHealth.cs
using UnityEngine;
using System;   // ← 为了 Action 事件

public class EnemyHealth : MonoBehaviour
{
    public int maxHP = 3;
    int hp;
    EnemySkeleton2D ai;

    /// <summary>敌人死亡时触发（在调用 ai.Die() 之前触发）。</summary>
    public event Action OnDied;

    void Awake()
    {
        hp = maxHP;
        ai = GetComponent<EnemySkeleton2D>();
    }

    /// <summary>受到伤害。from：伤害源位置（用于击退方向）。</summary>
    public void TakeDamage(int dmg, Vector2? from = null)
    {
        if (hp <= 0) return;

        hp -= Mathf.Max(1, dmg);

        // 受击反应（击退/闪烁/硬直）
        if (ai) ai.HurtKnockback(from ?? (Vector2)transform.position);

        if (hp <= 0)
        {
            // 先广播事件，让 Spawner 移除计数
            OnDied?.Invoke();

            // 正常死亡流程
            if (ai) ai.Die();
            else Destroy(gameObject, 1.5f);
        }
    }
}



