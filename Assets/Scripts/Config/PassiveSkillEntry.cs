using System;
using UnityEngine;

// 被动效果类型（可扩展：加一个枚举值 + 写一个 PassiveSkill 子类 + 在 PassiveSkillManager 工厂里加一行）
public enum PassiveEffectType
{
    Thorns,     // 荆棘：被撞击时对对方造成伤害（value = 伤害值）
    Momentum,   // 势能：每次发射力度 +value（0.2 = +20%）
    Tough,      // 坚硬：耐久上限 +value
    Slippery,   // 顺滑：摩擦 -value（滑得更远）
    Shockwave,  // 冲击波：自己回合结束时在笔的位置爆发（value = 推力，value2 = 半径）
    Ignite,     // 点燃：接触到的笔被叠加燃烧层数（value = 层数）
    Freeze,     // 冰冻：接触到的笔被结霜，摩擦 ×value（value2 = 持续回合数）
    Slime,      // 粘液：接触到的笔叠加层数并减速（value = 层数，value2 = 减速上限）
    AutoHeal    // 自动回复：每回合结束回复已损失耐久的一定比例（value = 比例，value2 = 单次上限）
}

// 挂在 PenConfig 上的被动条目：不用单独做资产，直接在笔配置里编辑
[Serializable]
public class PassiveSkillEntry
{
    public PassiveEffectType effect = PassiveEffectType.Thorns;

    // 只有"参数修改型"被动（坚硬/顺滑）用得到：决定 value 是比例还是绝对量。
    // 其余被动的 value 是玩法参数（层数/推力/半径/持续回合），operationType 无意义，Inspector 里也不显示。
    public ModifierOp operationType = ModifierOp.Multiply;

    [Tooltip("主数值")]
    public float value = 5f;

    [Tooltip("副数值（不同被动含义不同，见选笔界面的描述文案）")]
    public float value2 = 0f;

    public string DisplayName => PassiveSkillInfo.GetName(effect);
    public string Describe() => PassiveSkillInfo.Describe(effect, value, value2, operationType);

    // 深拷贝：配置资产里的条目只读，塞进玩家/运行时状态前必须复制一份
    public PassiveSkillEntry Clone() => new PassiveSkillEntry
    {
        effect = effect,
        operationType = operationType,
        value = value,
        value2 = value2
    };
}

// 名称与文案（UI 用），新增被动时在这里补一条
public static class PassiveSkillInfo
{
    public static string GetName(PassiveEffectType e)
    {
        switch (e)
        {
            case PassiveEffectType.Thorns:    return "荆棘";
            case PassiveEffectType.Momentum:  return "势能";
            case PassiveEffectType.Tough:     return "坚硬";
            case PassiveEffectType.Slippery:  return "顺滑";
            case PassiveEffectType.Shockwave: return "冲击波";
            case PassiveEffectType.Ignite:    return "点燃";
            case PassiveEffectType.Freeze:    return "冰冻";
            case PassiveEffectType.Slime:     return "粘液";
            case PassiveEffectType.AutoHeal:  return "自动回复";
        }
        return e.ToString();
    }

    // 旧签名保留（默认乘算），不破坏既有调用点
    public static string Describe(PassiveEffectType e, float v, float v2) =>
        Describe(e, v, v2, ModifierOp.Multiply);

    // 只有"参数修改型"被动支持加算/乘算两种方式；其余被动的 value 是玩法参数，operationType 无意义
    public static bool SupportsOp(PassiveEffectType e) =>
        e == PassiveEffectType.Tough || e == PassiveEffectType.Slippery;

    public static string Describe(PassiveEffectType e, float v, float v2, ModifierOp operationType)
    {
        bool add = operationType == ModifierOp.Add;

        switch (e)
        {
            case PassiveEffectType.Tough:
                return add ? $"耐久上限 +{v:0.#}" : $"耐久上限 +{v:P0}";
            case PassiveEffectType.Slippery:
                return add ? $"摩擦 -{v:0.##}，滑得更远" : $"摩擦 -{v:P0}，滑得更远";
        }

        switch (e)
        {
            case PassiveEffectType.Thorns:    return $"被撞击时对对方造成 {v:0.#} 点伤害";
            case PassiveEffectType.Momentum:  return $"每次发射力度 +{v * 100f:0}%";
            case PassiveEffectType.Tough:     return $"耐久上限 +{v:0.#}";
            case PassiveEffectType.Slippery:  return $"摩擦 -{v:P0}（更滑，滑得更远）";
            case PassiveEffectType.Shockwave: return $"自己行动结束时推开 {v2:0.#} 米内的其它笔（推力 {v:0.#}）";
            case PassiveEffectType.Ignite:    return $"接触到的笔叠加 {v:0} 层燃烧（回合结束时每层扣 1 点耐久，之后层数减半）";
            case PassiveEffectType.Freeze:    return $"接触到的笔被冰冻：摩擦降至 {v:P0}（更滑），持续 {v2:0} 回合";
            case PassiveEffectType.Slime:     return $"接触到的笔叠加 {v:0} 层粘液：减速上限 {v2:P0}，层数越高越慢，每回合层数减半";
            case PassiveEffectType.AutoHeal:
                return v2 > 0f
                    ? $"每回合结束回复已损失耐久的 {v:P0}（单次至多 {v2:0.#} 点）"
                    : $"每回合结束回复已损失耐久的 {v:P0}";
        }
        return string.Empty;
    }

    // ===== Inspector 字段名（让 value/value2 在不同被动下显示成有意义的标签）=====

    public static string GetValueLabel(PassiveEffectType e)
    {
        switch (e)
        {
            case PassiveEffectType.Thorns:    return "反弹伤害";
            case PassiveEffectType.Momentum:  return "发射力加成（0.2 = +20%）";
            case PassiveEffectType.Tough:     return "耐久上限加成比例（0.2 = +20%）";
            case PassiveEffectType.Slippery:  return "摩擦降低比例（0.3 = -30%）";
            case PassiveEffectType.Shockwave: return "推力";
            case PassiveEffectType.Ignite:    return "叠加的燃烧层数";
            case PassiveEffectType.Freeze:    return "摩擦倍率（0.5 = 摩擦减半）";
            case PassiveEffectType.Slime:     return "单次叠加的粘液层数";
            case PassiveEffectType.AutoHeal:  return "回复已损失耐久的比例（0.25 = 25%）";
        }
        return "主数值";
    }

    // 数值字段标签随方式变化（仅参数修改型被动会走到 Add 分支）
    public static string GetValueLabel(PassiveEffectType e, ModifierOp operationType)
    {
        if (operationType == ModifierOp.Add)
        {
            switch (e)
            {
                case PassiveEffectType.Tough:    return "耐久上限加算（40 = +40）";
                case PassiveEffectType.Slippery: return "摩擦降低量（0.05 = 摩擦 -0.05）";
            }
        }
        return GetValueLabel(e);
    }

    // 返回 null 表示这个被动不使用 value2（Inspector 里会隐藏该字段）
    public static string GetValue2Label(PassiveEffectType e)
    {
        switch (e)
        {
            case PassiveEffectType.Shockwave: return "半径（米）";
            case PassiveEffectType.Freeze:    return "持续回合数";
            case PassiveEffectType.Slime:     return "减速上限（0.5 = 最慢降到一半）";
            case PassiveEffectType.AutoHeal:  return "单次回复上限（0 = 不限）";
        }
        return null;
    }
}
