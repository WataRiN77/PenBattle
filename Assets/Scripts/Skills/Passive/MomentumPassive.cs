using UnityEngine;
using PenBattleGame.Core;

// 势能：每次发射力度 +value（靠 InputLaunchEvent 触发，所以发射事件必须先于 Launch 广播）
public class MomentumPassive : PassiveSkill
{
    protected override void OnLaunch()
    {
        _pen.pendingForceBonus += entry.value;
        Debug.Log($"{_pen.penId} 的[势能]触发：本次发射力 +{entry.value * 100f:0}%");
    }
}
