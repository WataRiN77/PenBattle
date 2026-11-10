using System.Collections.Generic;
using UnityEngine;

[System.Serializable]
public class PlayerData
{
    [Header("基础信息")]
    public string playerId;          // "Player1", "Player2", ...
    public string playerName;        // 显示名称
    public int playerIndex;          // 0, 1, 2, 3

    [Header("笔的引用")]
    public PenController pen;        // 游戏中的笔实体

    [Header("所选笔配置")]
    public PenConfig penConfig;

    [Header("所选技能")]
    public List<SkillConfig> skills = new List<SkillConfig>();
    public Dictionary<string, int> skillUses = new Dictionary<string, int>();

    [Header("强化（开局 / 败者强化，整场累计）")]
    public List<UpgradeEntry> upgrades = new List<UpgradeEntry>();

    [Header("积分与状态")]
    public int score = 0;            // 总积分（先到3分获胜）
    public bool isEliminated = false;// 当前小局是否被淘汰
    public int eliminationRank = 0;  // 淘汰排名（1=第一名被淘汰，越大越晚淘汰）

    [Header("操作限制（每回合）")]
    public bool hasPlacedThisRound = false;    // 是否已使用“放置”操作
    public bool hasUsedSkillThisRound = false; // 是否已使用技能（每回合限一次）

    // 兼容旧字段：返回所选技能 id 列表
    public List<string> cardIds => skills.ConvertAll(s => s.skillId);

    // 重置（每小局开始时调用）
    public void ResetForRound()
    {
        isEliminated = false;
        eliminationRank = 0;
        hasPlacedThisRound = false;
        hasUsedSkillThisRound = false;

        skillUses.Clear();
        foreach (var s in skills)
            skillUses[s.skillId] = s.usesPerRound;

        if (pen != null)
        {
            // 清掉上一小局残留的状态效果（燃烧等 DOT 的层数不跨小局保留）
            StatusEffectManager.ClearAllEffects(pen);

            pen.ResetPen(GetSpawnPosition());

            // 重新应用强化（基于基准值计算，每小局重置后都不会丢、也不会叠加）
            UpgradeSystem.ApplyAll(pen, upgrades);
        }
    }

    // 获取出生位置：委托给 GameManager（手动指定优先，否则自动均匀分布并避障）
    public Vector3 GetSpawnPosition()
    {
        var gm = GameManager.Instance;
        if (gm != null) return gm.GetSpawnPositionFor(playerIndex);

        // 兜底（GameManager 还没初始化时）
        float angle = (playerIndex / 2f) * 360f;
        float rad = angle * Mathf.Deg2Rad;

        return new Vector3(Mathf.Sin(rad) * 3f, 0.6f, Mathf.Cos(rad) * 3f);
    }
}
