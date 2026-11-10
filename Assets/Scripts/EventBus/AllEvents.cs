using System.Collections.Generic;
using UnityEngine;

namespace PenBattleGame.Core
{
    // ===== 事件（与策划文档一致，发布事件时统一调用）=====
    // 小局 (Round)  = 从开局直到只剩一名玩家，本小局结束
    // 回合 (Turn)   = 小局内每位存活玩家各行动一次
    // 行动 (Action) = 单名玩家的一次操作（弹笔 / 放置 / 使用技能）

    #region INPUT

    public class InputBeginDragEvent : GameEvent
    {
        public string penId { get; set; }
        public Vector3 startPos { get; set; }
    }

    public class InputUpdateDragEvent : GameEvent
    {
        public string penId { get; set; }
        public Vector3 currentPos { get; set; }
        public Vector3 delta { get; set; }
    }

    public class InputCancelDragEvent : GameEvent
    {
        public string penId { get; set; }
    }

    public class InputLaunchEvent : GameEvent
    {
        public string penId { get; set; }
        public Vector3 launchDirection { get; set; }
        public float launchForce { get; set; }
    }

    public class PenSelectedEvent : GameEvent
    {
        public string penId { get; set; }
    }

    public class PowerStartEvent : GameEvent
    {
        public string penId { get; set; }
    }

    public class PowerUpdateEvent : GameEvent
    {
        public string penId { get; set; }
        public float currentPower { get; set; }
        public float maxPower { get; set; }
        public float normalizedPower { get; set; }
    }

    #endregion

    #region PHYSICS

    public class PenPenCollisionEvent : GameEvent
    {
        public string penAId { get; set; }
        public string penBId { get; set; }
        // 本次碰撞的发起方。用来判断"是不是当前回合玩家的笔"，判定伤害时按碰撞时的速度归属
        public string initiatorPenId { get; set; }
        public float relativeSpeed { get; set; }
        public Vector3 collisionNormal { get; set; }
        public Vector3 collisionPoint { get; set; }

        // 工具：判断某支笔是不是发起方 / 取对方 id
        public bool IsInitiator(string penId) => penId == initiatorPenId;
        public string GetOpponentId(string penId) => penId == penAId ? penBId : penAId;
    }

    public class PenObstacleCollisionEvent : GameEvent
    {
        public string penId { get; set; }
        public string obstacleId { get; set; }
        public float impactForce { get; set; }
        public Vector3 collisionNormal { get; set; }
        public Vector3 collisionPoint { get; set; }
    }

    public class ObstacleDamagedEvent : GameEvent
    {
        public string obstacleId { get; set; }
        public int oldDurability { get; set; }
        public int newDurability { get; set; }
        public string attackerPenId { get; set; }
    }

    public class ObstacleDestroyedEvent : GameEvent
    {
        public string obstacleId { get; set; }
        public Vector3 position { get; set; }
        public string destroyerPenId { get; set; }
    }

    #endregion

    #region DEAD OR ALIVE

    public class PenOutOfBoundsEvent : GameEvent
    {
        public string penId { get; set; }
        public string killerId { get; set; }
        public Vector3 outOfBoundsPosition { get; set; }
    }

    public class PenEliminatedEvent : GameEvent
    {
        public string penId { get; set; }
        public string killerId { get; set; }
        public string eliminationReason { get; set; }
    }

    #endregion

    #region ROUNDS

    public class RoundPreStartEvent : GameEvent
    {
        public int roundNumber { get; set; }
        public List<string> playerIds { get; set; }   // 2~4 人
    }

    public class RoundStartEvent : GameEvent
    {
        public int roundNumber { get; set; }
        public List<string> playerIds { get; set; }   // StatusEffect 的 Tick 起点
    }

    public class RoundEndEvent : GameEvent
    {
        public int roundNumber { get; set; }
        public string winnerId { get; set; }
        public string endReason { get; set; }
    }

    #endregion

    #region TURN（回合：小局内每位存活玩家各行动一次）

    public class TurnStartedEvent : GameEvent
    {
        public int roundNumber { get; set; }
        public int turnNumber { get; set; }    // 该小局内的第几回合
    }

    public class TurnEndedEvent : GameEvent
    {
        public int roundNumber { get; set; }
        public int turnNumber { get; set; }
    }

    #endregion

    #region ACTION（行动：单名玩家的一次操作，弹笔 / 放置 / 使用技能）

    public class ActionStartedEvent : GameEvent
    {
        public string playerId { get; set; }
        public float timeLimit { get; set; }
    }

    public class ActionEndedEvent : GameEvent
    {
        public string playerId { get; set; }
    }

    #endregion

    #region UPGRADE

    // 强化阶段开始（开局强化 / 每小局败者强化），用于弹出 UI。
    public class UpgradePhaseStartEvent : GameEvent
    {
        public int roundNumber { get; set; }
        public List<string> eligiblePlayerIds { get; set; } // 哪些玩家可以选强化
        public int upgradeCount { get; set; }               // 每位可选几个
        public bool beforeMatch { get; set; }               // true = 开局强化
    }

    // 某位玩家本次抽到的选项（每次选择都会重新抽）
    public class UpgradeOptionsRolledEvent : GameEvent
    {
        public string playerId { get; set; }
        public List<UpgradeEntry> options { get; set; }
    }

    public class UpgradeChosenEvent : GameEvent
    {
        public string playerId { get; set; }
        public string upgradeName { get; set; }
    }

    public class UpgradePhaseEndEvent : GameEvent
    {
        public int roundNumber { get; set; }
        public bool beforeMatch { get; set; }
    }

    #endregion

    #region SETUP

    public class MatchSetupStartEvent : GameEvent
    {
        public int totalPlayers { get; set; }
    }

    public class PlayerSetupConfirmedEvent : GameEvent
    {
        public string playerId { get; set; }
        public string penConfigId { get; set; }
        public List<string> skillIds { get; set; }
    }

    public class MatchSetupCompleteEvent : GameEvent
    {
        public List<string> playerIds { get; set; }
    }

    #endregion

    #region CARDS

    #endregion

    #region SKILLS

    public class SkillUsedEvent : GameEvent
    {
        public string playerId { get; set; }
        public string skillId { get; set; }
    }

    #endregion

    #region DAMAGE

    // 技能或状态造成的伤害（燃烧、荆棘等）
    public class PenDamagedEvent : GameEvent
    {
        public string penId { get; set; }
        public float amount { get; set; }
        public float remainingDurability { get; set; }
        public string source { get; set; }
    }

    // 耐久回复（自动回复 / 涅槃 / 回复类强化 / 回复类技能）。
    // amount = **实际**回复量（已受上限钳制）；满血时不广播（healed <= 0 直接返回）
    // source = 来源标识（"AutoHeal" / "Nirvana" / 强化 id ...），监听方据此区分是谁回的血
    // ⚠️ 监听方如果在这个事件里再次调用回复，会递归触发自己 —— 用 source 过滤，别无条件回血
    public class PenHealedEvent : GameEvent
    {
        public string penId { get; set; }
        public float amount { get; set; }
        public float remainingDurability { get; set; }
        public float maxDurability { get; set; }
        public string source { get; set; }
    }

    #endregion

    #region STATUS

    public class BurnTickEvent : GameEvent
    {
        public string penId { get; set; }
        public float remainingDurability { get; set; }
        public int stacks { get; set; }   // 本次结算后剩余燃烧层数
    }

    #endregion

    #region 表现层反馈（供 Feel 桥接使用）

    /// <summary>
    /// 纯表现层的碰撞反馈事件 —— **不含任何玩法语义**，只用来播特效。
    ///
    /// 为什么不直接复用 PenPenCollisionEvent：
    ///   ① 客户端两支笔都是 kinematic、不跑物理 → 收不到 OnCollisionEnter；
    ///   ② 即使有回调，PenPenCollisionEvent 也只由 penId 较小的一方发 → 客户端永远收不到。
    ///   所以由**房主广播**碰撞数据，各端本地发这个事件给表现层。
    ///
    /// 刻意与 PenPenCollisionEvent 分开：被动技能（荆棘/点燃/冰冻）只订阅后者，
    /// 否则客户端会把被动技能也跑一遍（重复结算）。
    /// </summary>
    public class CollisionFeedbackEvent : GameEvent
    {
        public Vector3 point { get; set; }
        public float relativeSpeed { get; set; }
        public string penAId { get; set; }
        public string penBId { get; set; }
    }

    /// <summary>淘汰的表现层反馈（同上，由房主广播给各端本地播）</summary>
    public class EliminateFeedbackEvent : GameEvent
    {
        public string penId { get; set; }
    }

    /// <summary>撞障碍物的表现层反馈（同上，由房主广播给各端本地播）</summary>
    public class ObstacleFeedbackEvent : GameEvent
    {
        public Vector3 point { get; set; }
        public float impactForce { get; set; }
        public string penId { get; set; }
        public string obstacleId { get; set; }
    }

    #endregion
}
