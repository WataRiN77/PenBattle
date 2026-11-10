using UnityEngine;

// 按配置把被动组件挂到笔上
public static class PassiveSkillManager
{
    public static void AttachTo(PenController pen, PenConfig cfg)
    {
        if (pen == null || cfg == null || cfg.passiveSkills == null) return;

        foreach (var e in cfg.passiveSkills)
        {
            if (e == null) continue;

            PassiveSkill comp = CreatePassive(pen, e.effect);
            if (comp == null) continue;

            // AddComponent 早于 Bind，所以 entry 只能在这里给
            comp.Bind(pen, e);
            Debug.Log($"{pen.penId} 获得被动：{e.DisplayName}（{e.Describe()}）");
        }
    }

    // 行为型强化的入口：把强化条目里的被动挂到笔上。
    // 和 AttachTo 共用同一个工厂，新增被动时只需要改 CreatePassive 一处。
    //
    // 允许重复挂同一种被动，这正是"强化已有被动"的实现方式：
    //   火焰笔自带「点燃 3 层」+ 选一张行为强化「点燃 2 层」→ 两个组件，每次撞击合计叠 5 层。
    //   若将来要改成"取最高值不叠加"，在这里加一步去重检查即可。
    public static PassiveSkill AttachUpgradeBehavior(PenController pen, PassiveSkillEntry e)
    {
        if (pen == null || e == null) return null;

        PassiveSkill comp = CreatePassive(pen, e.effect);
        if (comp == null) return null;

        comp.fromUpgrade = true;   // 先打标记，再 Bind（Bind 会立刻跑 ApplyStaticEffect）
        comp.Bind(pen, e);
        Debug.Log($"{pen.penId} 获得强化行为：{e.DisplayName}（{e.Describe()}）");
        return comp;
    }

    // 摘掉所有"强化挂上来的"被动，笔自带的一个都不动。
    // ⚠️ Destroy 是帧末生效的：必须先把 enabled 关掉，让 OnDisable 立刻退订事件，
    //    否则同一帧里再跑一次 ApplyAll，旧组件还会再响应一遍事件（层数/DOT 会双倍）。
    public static void DetachUpgradeBehaviors(PenController pen)
    {
        if (pen == null) return;

        var comps = pen.GetComponents<PassiveSkill>();
        for (int i = 0; i < comps.Length; i++)
        {
            var comp = comps[i];
            if (comp == null || !comp.fromUpgrade) continue;

            comp.fromUpgrade = false;                 // 防止同一帧被清两次
            comp.enabled = false;                     // 立刻退订
            UnityEngine.Object.Destroy(comp);         // 帧末销毁
        }
    }

    // 唯一的"按枚举造组件"的地方：笔自带被动与行为型强化共用
    public static PassiveSkill CreatePassive(PenController pen, PassiveEffectType effect)
    {
        if (pen == null) return null;

        switch (effect)
        {
            case PassiveEffectType.Thorns:    return pen.gameObject.AddComponent<ThornsPassive>();
            case PassiveEffectType.Momentum:  return pen.gameObject.AddComponent<MomentumPassive>();
            case PassiveEffectType.Tough:     return pen.gameObject.AddComponent<ToughPassive>();
            case PassiveEffectType.Slippery:  return pen.gameObject.AddComponent<SlipperyPassive>();
            case PassiveEffectType.Shockwave: return pen.gameObject.AddComponent<ShockwavePassive>();
            case PassiveEffectType.Ignite:    return pen.gameObject.AddComponent<IgnitePassive>();
            case PassiveEffectType.Freeze:    return pen.gameObject.AddComponent<FreezePassive>();
            case PassiveEffectType.Slime:     return pen.gameObject.AddComponent<SlimePassive>();
            case PassiveEffectType.AutoHeal:  return pen.gameObject.AddComponent<AutoHealPassive>();
            default:
                Debug.LogWarning($"[被动] 未实现的类型：{effect}");
                return null;
        }
    }
}
