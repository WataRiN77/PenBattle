using UnityEngine;
using PenBattleGame.Core;

// 冰冻：只降低笔自身的摩擦系数（表面结霜 → 变滑），**不影响速度**
//   - 摩擦 × frictionMultiplier（0.5 = 减半），解除时还原
//   - 视觉：笔身泛蓝色冰霜（FreezeVisual）
// 注意：数值由施加方调用 Freeze(...) 传入，不走 OnApply
// （AddComponent 时 OnApply 比"设置数值"更早，时序上拿不到参数）
public class FreezeEffect : StatusEffect
{
    [Header("冰冻")]
    [Tooltip("摩擦倍率（0.5 = 摩擦减半，表面像结了霜一样滑）")]
    public float frictionMultiplier = 0.5f;

    protected override void Awake()
    {
        base.Awake();
        effectId = "Freeze";
        duration = 2;
    }

    // 由施加方调用：设置数值并生效；重复施加会刷新持续回合
    // 摩擦走 PenController.statusFrictionMult（状态层独立乘数），不直接改基准字段 —— 解除时归 1 即还原
    public void Freeze(PenController target, float frictionMult, int turns)
    {
        if (target == null) return;

        frictionMultiplier = Mathf.Clamp01(frictionMult);
        duration = Mathf.Max(1, turns);
        _tickCount = 0;                 // 重新计时

        // 结霜 → 摩擦降低（只动摩擦，速度不受影响）
        // 镜像实例只表演：客户端是运动学刚体，改摩擦没有任何意义
        if (!visualOnly)
        {
            target.statusFrictionMult = frictionMultiplier;
            target.RefreshModifiedStats();
        }

        // 视觉：蓝色霜冻（挂在子物体上，解除时销毁）
        if (transform.Find("FreezeVisual") == null)
        {
            var go = new GameObject("FreezeVisual");
            go.transform.SetParent(transform, false);
            go.AddComponent<FreezeVisual>();
        }

        Debug.Log($"冰冻生效：{target.penId} 摩擦 ×{frictionMultiplier:0.##}" +
                  $"（有效摩擦 {target.EffectiveFriction:0.##}），持续 {duration} 回合");
    }

    /// <summary>剩余回合数（联机同步 / HUD 用）；0 表示已到期</summary>
    public int RemainingTurns => duration > 0 ? Mathf.Max(0, duration - _tickCount) : 0;

    /// <summary>
    /// 联机镜像：按房主下发的剩余回合与摩擦倍率刷新表现。
    /// 不复用 Freeze() 是因为那个会写物理参数（客户端不需要，也不该重算）。
    /// </summary>
    public void ApplyMirror(int turnsLeft, float frictionMult)
    {
        visualOnly = true;
        frictionMultiplier = Mathf.Clamp01(frictionMult);
        duration = Mathf.Max(1, turnsLeft);
        _tickCount = 0;

        if (transform.Find("FreezeVisual") == null)
        {
            var go = new GameObject("FreezeVisual");
            go.transform.SetParent(transform, false);
            go.AddComponent<FreezeVisual>();
        }
    }

    protected override void OnExpire()
    {
        Restore();
        Debug.Log($"{_pen?.penId} 的冰冻解除了");

        // 顺手清掉霜冻视觉（视觉自己也会在检测到 FreezeEffect 消失时销毁）
        var vis = transform.Find("FreezeVisual");
        if (vis != null) Destroy(vis.gameObject);

        base.OnExpire();
    }

    private void Restore()
    {
        if (_pen != null)
        {
            _pen.statusFrictionMult = 1f;
            _pen.RefreshModifiedStats();
        }
    }
}
