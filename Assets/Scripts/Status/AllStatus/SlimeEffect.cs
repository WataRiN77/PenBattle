using UnityEngine;
using PenBattleGame.Core;

// 粘液：按"层数"结算的减速状态（无伤害）
//
// 规则（与燃烧保持一致的节奏）：
//   - 每回合结束时层数减半（向下取整），减到 0 自动消失
//
// 减速（递减 + 封顶）：
//   reduction = maxSlow * n / (n + softness)
//   speedMultiplier = 1 - reduction
//   n 越大越接近 maxSlow，但永远不超过它 → 边际收益递减，天然封顶 50%
//
// 实现方式：只改 PenController.slowMultiplier（Launch 里会乘进去），不碰 drag，
// 避免和"顺滑"被动、强化系统的 RestoreBaseValues 抢同一个参数。
// 注意：slowMultiplier 目前是独占的减速通道，将来若有多来源减速需要改成乘算栈。
public class SlimeEffect : StatusEffect
{
    [Header("粘液")]
    [Tooltip("当前层数：回合结束时减半（向下取整），减到 0 自动消失")]
    public int stacks = 0;

    [Tooltip("减速上限（0.5 = 最多减速 50%，速度最低降到一半）")]
    [Range(0.05f, 0.9f)] public float maxSlow = 0.5f;

    [Tooltip("曲线柔和度：越大越平缓（1.5 = 3层约减33%；1 = 3层约减37.5%）")]
    [Range(0.1f, 8f)] public float softness = 1.5f;

    private bool _applied;

    protected override void Awake()
    {
        base.Awake();
        effectId = "Slime";
        duration = 0;   // 由层数驱动，不走基类的"持续回合数"
    }

    // 当前层数对应的速度倍率（1 = 不减速，0.5 = 减半）
    public float CurrentMultiplier
    {
        get
        {
            if (stacks <= 0) return 1f;
            float ratio = stacks / (stacks + Mathf.Max(0.01f, softness));
            return 1f - maxSlow * ratio;
        }
    }

    // 叠加层数并立刻刷新减速（施加方显式调用，不依赖 OnApply 时序）
    public void AddStacks(int amount)
    {
        if (amount <= 0) return;

        stacks += amount;
        _applied = true;
        RefreshSlow();

        Debug.Log($"{_pen?.penId} 粘液 +{amount} 层，当前 {stacks} 层，速度 ×{CurrentMultiplier:0.##}");
    }

    // 联机镜像：直接设定层数（客户端只做表现，减速由房主算）
    public void SetStacks(int value)
    {
        stacks = Mathf.Max(0, value);
    }

    protected override void OnApply()
    {
        RefreshSlow();
        Debug.Log($"粘液施加在 {_pen?.penId} 上，当前 {stacks} 层，速度 ×{CurrentMultiplier:0.##}");

        // TODO 视觉：绿色黏液叠加层（照 FreezeVisual 的"复制网格 + 叠加层"做法，shader 必须纯 ASCII）
    }

    protected override void OnTick()
    {
        if (_pen == null) return;

        if (stacks <= 0)
        {
            OnExpire();
            return;
        }

        // 回合结束：层数减半（向下取整），与燃烧一致
        stacks /= 2;

        if (stacks <= 0)
        {
            OnExpire();
            return;
        }

        RefreshSlow();
        Debug.Log($"{_pen.penId} 粘液结算：剩余 {stacks} 层，速度 ×{CurrentMultiplier:0.##}");
    }

    protected override void OnExpire()
    {
        Debug.Log($"{_pen?.penId} 的粘液干了");
        RestoreSlow();
        base.OnExpire();
    }

    private void RefreshSlow()
    {
        if (_pen == null || visualOnly) return;
        _pen.slowMultiplier = CurrentMultiplier;
    }

    private void RestoreSlow()
    {
        if (_pen != null && _applied) _pen.slowMultiplier = 1f;
        _applied = false;
    }
}
