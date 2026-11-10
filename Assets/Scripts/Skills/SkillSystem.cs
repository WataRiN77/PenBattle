using UnityEngine;

public static class SkillSystem
{
    // 统一入口：ctx 承载目标（单体目标 / 方向 / 空）
    public static void Execute(SkillConfig skill, PlayerData caster, SkillTargetContext ctx)
    {
        if (skill == null || caster == null || caster.pen == null) return;

        switch (skill.type)
        {
            case SkillType.Boost:
                caster.pen.pendingForceBonus += skill.value;
                Debug.Log($"{caster.playerName} 下次发射力 +{skill.value * 100f:F0}%");
                break;

            case SkillType.Bounce:
                caster.pen.ApplyBouncinessModifier(skill.value);
                Debug.Log($"{caster.playerName} 弹性临时覆盖为 {skill.value}");
                break;

            case SkillType.Speed:
                caster.pen.ApplySpeedMultiplier(skill.value);
                Debug.Log($"{caster.playerName} 速度倍率 {skill.value}");
                break;

            case SkillType.Burn:
                ApplyBurn(skill, caster, ctx);
                break;
        }
    }

    // 燃烧按 targetMode 决定打谁
    private static void ApplyBurn(SkillConfig skill, PlayerData caster, SkillTargetContext ctx)
    {
        switch (skill.targetMode)
        {
            case SkillTargetMode.EnemySingle:
                if (ctx.hasTarget && ctx.targetPlayer != null && ctx.targetPlayer.pen != null)
                {
                    ApplyBurnTo(ctx.targetPlayer.pen, skill);
                    Debug.Log($"{caster.playerName} 对 {ctx.targetPlayer.playerName} 施加燃烧");
                }
                break;

            case SkillTargetMode.AllEnemies:
                foreach (var p in GameManager.Instance.players)
                    if (p != caster && !p.isEliminated && p.pen != null)
                        ApplyBurnTo(p.pen, skill);
                Debug.Log($"{caster.playerName} 对全体敌人施加燃烧");
                break;

            case SkillTargetMode.AllPens:   // 含自己
                foreach (var p in GameManager.Instance.players)
                    if (!p.isEliminated && p.pen != null)
                        ApplyBurnTo(p.pen, skill);
                Debug.Log($"{caster.playerName} 对全场施加燃烧");
                break;

            default:   // Self / Direction 对燃烧无意义，退化为打最近敌人
                var nearest = FindNearestEnemy(caster);
                if (nearest != null && nearest.pen != null)
                {
                    ApplyBurnTo(nearest.pen, skill);
                    Debug.Log($"{caster.playerName} 对最近的 {nearest.playerName} 施加燃烧");
                }
                else
                {
                    Debug.Log("Burn: 没有可施加的敌方笔");
                }
                break;
        }
    }

    // 给目标叠加燃烧层数（skill.value = 层数，未配置时默认 3 层）
    // 施加细节走 PenStatus 统一入口，与被动[点燃]共用同一套规则
    private static void ApplyBurnTo(PenController target, SkillConfig skill)
    {
        int stacks = skill.value >= 1f ? Mathf.RoundToInt(skill.value) : 3;
        PenStatus.ApplyBurn(target, stacks);
    }

    private static PlayerData FindNearestEnemy(PlayerData caster)
    {
        var gm = GameManager.Instance;
        if (gm == null || caster == null || caster.pen == null) return null;

        PlayerData nearest = null;
        float minSqr = float.MaxValue;
        Vector3 origin = caster.pen.transform.position;

        foreach (var p in gm.players)
        {
            if (p == caster || p.isEliminated || p.pen == null) continue;
            float d = (p.pen.transform.position - origin).sqrMagnitude;
            if (d < minSqr)
            {
                minSqr = d;
                nearest = p;
            }
        }
        return nearest;
    }
}
