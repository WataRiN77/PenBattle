using UnityEngine;
using PenBattleGame.Core;

// 顺滑：摩擦降低（常驻，滑得更远）
//   operationType = Multiply → 比例（0.3 = 摩擦 ×0.7，降 30%）
//   operationType = Add      → 绝对量（0.05 = 摩擦 -0.05）
public class SlipperyPassive : PassiveSkill
{
    protected override void ApplyStaticEffect()
    {
        if (_pen == null || entry == null) return;

        if (entry.operationType == ModifierOp.Add)
            _pen.frictionAdd -= entry.value;
        else
            _pen.frictionMult = Mathf.Max(0f, _pen.frictionMult * (1f - entry.value));

        _pen.RefreshModifiedStats();
        Debug.Log($"{_pen.penId} 的[顺滑]：有效摩擦 {_pen.EffectiveFriction:0.##}");
    }
}
