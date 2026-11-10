using System.Linq;
using UnityEngine;
using PenBattleGame.Core;

// 被动技能运行时基类：挂在笔对象上，按事件触发，或直接施加常驻效果
public abstract class PassiveSkill : MonoBehaviour
{
    [HideInInspector] public PassiveSkillEntry entry;

    // 由"行为型强化"挂载的标记（区别于笔自带的被动）。
    // UpgradeSystem.ApplyAll 每小局会重跑，靠这个标记把上一轮挂的强化行为清掉再重建。
    //
    // ⚠️ 子类约定：**不要**在 OnDisable / OnDestroy 里撤销 ApplyStaticEffect 写进笔的修饰量。
    //    修饰量统一由 PenController.RestoreBaseValues() 归零，之后重建组件重新加；
    //    如果在 OnDisable 里再减一次，会减成负数（RestoreBaseValues 在 Detach 之前跑）。
    [HideInInspector] public bool fromUpgrade;

    protected PenController _pen;

    // 由 PassiveSkillManager 在 AddComponent 之后立刻调用
    // （Awake/OnEnable 比它早，所以子类里不要在这两个方法里读 entry）
    public void Bind(PenController pen, PassiveSkillEntry e)
    {
        _pen = pen;
        entry = e;
        ApplyStaticEffect();
    }

    protected bool Ready => _pen != null && entry != null;
    protected bool IsMe(string penId) => _pen != null && penId == _pen.penId;

    // ===== 子类按需覆写：常驻效果 =====
    protected virtual void ApplyStaticEffect() { }

    // ===== 子类按需覆写：事件触发效果 =====
    // 术语：小局 (Round) = 开局到只剩一名玩家存活；回合 (Turn) = 小局内每位存活玩家各行动一次；
    //       行动 (Action) = 单个玩家的一次操作（弹笔 / 放置 / 使用技能）
    protected virtual void OnRoundStart() { }     // 小局开始
    protected virtual void OnTurnStarted() { }    // 回合开始
    protected virtual void OnTurnEnded() { }      // 回合结束（所有存活玩家各行动一次之后）
    protected virtual void OnLaunch() { }         // 自己发射前（可改本次发射力）
    protected virtual void OnActionEnded() { }    // 自己的"行动"结束
    protected virtual void OnHitPen(PenPenCollisionEvent e) { }
    protected virtual void OnDamaged(PenDamagedEvent e) { }
    protected virtual void OnHealed(PenHealedEvent e) { }   // 自身耐久被回复（e.amount = 实际回复量，e.source = 来源）
    protected virtual void OnEliminated() { }

    private void OnEnable()
    {
        EventBus.Instance.Subscribe<RoundStartEvent>(HandleRoundStart);
        EventBus.Instance.Subscribe<TurnStartedEvent>(HandleTurnStarted);
        EventBus.Instance.Subscribe<TurnEndedEvent>(HandleTurnEnded);
        EventBus.Instance.Subscribe<InputLaunchEvent>(HandleLaunch);
        EventBus.Instance.Subscribe<ActionEndedEvent>(HandleActionEnded);
        EventBus.Instance.Subscribe<PenPenCollisionEvent>(HandlePenPen);
        EventBus.Instance.Subscribe<PenDamagedEvent>(HandleDamaged);
        EventBus.Instance.Subscribe<PenHealedEvent>(HandleHealed);
        EventBus.Instance.Subscribe<PenEliminatedEvent>(HandleEliminated);
    }

    private void OnDisable()
    {
        EventBus.Instance.Unsubscribe<RoundStartEvent>(HandleRoundStart);
        EventBus.Instance.Unsubscribe<TurnStartedEvent>(HandleTurnStarted);
        EventBus.Instance.Unsubscribe<TurnEndedEvent>(HandleTurnEnded);
        EventBus.Instance.Unsubscribe<InputLaunchEvent>(HandleLaunch);
        EventBus.Instance.Unsubscribe<ActionEndedEvent>(HandleActionEnded);
        EventBus.Instance.Unsubscribe<PenPenCollisionEvent>(HandlePenPen);
        EventBus.Instance.Unsubscribe<PenDamagedEvent>(HandleDamaged);
        EventBus.Instance.Unsubscribe<PenHealedEvent>(HandleHealed);
        EventBus.Instance.Unsubscribe<PenEliminatedEvent>(HandleEliminated);
    }

    private void HandleRoundStart(RoundStartEvent e)
    {
        if (!Ready) return;
        if (e.playerIds != null && !e.playerIds.Contains(_pen.penId)) return;
        OnRoundStart();
    }

    private void HandleTurnStarted(TurnStartedEvent e)
    {
        if (!Ready) return;
        OnTurnStarted();
    }

    private void HandleTurnEnded(TurnEndedEvent e)
    {
        if (!Ready) return;
        OnTurnEnded();
    }

    private void HandleLaunch(InputLaunchEvent e)
    {
        if (!Ready || !IsMe(e.penId)) return;
        OnLaunch();
    }

    private void HandleActionEnded(ActionEndedEvent e)
    {
        if (!Ready || !IsMe(e.playerId)) return;
        OnActionEnded();
    }

    private void HandlePenPen(PenPenCollisionEvent e)
    {
        if (!Ready) return;
        if (!IsMe(e.penAId) && !IsMe(e.penBId)) return;
        OnHitPen(e);
    }

    private void HandleDamaged(PenDamagedEvent e)
    {
        if (!Ready || !IsMe(e.penId)) return;
        OnDamaged(e);
    }

    // ⚠️ 在 OnHealed 里再调 PenStatus.Restore 会**递归触发自己**（事件套事件）。
    //    要做"回血转护盾/转伤害"这类转化，务必用 e.source 过滤掉自己，或改用累积值+回合末结算。
    private void HandleHealed(PenHealedEvent e)
    {
        if (!Ready || !IsMe(e.penId)) return;
        OnHealed(e);
    }

    private void HandleEliminated(PenEliminatedEvent e)
    {
        if (!Ready || !IsMe(e.penId)) return;
        OnEliminated();
    }

    // ===== 工具 =====

    protected PenController FindPen(string penId)
    {
        var gm = GameManager.Instance;
        if (gm == null) return null;
        var p = gm.players.Find(x => x.pen != null && x.pen.penId == penId);
        return p != null ? p.pen : null;
    }

    // 撞击事件里除自己以外的那一支
    protected PenController FindOpponent(PenPenCollisionEvent e)
    {
        if (_pen == null) return null;
        string otherId = e.penAId == _pen.penId ? e.penBId : e.penAId;
        return FindPen(otherId);
    }
}
