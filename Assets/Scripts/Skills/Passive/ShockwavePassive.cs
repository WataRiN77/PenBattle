using UnityEngine;
using PenBattleGame.Core;

// 被动[冲击波]：自己的"行动"结束时（弹笔/放置/用技能之后），在笔的位置爆发一道冲击波推开周围的其它笔
// 注意：是"行动"（单个玩家的一次操作）结束，不是"回合"（所有玩家各行动一次）结束
// value = 推力，value2 = 半径（米）
public class ShockwavePassive : PassiveSkill
{
    protected override void OnActionEnded()
    {
        if (_pen == null) return;

        float force  = entry.value;
        float radius = Mathf.Max(0.1f, entry.value2);

        ShockwaveEffect.Spawn(_pen.transform.position, radius, force, _pen.penId);
        Debug.Log($"{_pen.penId} 的[冲击波]爆发：半径 {radius:0.#}，推力 {force:0.#}");
    }
}
