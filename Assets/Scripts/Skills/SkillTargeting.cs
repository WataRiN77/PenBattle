using UnityEngine;

// 主动技能的目标类型（可扩展：以后加新类型，只需在这里加一个枚举 + 在 SkillSystem 里加分支）
public enum SkillTargetMode
{
    Self,          // 无需选择：作用于自己
    EnemySingle,   // 需要选择：点选一只敌方笔
    Direction,     // 需要选择：拖拽指定释放方向
    AllEnemies,    // 无需选择：全体敌方
    AllPens        // 无需选择：全场所有笔（含自己）
}

// 释放技能时携带的目标信息
public struct SkillTargetContext
{
    public PlayerData targetPlayer;   // EnemySingle 用
    public Vector3 direction;         // Direction 用
    public bool hasTarget;            // 是否选中了目标玩家
    public bool hasDirection;         // 是否给出了方向

    public static SkillTargetContext None => new SkillTargetContext();

    public static SkillTargetContext ForPlayer(PlayerData p)
        => new SkillTargetContext { targetPlayer = p, hasTarget = true };

    public static SkillTargetContext ForDirection(Vector3 dir)
        => new SkillTargetContext { direction = dir.normalized, hasDirection = true };
}
