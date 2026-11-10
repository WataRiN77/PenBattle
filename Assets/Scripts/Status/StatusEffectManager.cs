using System.Collections.Generic;
using UnityEngine;
using PenBattleGame.Core;

public class StatusEffectManager : MonoBehaviour
{
    private Dictionary<string, List<StatusEffect>> _activeEffects = new();

    private void Start()
    {
        EventBus.Instance.Subscribe<RoundPreStartEvent>(OnRoundPreStart);
    }

    // 给某支笔添加效果；返回效果组件，方便调用方设置/叠加数值
    // 注意：已有同类型效果时**复用**而不是重建（燃烧这类"层数叠加"的效果需要保留已有层数）
    public static T AddEffect<T>(PenController pen) where T : StatusEffect
    {
        if (pen == null) return null;

        var existing = pen.GetComponent<T>();
        if (existing != null) return existing;

        return pen.gameObject.AddComponent<T>();
    }

    // 移除某支笔的指定效果
    public static void RemoveEffect<T>(PenController pen) where T : StatusEffect
    {
        if (pen == null) return;
        var effect = pen.GetComponent<T>();
        if (effect != null)
        {
            effect.RemoveEffect();
        }
    }

    // 清掉一支笔身上所有状态效果（DOT 层数不跨小局保留，每小局开始时调用）
    public static void ClearAllEffects(PenController pen)
    {
        if (pen == null) return;

        foreach (var effect in pen.GetComponents<StatusEffect>())
        {
            if (effect != null) effect.RemoveEffect();
        }
    }

    private void OnRoundPreStart(RoundPreStartEvent evt)
    {
        // 每回合重置前，可以做一些清理（可选）
    }
    private void OnDestroy()
    {
        EventBus.Instance.Unsubscribe<RoundPreStartEvent>(OnRoundPreStart);
    }
}
