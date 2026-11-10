// 效果统一入口 —— 任何来源（主动技能 / 被动 / 强化 / 地图机关）给笔挂状态都走这里
//
// 为什么要有这一层：
//   "效果"独立于"技能"层。同一个 BurnEffect 既可能被主动技能施加（SkillType.Burn），
//   也可能被被动施加（IgnitePassive 碰触叠层）。如果每个来源各写一遍施加逻辑，
//   以后每加一个技能就要复制一次有效性检查和参数兜底。
//
// 本类只负责"挂上去"，结算规则仍在各自的 StatusEffect 子类里：
//   BurnEffect  —— 回合结束每层扣 1 耐久，然后层数减半
//   FreezeEffect —— 摩擦倍率 + 持续回合
//
// 新增效果时在这里加一个 ApplyXxx 方法，然后让所有来源都调它，不要在别处直接
// 调 StatusEffectManager.AddEffect<T>()。

using UnityEngine;

public static class PenStatus
{
    // 燃烧：叠加层数。返回状态实例，方便调用方读当前层数打日志
    // stacks <= 0 直接忽略（避免误配把层数写成 0 或负数）
    public static BurnEffect ApplyBurn(PenController target, int stacks)
    {
        if (target == null || !target.isActive || stacks <= 0) return null;

        var burn = StatusEffectManager.AddEffect<BurnEffect>(target);
        if (burn == null) return null;

        burn.AddStacks(stacks);
        return burn;
    }

    // 冰冻：frictionMultiplier = 摩擦倍率（0.5 = 摩擦减半），turns = 持续回合数
    public static FreezeEffect ApplyFreeze(PenController target, float frictionMultiplier, int turns)
    {
        if (target == null || !target.isActive) return null;

        var freeze = StatusEffectManager.AddEffect<FreezeEffect>(target);
        if (freeze == null) return null;

        freeze.Freeze(target, frictionMultiplier, turns);
        return freeze;
    }

    // 粘液：叠加层数 + 减速上限（maxSlow = 最多减速多少，0.5 = 最多慢一半）
    // 层数每回合减半，减速按饱和曲线递增但有上限，细节见 SlimeEffect
    public static SlimeEffect ApplySlime(PenController target, int stacks, float maxSlow = 0.5f)
    {
        if (target == null || !target.isActive || stacks <= 0) return null;

        var slime = StatusEffectManager.AddEffect<SlimeEffect>(target);
        if (slime == null) return null;

        // 上限由施加方决定（不同被动/技能可以配不同的黏度），但夹在合理区间
        if (maxSlow > 0f) slime.maxSlow = Mathf.Clamp(maxSlow, 0.05f, 0.9f);

        slime.AddStacks(stacks);
        return slime;
    }

    // 回复耐久：统一入口（自动回复 / 涅槃 / 回复类强化和技能都走这里）
    // 返回实际回复量；内部会广播 PenHealedEvent，下游（强化/UI/特效）靠它感知回复量
    public static float Restore(PenController target, float amount, string source = "Unknown")
    {
        if (target == null) return 0f;
        return target.RestoreStatusDurability(amount, source);
    }
}
