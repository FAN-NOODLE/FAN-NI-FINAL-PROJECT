using UnityEngine;

public class PlayerCombat2D : MonoBehaviour
{
    [Header("Hitbox")]
    public Transform attackPoint;    // 放在武器刃尖/拳头上
    public float attackRange = 0.9f;
    public LayerMask enemyMask;      // 只勾 Enemy

    [Header("Damage")]
    public int damage = 1;           // 基础伤害（不变）
    [SerializeField] float damageMultiplier = 1f; // 由情绪/状态修改的倍率

    /// <summary>当前实际伤害 = 基础伤害 * 倍率（向最近整数取整，至少为1）</summary>
    public int CurrentDamage => Mathf.Max(1, Mathf.RoundToInt(damage * Mathf.Max(0f, damageMultiplier)));

    /// <summary>设置伤害倍率（例如 2.0 = 翻倍）</summary>
    public void SetDamageMultiplier(float mult)
    {
        damageMultiplier = Mathf.Max(0f, mult);
    }

    /// <summary>将倍率重置为 1（无加成）</summary>
    public void ResetDamageMultiplier()
    {
        damageMultiplier = 1f;
    }

    /// <summary>累乘一个因子（可用于临时增益）</summary>
    public void MultiplyDamage(float factor)
    {
        damageMultiplier = Mathf.Max(0f, damageMultiplier * factor);
    }

    // 在“攻击动画”的命中帧，添加 Animation Event 调用这个（函数名千万别改）
    public void AnimationAttackHit()
    {
        Vector3 p = attackPoint ? attackPoint.position : transform.position;
        var hits = Physics2D.OverlapCircleAll(p, attackRange, enemyMask);
        int dmg = CurrentDamage; // ← 使用倍率后的伤害

        foreach (var h in hits)
        {
            var eh = h.GetComponentInParent<EnemyHealth>();
            if (eh) eh.TakeDamage(dmg, transform.position);
        }
    }

    void OnDrawGizmosSelected()
    {
        if (attackPoint)
        {
            Gizmos.color = Color.red;
            Gizmos.DrawWireSphere(attackPoint.position, attackRange);
        }
    }
}


