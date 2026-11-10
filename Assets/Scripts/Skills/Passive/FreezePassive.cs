using UnityEngine;
using PenBattleGame.Core;

// 被动[冰冻]：接触到的笔被冰冻 → 表面结霜，摩擦降低（不影响速度）
// value = 摩擦倍率（0.5 = 摩擦减半），value2 = 持续回合数
public class FreezePassive : PassiveSkill
{
    private const float DefaultFrictionMultiplier = 0.5f;
    private const int DefaultTurns = 2;

    protected override void OnHitPen(PenPenCollisionEvent e)
    {
        var opponent = FindOpponent(e);
        if (opponent == null || !opponent.isActive) return;

        float mult = (entry != null && entry.value > 0f) ? entry.value : DefaultFrictionMultiplier;
        int turns = (entry != null && entry.value2 > 0f) ? Mathf.RoundToInt(entry.value2) : DefaultTurns;

        // 施加走 PenStatus 统一入口
        if (PenStatus.ApplyFreeze(opponent, mult, turns) == null) return;

        Debug.Log($"{_pen.penId} 的[冰冻]冻住了 {opponent.penId}：摩擦 ×{mult:0.##}，持续 {turns} 回合");
    }
}
