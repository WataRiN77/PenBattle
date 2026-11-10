using UnityEngine;
using PenBattleGame.Core;

// 被动[自动回复]：每回合结束回复"已损失耐久"的一定比例
// value  = 回复比例（0.25 = 回复已损失的 25%）
// value2 = 单次回复上限（0 = 不限），防止残血时一次性回太多
//
// 特性：满血时不回（已损失为 0）；被打得越狠回得越多，天然平衡。
// 结算时机与燃烧/粘液等 DOT 同在 TurnEnded，由 EventBus 按订阅顺序调用，不做跨效果顺序保证。
// 若将来需要"必定在扣血之后结算"，把 OnTurnEnded 改成 OnTurnStarted 即可（语义等价：上回合结束的回血，下回合开始到账）。
public class AutoHealPassive : PassiveSkill
{
    private const float DefaultRatio = 0.25f;

    protected override void OnTurnEnded()
    {
        if (_pen == null || !_pen.isActive) return;

        float ratio = (entry != null && entry.value > 0f)
            ? entry.value
            : DefaultRatio;

        float missing = _pen.maxStatusDurability - _pen.StatusDurability;
        if (missing <= 0f) return;   // 满血不回

        float amount = missing * Mathf.Clamp01(ratio);

        float cap = (entry != null && entry.value2 > 0f) ? entry.value2 : float.MaxValue;
        amount = Mathf.Min(amount, cap);

        float healed = PenStatus.Restore(_pen, amount, "AutoHeal");
        if (healed <= 0f) return;

        Debug.Log($"{_pen.penId} 的[自动回复]回了 {healed:F1} 点耐久" +
                  $"（已损失 {missing:F1} 的 {ratio:P0}，当前 {_pen.StatusDurability:F1}/{_pen.maxStatusDurability:F1}）");
    }
}
