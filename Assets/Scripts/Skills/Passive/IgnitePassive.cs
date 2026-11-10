using UnityEngine;
using PenBattleGame.Core;

// 被动[点燃]：接触到的笔会被叠加"燃烧"层数
// value = 叠加的燃烧层数（默认 3）
// 燃烧结算规则见 BurnEffect：回合结束时每层扣 1 点耐久，之后层数减半向下取整
public class IgnitePassive : PassiveSkill
{
    private const int DefaultStacks = 3;

    protected override void OnHitPen(PenPenCollisionEvent e)
    {
        var opponent = FindOpponent(e);
        if (opponent == null || !opponent.isActive) return;

        int stacks = (entry != null && entry.value > 0f)
            ? Mathf.RoundToInt(entry.value)
            : DefaultStacks;

        // 施加走 PenStatus 统一入口，与主动技能 SkillType.Burn 共用同一套规则
        var burn = PenStatus.ApplyBurn(opponent, stacks);
        if (burn == null) return;

        Debug.Log($"{_pen.penId} 的[点燃]给 {opponent.penId} 叠加 {stacks} 层燃烧（当前 {burn.stacks} 层）");
    }
}
