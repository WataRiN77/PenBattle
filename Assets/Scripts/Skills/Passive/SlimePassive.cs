using UnityEngine;
using PenBattleGame.Core;

// 被动[粘液]：接触到的笔被叠加"粘液"层数 → 减速
// value  = 单次碰撞叠加的层数（默认 3）
// value2 = 减速上限（默认 0.5 = 最多慢一半）
// 结算规则见 SlimeEffect：回合结束时层数减半（向下取整），减速随层数递减且封顶
public class SlimePassive : PassiveSkill
{
    private const int DefaultStacks = 3;
    private const float DefaultMaxSlow = 0.5f;

    protected override void OnHitPen(PenPenCollisionEvent e)
    {
        var opponent = FindOpponent(e);
        if (opponent == null || !opponent.isActive) return;

        int stacks = (entry != null && entry.value > 0f)
            ? Mathf.RoundToInt(entry.value)
            : DefaultStacks;

        float maxSlow = (entry != null && entry.value2 > 0f)
            ? entry.value2
            : DefaultMaxSlow;

        // 施加走 PenStatus 统一入口
        var slime = PenStatus.ApplySlime(opponent, stacks, maxSlow);
        if (slime == null) return;

        Debug.Log($"{_pen.penId} 的[粘液]给 {opponent.penId} 叠加 {stacks} 层" +
                  $"（当前 {slime.stacks} 层，速度 ×{slime.CurrentMultiplier:0.##}）");
    }
}
