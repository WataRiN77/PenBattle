using System.Collections.Generic;
using UnityEngine;

// 强化系统：把强化条目应用到笔上
// 两种模式：
//   参数型：改 PenController 上的**修饰乘数**（massMult 等），不直接改基准字段；
//           应用前先 RestoreBaseValues() 把乘数归 1，重复应用不会叠加。
//   行为型：给笔挂一个 PassiveSkill 组件（UpgradeEffectType.Passive）。
//           组件带 fromUpgrade 标记，每小局重跑前先整体摘掉，同样不会叠加。
public static class UpgradeSystem
{
    // 归零乘数 + 摘掉上一轮的行为组件 + 依次应用
    // （每小局开始、以及强化阶段结束后都会调用）
    public static void ApplyAll(PenController pen, List<UpgradeEntry> upgrades)
    {
        if (pen == null) return;

        pen.RestoreBaseValues();

        // 必须先清再建：不清的话每小局都会多挂一层组件，事件订阅随之翻倍
        PassiveSkillManager.DetachUpgradeBehaviors(pen);

        if (upgrades != null)
        {
            foreach (var u in upgrades)
            {
                if (u == null) continue;
                Apply(pen, u);
            }
        }

        pen.RefreshModifiedStats();   // 兜底：所有乘数改完后统一落地一次
    }

    private static void Apply(PenController pen, UpgradeEntry u)
    {
        // 行为型：挂被动组件，参数走 u.passive（value / value2 / operationType 都在里面）
        if (u.IsBehavior)
        {
            PassiveSkillManager.AttachUpgradeBehavior(pen, u.passive);
            return;
        }

        bool add = u.operationType == ModifierOp.Add;

        switch (u.effect)
        {
            // 发射力在 Launch 里是相对倍率，只支持乘算
            case UpgradeEffectType.Attack:
                pen.upgradeForceMultiplier *= (1f + u.value);
                break;

            case UpgradeEffectType.Durability:
                if (add) pen.durabilityAdd += u.value;
                else     pen.durabilityMult = Mathf.Max(0.1f, pen.durabilityMult * (1f + u.value));
                break;

            case UpgradeEffectType.Bounce:
                if (add) pen.bouncinessAdd += u.value;
                else     pen.bouncinessMult = Mathf.Clamp(pen.bouncinessMult * (1f + u.value), 0f, 2f);
                break;

            case UpgradeEffectType.Mass:
                if (add) pen.massAdd += u.value;
                else     pen.massMult = Mathf.Max(0.1f, pen.massMult * (1f + u.value));
                break;

            case UpgradeEffectType.Size:
                if (add) pen.scaleAdd += u.value;
                else     pen.scaleMult = Mathf.Max(0.3f, pen.scaleMult * (1f + u.value));
                break;

            case UpgradeEffectType.Glide:
                // 语义统一是"摩擦变小"：加算写负值，乘算乘 (1-value)
                if (add) pen.frictionAdd -= u.value;
                else     pen.frictionMult = Mathf.Max(0f, pen.frictionMult * (1f - u.value));
                break;
        }
    }
}
