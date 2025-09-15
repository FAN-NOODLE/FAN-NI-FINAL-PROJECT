using UnityEngine;

public class AnimEventRelay : MonoBehaviour
{
    public EnemySkeleton2D target;  // 拖拽父物体（挂了 EnemySkeleton2D 的那个）

    void Awake()
    {
        if (!target) target = GetComponentInParent<EnemySkeleton2D>();
    }

    // 这些方法名必须与动画事件里选择的函数名一模一样
    public void Anim_AttackBegin() { target?.Anim_AttackBegin(); }
    public void Anim_AttackHit()   { target?.Anim_AttackHit(); }
    public void Anim_AttackEnd()   { target?.Anim_AttackEnd(); }
}
