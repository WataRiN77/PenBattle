using UnityEngine;
using PenBattleGame.Core;

// 荆棘：被撞击时对对方造成伤害
public class ThornsPassive : PassiveSkill
{
    protected override void OnHitPen(PenPenCollisionEvent e)
    {
        var opponent = FindOpponent(e);
        if (opponent == null || !opponent.isActive) return;

        opponent.ReduceStatusDurability(entry.value, $"{_pen.penId}_Thorns");
        Debug.Log($"{_pen.penId} 的[荆棘]反弹 {entry.value} 点伤害给 {opponent.penId}");
    }
}
