using System;
using UnityEngine;

// 强化效果类型（可扩展：加一个枚举值 + 在 UpgradeSystem 的 switch 里加一个分支）
// ⚠️ 新值只能往后加，已有值的顺序不能动（.asset 里存的是 int）
public enum UpgradeEffectType
{
    Attack,      // 攻击强化：发射力 ×(1+value)（0.2 = +20%）
    Durability,  // 耐久强化：耐久上限 +value
    Bounce,      // 弹性强化：弹性 +value
    Mass,        // 质量强化：质量 ×(1+value)，更难被撞飞
    Size,        // 体型强化：体积 ×(1+value)，更容易被打中
    Glide,       // 滑行强化：摩擦 -value，滑得更远
    Passive      // 行为型强化：给笔挂一个被动组件（内容见 UpgradeEntry.passive）
}

// 强化条目：直接在 GameManager 的"强化池"里编辑，不需要单独做资产
[Serializable]
public class UpgradeEntry
{
    public UpgradeEffectType effect = UpgradeEffectType.Attack;

    public ModifierOp operationType = ModifierOp.Multiply;

    [Tooltip("主数值：Multiply 时是比例（0.15 = +15%），Add 时是绝对量（2 = +2）")]
    public float value = 0.2f;

    [Tooltip("仅 effect = Passive 时使用：这条强化要挂上的被动及其参数")]
    public PassiveSkillEntry passive = new PassiveSkillEntry();

    // 行为型强化（挂组件）而非参数型强化（改数值）
    public bool IsBehavior => effect == UpgradeEffectType.Passive;

    public string DisplayName => IsBehavior && passive != null
        ? PassiveSkillInfo.GetName(passive.effect)
        : UpgradeInfo.GetName(effect);

    public string Describe() => IsBehavior && passive != null
        ? passive.Describe()
        : UpgradeInfo.Describe(effect, value, operationType);

    // 深拷贝：选项来自强化池资产，塞进玩家列表前必须复制一份（否则改一个玩家会污染配置）
    public UpgradeEntry Clone() => new UpgradeEntry
    {
        effect = effect,
        operationType = operationType,
        value = value,
        passive = passive != null ? passive.Clone() : new PassiveSkillEntry()
    };
}

// 名称与文案（UI 与 Inspector 用），新增强化时在这里补一条
public static class UpgradeInfo
{
    public static string GetName(UpgradeEffectType e)
    {
        switch (e)
        {
            case UpgradeEffectType.Attack:     return "攻击强化";
            case UpgradeEffectType.Durability: return "耐久强化";
            case UpgradeEffectType.Bounce:     return "弹性强化";
            case UpgradeEffectType.Mass:       return "质量强化";
            case UpgradeEffectType.Size:       return "体型强化";
            case UpgradeEffectType.Glide:      return "滑行强化";
            case UpgradeEffectType.Passive:    return "行为强化";
        }
        return e.ToString();
    }

    // 旧签名保留（默认乘算），不破坏既有调用点
    public static string Describe(UpgradeEffectType e, float v) => Describe(e, v, ModifierOp.Multiply);

    public static string Describe(UpgradeEffectType e, float v, ModifierOp operationType)
    {
        bool add = operationType == ModifierOp.Add;

        switch (e)
        {
            // Attack：发射力在 Launch 里是相对倍率，只支持乘算
            case UpgradeEffectType.Attack:     return $"发射力 +{v:P0}";

            // Durability：本来就是绝对加量
            case UpgradeEffectType.Durability: return $"耐久上限 +{v:0.#}";

            case UpgradeEffectType.Bounce:
                return add ? $"弹性 +{v:0.##}" : $"弹性 +{v:P0}";

            case UpgradeEffectType.Mass:
                return add ? $"质量 +{v:0.#}（更难被撞飞）" : $"质量 +{v:P0}（更难被撞飞）";

            case UpgradeEffectType.Size:
                return add ? $"体型 +{v:0.##}（更容易被打中）" : $"体型 +{v:P0}（更容易被打中）";

            case UpgradeEffectType.Glide:
                return add ? $"摩擦 -{v:0.##}（滑得更远）" : $"摩擦 -{v:P0}（滑得更远）";

            case UpgradeEffectType.Passive:
                return "（行为型强化：参数见下方被动配置）";
        }
        return string.Empty;
    }

    public static string GetValueLabel(UpgradeEffectType e)
    {
        switch (e)
        {
            case UpgradeEffectType.Attack:     return "发射力加成（0.2 = +20%）";
            case UpgradeEffectType.Durability: return "耐久上限加成比例（0.2 = +20%）";
            case UpgradeEffectType.Bounce:     return "弹性加成比例（0.1 = +10%）";
            case UpgradeEffectType.Mass:       return "质量加成比例（0.5 = +50%）";
            case UpgradeEffectType.Size:       return "体型加成比例（0.3 = +30%）";
            case UpgradeEffectType.Glide:      return "摩擦降低比例（0.1 = -10%）";
            case UpgradeEffectType.Passive:    return "（行为型强化不使用此数值，请在下方选被动）";
        }
        return "数值";
    }

    // 数值字段标签随方式变化：加算时显示绝对量语义，不支持加算的效果给出明确提示
    public static string GetValueLabel(UpgradeEffectType e, ModifierOp operationType)
    {
        if (operationType == ModifierOp.Add)
        {
            switch (e)
            {
                case UpgradeEffectType.Mass:       return "质量加算（2 = +2 点）";
                case UpgradeEffectType.Bounce:     return "弹性加算（0.1 = +0.1）";
                case UpgradeEffectType.Glide:      return "摩擦加算（0.05 = 摩擦 -0.05）";
                case UpgradeEffectType.Durability: return "耐久上限加算（40 = +40 点）";
                case UpgradeEffectType.Size:       return "体型加算（0.2 = 尺寸 +0.2）";
                case UpgradeEffectType.Attack:     return "⚠ 发射力只支持乘算，此值按乘算生效";
            }
        }
        return GetValueLabel(e);
    }
}
