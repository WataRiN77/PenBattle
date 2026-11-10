using PenBattleGame.Core;
using Unity.Netcode;
using UnityEngine;

/// 联机「对局同步」（房主权威）：
///
///   A. 回合状态下发：roundNumber / turnNumber / currentPlayerIndex / phase
///      —— 客户端靠它判断"现在轮到谁"，从而知道自己能不能操作。
///      用 NetworkVariable（状态），不用一次性 RPC：后加入的端也能立刻拿到当前状态。
///
///   B. 操作上报：客户端把「弹笔 / 放置」发给房主，房主校验后再执行。
///      校验用 ServerRpcParams.SenderClientId 反查玩家，**不信任客户端自报的 penId**。
///
/// 【场景准备】挂在和 NetworkMatchFlow 同一个物体上（复用那个 NetworkObject）。
public class NetworkMatchSync : NetworkBehaviour
{
    public static NetworkMatchSync Instance { get; private set; }

    // ================= A. 回合状态（房主写，各端读）=================

    private readonly NetworkVariable<int> _round = new NetworkVariable<int>(
        0, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
    private readonly NetworkVariable<int> _turn = new NetworkVariable<int>(
        0, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
    private readonly NetworkVariable<int> _currentPlayerIndex = new NetworkVariable<int>(
        -1, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
    private readonly NetworkVariable<int> _phase = new NetworkVariable<int>(
        (int)GameManager.TurnPhase.Idle, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    public int Round => _round.Value;
    public int Turn => _turn.Value;
    public int CurrentPlayerIndex => _currentPlayerIndex.Value;
    public int Phase => _phase.Value;

    public bool IsActionPhase => _phase.Value == (int)GameManager.TurnPhase.Action;

    /// 当前该行动的玩家 id（"Player1" 这种）；还没轮到任何人时为 null
    public string CurrentPlayerId => _currentPlayerIndex.Value >= 0
        ? $"Player{_currentPlayerIndex.Value + 1}"
        : null;

    // ================= 比分 / 淘汰（房主写，各端读）=================
    // 最多 4 名玩家，用两个 int 打包，省掉一堆 NetworkVariable：
    //   _scoresPacked  ：每 2 bit 存一名玩家的积分（先到 3 分获胜，2 bit 刚好放得下 0~3）
    //   _eliminatedMask：第 i 位 = Player(i+1) 本小局是否已被淘汰
    private const int MaxPlayersForSync = 4;

    private readonly NetworkVariable<int> _scoresPacked = new NetworkVariable<int>(
        0, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
    private readonly NetworkVariable<int> _eliminatedMask = new NetworkVariable<int>(
        0, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    public int GetScore(int playerIndex)
    {
        if (playerIndex < 0 || playerIndex >= MaxPlayersForSync) return 0;
        return (_scoresPacked.Value >> (playerIndex * 2)) & 0b11;
    }

    public bool IsEliminated(int playerIndex)
    {
        if (playerIndex < 0 || playerIndex >= MaxPlayersForSync) return false;
        return ((_eliminatedMask.Value >> playerIndex) & 1) != 0;
    }

    public void HostSetScore(int playerIndex, int score)
    {
        if (!IsServer || playerIndex < 0 || playerIndex >= MaxPlayersForSync) return;

        int v = _scoresPacked.Value;
        v &= ~(0b11 << (playerIndex * 2));
        v |= (Mathf.Clamp(score, 0, 3) << (playerIndex * 2));
        _scoresPacked.Value = v;
    }

    public void HostSetEliminated(int playerIndex, bool eliminated)
    {
        if (!IsServer || playerIndex < 0 || playerIndex >= MaxPlayersForSync) return;

        int v = _eliminatedMask.Value;
        v = eliminated ? (v | (1 << playerIndex)) : (v & ~(1 << playerIndex));
        _eliminatedMask.Value = v;
    }

    /// <summary>新小局开始时清空淘汰标记（积分跨小局保留）</summary>
    public void HostClearEliminated()
    {
        if (IsServer) _eliminatedMask.Value = 0;
    }

    public override void OnNetworkSpawn()
    {
        Instance = this;

        // 客户端侧诊断：把"收到的回合状态"打出来。
        // 排查"点了没反应"时，这一行能立刻区分是「房主没推」还是「推了但推错人」。
        _currentPlayerIndex.OnValueChanged += OnTurnStateChanged;
        _phase.OnValueChanged += OnTurnStateChanged;

        // 表现层反馈：房主把碰撞 / 淘汰广播给各端，让客户端也能播特效
        EventBus.Instance.Subscribe<PenPenCollisionEvent>(OnLocalPenPenCollision);
        EventBus.Instance.Subscribe<PenObstacleCollisionEvent>(OnLocalPenObstacleCollision);
        EventBus.Instance.Subscribe<PenEliminatedEvent>(OnLocalPenEliminated);
    }

    public override void OnNetworkDespawn()
    {
        if (Instance == this) Instance = null;

        _currentPlayerIndex.OnValueChanged -= OnTurnStateChanged;
        _phase.OnValueChanged -= OnTurnStateChanged;

        EventBus.Instance.Unsubscribe<PenPenCollisionEvent>(OnLocalPenPenCollision);
        EventBus.Instance.Unsubscribe<PenObstacleCollisionEvent>(OnLocalPenObstacleCollision);
        EventBus.Instance.Unsubscribe<PenEliminatedEvent>(OnLocalPenEliminated);
    }

    private void OnTurnStateChanged(int _, int __)
    {
        // 房主侧已经有 GameManager.PushTurnState 的 [回合] 日志了，这里只管客户端
        if (IsServer) return;

        var flow = NetworkMatchFlow.Instance;
        string me = flow != null ? (flow.MyPlayerId ?? "未分配") : "无 flow";

        Debug.Log($"[回合] 客户端收到 → 小局 {Round} · 回合 {Turn} · " +
                  $"轮到 {CurrentPlayerId ?? "-"} · {(GameManager.TurnPhase)Phase} · 我是 {me}");
    }

    // ================= 表现层反馈：房主广播 → 各端本地播 =================
    // 客户端的两支笔是 kinematic、不跑物理，收不到 OnCollisionEnter；
    // 而且 PenPenCollisionEvent 只由 penId 较小的一方发 → 客户端永远收不到。
    // 所以这里由房主统一广播，各端本地发"纯表现层"事件（CollisionFeedbackEvent），
    // 与玩法侧事件分离，避免客户端把被动技能也跑一遍。

    private void OnLocalPenPenCollision(PenPenCollisionEvent evt)
    {
        if (!IsServer) return;

        BroadcastCollisionClientRpc(evt.collisionPoint, evt.relativeSpeed, evt.penAId, evt.penBId);
        PublishCollisionFeedback(evt.collisionPoint, evt.relativeSpeed, evt.penAId, evt.penBId);   // 房主自己也播
    }

    private void OnLocalPenEliminated(PenEliminatedEvent evt)
    {
        if (!IsServer) return;

        BroadcastEliminateClientRpc(evt.penId);
        EventBus.Instance.Publish(new EliminateFeedbackEvent { penId = evt.penId });
    }

    private void OnLocalPenObstacleCollision(PenObstacleCollisionEvent evt)
    {
        if (!IsServer) return;

        BroadcastObstacleClientRpc(evt.collisionPoint, evt.impactForce, evt.penId, evt.obstacleId);
        PublishObstacleFeedback(evt.collisionPoint, evt.impactForce, evt.penId, evt.obstacleId);
    }

    [ClientRpc]
    private void BroadcastObstacleClientRpc(Vector3 point, float impactForce, string penId, string obstacleId)
    {
        PublishObstacleFeedback(point, impactForce, penId, obstacleId);
    }

    private static void PublishObstacleFeedback(Vector3 point, float impactForce, string penId, string obstacleId)
    {
        EventBus.Instance.Publish(new ObstacleFeedbackEvent
        {
            point = point,
            impactForce = impactForce,
            penId = penId,
            obstacleId = obstacleId
        });
    }

    [ClientRpc]
    private void BroadcastCollisionClientRpc(Vector3 point, float relativeSpeed, string penAId, string penBId)
    {
        PublishCollisionFeedback(point, relativeSpeed, penAId, penBId);
    }

    [ClientRpc]
    private void BroadcastEliminateClientRpc(string penId)
    {
        EventBus.Instance.Publish(new EliminateFeedbackEvent { penId = penId });
    }

    private static void PublishCollisionFeedback(Vector3 point, float relativeSpeed, string penAId, string penBId)
    {
        EventBus.Instance.Publish(new CollisionFeedbackEvent
        {
            point = point,
            relativeSpeed = relativeSpeed,
            penAId = penAId,
            penBId = penBId
        });
    }

    /// <summary>房主：广播冲击波视觉（推力已由房主自己算完，客户端只播动画）</summary>
    public void HostBroadcastShockwave(Vector3 center, float radius, float duration)
    {
        if (IsServer) ShockwaveClientRpc(center, radius, duration);
    }

    [ClientRpc]
    private void ShockwaveClientRpc(Vector3 center, float radius, float duration)
    {
        ShockwaveEffect.SpawnVisual(center, radius, duration);
    }

    // ---- 房主写入接口 ----
    public void HostSetRound(int value) { if (IsServer) _round.Value = value; }
    public void HostSetTurn(int value) { if (IsServer) _turn.Value = value; }
    public void HostSetCurrentPlayerIndex(int value) { if (IsServer) _currentPlayerIndex.Value = value; }
    public void HostSetPhase(GameManager.TurnPhase value) { if (IsServer) _phase.Value = (int)value; }

    // ================= B. 操作上报（客户端 → 房主）=================

    private InputManager _input;

    private float MaxLaunchForce
    {
        get
        {
            if (_input == null) _input = FindObjectOfType<InputManager>();
            return _input != null ? _input.maxLaunchForce : 10f;
        }
    }

    /// <summary>客户端侧调用：把一次弹笔上报给房主（房主自己直接走本地逻辑，不用经过这里）</summary>
    public void SubmitLaunch(Vector3 direction, float force, Vector3 clickPoint)
    {
        LaunchServerRpc(direction, force, clickPoint);
    }

    /// <summary>客户端侧调用：把一次放置上报给房主</summary>
    public void SubmitPlace(Vector3 targetPos)
    {
        PlaceServerRpc(targetPos);
    }

    [ServerRpc(RequireOwnership = false)]
    private void LaunchServerRpc(Vector3 direction, float force, Vector3 clickPoint, ServerRpcParams p = default)
    {
        if (!TryResolveSenderPlayer(p.Receive.SenderClientId, out var player)) return;

        var gm = GameManager.Instance;
        if (gm == null) return;

        if (!gm.IsMyTurn(player.playerId))
        {
            Debug.LogWarning($"[联机] 拒绝越权弹笔：clientId={p.Receive.SenderClientId} " +
                             $"（{player.playerId}）现在不是他的回合");
            return;
        }

        // 力度夹紧，防止客户端伪造超大力
        float clamped = Mathf.Clamp(force, 0f, MaxLaunchForce);
        Vector3 dir = direction.sqrMagnitude > 0.0001f ? direction.normalized : player.pen.transform.forward;

        // 先广播"即将发射"，让房主的被动/结算走和单机完全相同的路径
        EventBus.Instance.Publish(new InputLaunchEvent
        {
            penId = player.playerId,
            launchDirection = dir,
            launchForce = clamped
        });

        player.pen.Launch(dir, clamped, clickPoint);
        Debug.Log($"[联机] 执行远端弹笔：{player.playerId}，力度 {clamped:F2}");
    }

    [ServerRpc(RequireOwnership = false)]
    private void PlaceServerRpc(Vector3 targetPos, ServerRpcParams p = default)
    {
        if (!TryResolveSenderPlayer(p.Receive.SenderClientId, out var player)) return;

        var gm = GameManager.Instance;
        if (gm == null) return;

        if (!gm.IsMyTurn(player.playerId))
        {
            Debug.LogWarning($"[联机] 拒绝越权放置：clientId={p.Receive.SenderClientId}（{player.playerId}）");
            return;
        }

        gm.PlayerPlacePen(player.playerId, targetPos);
        Debug.Log($"[联机] 执行远端放置：{player.playerId} → {targetPos}");
    }

    /// <summary>客户端侧调用：把一次技能释放上报房主（targetPlayerIndex &lt; 0 表示无目标）</summary>
    public void SubmitSkill(int skillIndex, int targetPlayerIndex, Vector3 direction)
    {
        SkillServerRpc(skillIndex, targetPlayerIndex, direction);
    }

    [ServerRpc(RequireOwnership = false)]
    private void SkillServerRpc(int skillIndex, int targetPlayerIndex, Vector3 direction, ServerRpcParams p = default)
    {
        if (!TryResolveSenderPlayer(p.Receive.SenderClientId, out var player)) return;

        var gm = GameManager.Instance;
        if (gm == null) return;

        if (!gm.IsMyTurn(player.playerId))
        {
            Debug.LogWarning($"[联机] 拒绝越权用技能：clientId={p.Receive.SenderClientId}（{player.playerId}）");
            return;
        }

        // 客户端只上报原始参数，这里才还原成 SkillTargetContext —— PlayerData 不过网
        var ctx = SkillTargetContext.None;

        if (targetPlayerIndex >= 0)
        {
            var target = gm.players.Find(pl => pl.playerIndex == targetPlayerIndex);
            if (target == null)
            {
                Debug.LogWarning($"[联机] 技能目标不存在：playerIndex={targetPlayerIndex}");
                return;
            }
            ctx = SkillTargetContext.ForPlayer(target);
        }
        else if (direction.sqrMagnitude > 0.0001f)
        {
            ctx = SkillTargetContext.ForDirection(direction);
        }

        gm.PlayerUseSkill(player.playerId, skillIndex, ctx);
        Debug.Log($"[联机] 执行远端技能：{player.playerId} 技能#{skillIndex}（目标 {targetPlayerIndex}）");
    }

    /// <summary>用发送方 clientId 反查出玩家 —— 不信任客户端自报的身份</summary>
    private static bool TryResolveSenderPlayer(ulong senderClientId, out PlayerData player)
    {
        player = null;

        var flow = NetworkMatchFlow.Instance;
        var gm = GameManager.Instance;
        if (flow == null || gm == null) return false;

        if (!flow.TryGetPlayerIndex(senderClientId, out int index)) return false;

        player = gm.players.Find(pl => pl.playerIndex == index);
        return player != null && player.pen != null;
    }

    // ================= 联机调试 HUD（阶段⑥换正式 UI 时可关掉）=================

    [Header("调试 HUD")]
    public bool showHud = true;

    private void OnGUI()
    {
        if (!showHud) return;

        var flow = NetworkMatchFlow.Instance;
        if (flow == null) return;

        NetUi.Begin();
        GUILayout.BeginArea(new Rect(20f, 360f, 340f, 170f), GUI.skin.box);

        GUILayout.Label($"第 {Round} 小局 · 第 {Turn} 回合   [{PhaseName()}]");
        GUILayout.Label($"轮到：{CurrentPlayerId ?? "-"}");

        int mine = flow.MyPlayerIndex;
        if (mine >= 0) GUILayout.Label($"我是 Player{mine + 1}");

        GUILayout.Space(4f);
        for (int i = 0; i < MaxPlayersForSync; i++)
        {
            if (!flow.HasPlayerIndex(i)) continue;

            string tag = IsEliminated(i) ? "   [已淘汰]" : "";
            string me = i == mine ? "   <-我" : "";
            GUILayout.Label($"Player{i + 1}   积分 {GetScore(i)}{tag}{me}");
        }

        GUILayout.EndArea();
        NetUi.End();
    }

    private string PhaseName()
    {
        switch ((GameManager.TurnPhase)Phase)
        {
            case GameManager.TurnPhase.Action: return "行动中";
            case GameManager.TurnPhase.Resolving: return "物理结算";
            case GameManager.TurnPhase.RoundEnd: return "小局结束";
            case GameManager.TurnPhase.MatchEnd: return "比赛结束";
            default: return "准备";
        }
    }
}
