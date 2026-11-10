using UnityEngine;
using PenBattleGame.Core;

// 坚硬：耐久上限 +value（常驻）
//   operationType = Add      → 绝对点数（40 = 上限 +40）
//   operationType = Multiply → 比例（0.2 = 上限 +20%）
public class ToughPassive : PassiveSkill
{
    protected override void ApplyStaticEffect()
    {
        if (_pen == null || entry == null) return;

        if (entry.operationType == ModifierOp.Add)
            _pen.durabilityAdd += entry.value;
        else
            _pen.durabilityMult = Mathf.Max(0.1f, _pen.durabilityMult * (1f + entry.value));

        _pen.RefreshModifiedStats();
        Debug.Log($"{_pen.penId} 的[坚硬]：耐久上限 {_pen.maxStatusDurability:0.#}");
    }
}
