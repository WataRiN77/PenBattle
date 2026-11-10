using System.Collections.Generic;
using System.Linq;
using Unity.Netcode;
using UnityEngine;

/// 联机「选笔」流程（房主权威）。
///
/// 每个玩家在**自己的机器**上选自己的笔 → 通过 ServerRpc 上报房主 →
/// 房主收齐所有人后调用 GameManager.BeginMatch（笔的网络生成在那边做）。
///
/// 【场景准备】本脚本所在物体必须挂 NetworkObject，且是**场景根物体**
/// （in-scene NetworkObject）。可以就挂在现有的 NetworkLauncher 物体上。
[RequireComponent(typeof(NetworkObject))]
public class NetworkMatchFlow : NetworkBehaviour
{
    [Header("规则")]
    [Tooltip("满几个人开赛（含房主自己）")]
    public int expectedPlayers = 2;

    [Header("界面")]
    public bool showUI = true;

    private PenConfig[] _penConfigs;

    // 全端共有：clientId → playerIndex
    private readonly Dictionary<ulong, int> _indexOf = new Dictionary<ulong, int>();
    // 房主侧：clientId → 已选 penConfig 下标（-1 表示还没选）
    private readonly Dictionary<ulong, int> _choice = new Dictionary<ulong, int>();

    private int _nextIndex;         // 房主分配 playerIndex 用
    private int _chosenMask;        // 位图：第 i 位 = Player(i+1) 已选
    private bool _localSubmitted;   // 本端自己是否已提交
    private int _myChoiceIndex = -1; // 本端选了哪支笔（客户端要用它查自己有哪些技能）
    private bool _matchStarted;
    private bool _selectionOpen;    // 房主点「开始游戏」后才开放选笔（全端同步）
    private string _msg = "等待玩家加入…";

    private static NetworkManager NM => NetworkManager.Singleton;

    /// <summary>场景里唯一的这个组件，供其它联机脚本查询席位</summary>
    public static NetworkMatchFlow Instance { get; private set; }

    /// <summary>本端分到的 Player 序号（0 起）；还没分到席位返回 -1</summary>
    public int MyPlayerIndex
    {
        get
        {
            var nm = NM;
            if (nm == null) return -1;
            return _indexOf.TryGetValue(nm.LocalClientId, out int idx) ? idx : -1;
        }
    }

    /// <summary>本端自己的 Player id（"Player1"…）；还没分到席位返回 null</summary>
    public string MyPlayerId
    {
        get { int i = MyPlayerIndex; return i >= 0 ? $"Player{i + 1}" : null; }
    }

    /// <summary>
    /// 本端选的笔配置 —— 客户端靠它知道自己有哪些技能。
    /// （服务端不会把技能表发下来，客户端也没法从玩家的 PlayerData 拿，那边是空的。）
    /// </summary>
    public PenConfig MyPenConfig =>
        _myChoiceIndex >= 0 && _penConfigs != null && _myChoiceIndex < _penConfigs.Length
            ? _penConfigs[_myChoiceIndex]
            : null;

    /// <summary>这个席位有没有人（HUD 用它决定显示哪几行）</summary>
    public bool HasPlayerIndex(int playerIndex)
    {
        foreach (var kv in _indexOf)
            if (kv.Value == playerIndex) return true;
        return false;
    }

    /// <summary>按 clientId 反查 Player 序号（房主校验操作来源时用）</summary>
    public bool TryGetPlayerIndex(ulong clientId, out int playerIndex)
    {
        return _indexOf.TryGetValue(clientId, out playerIndex);
    }

    /// <summary>按 Player 序号查 clientId（房主校验"越权替别人选"时用）</summary>
    public bool TryGetClientId(int playerIndex, out ulong clientId)
    {
        foreach (var kv in _indexOf)
        {
            if (kv.Value == playerIndex)
            {
                clientId = kv.Key;
                return true;
            }
        }
        clientId = 0;
        return false;
    }

    // ================= 生命周期 =================

    public override void OnNetworkSpawn()
    {
        Instance = this;

        // 两端都用同一份 Resources，并按 configId 排序 ——
        // 保证下标在两端一致，RPC 只传 int 就够，不受资源加载顺序影响
        _penConfigs = Resources.LoadAll<PenConfig>("Pens")
                              .Where(c => c != null)
                              .OrderBy(c => c.configId)
                              .ToArray();

        if (_penConfigs.Length == 0)
            Debug.LogError("[联机] Resources/Pens 下没有 PenConfig，无法选笔。");

        if (IsServer)
        {
            // 注意：NetworkBehaviour 自己有个叫 NetworkManager 的实例属性，会遮住类型名，
            // 所以这里必须用完全限定名，否则 ServerClientId（静态成员）取不到
            AssignIndex(Unity.Netcode.NetworkManager.ServerClientId);   // 房主自己 = Player1（下标 0）
            NM.OnClientConnectedCallback += HostOnClientConnected;
            NM.OnClientDisconnectCallback += HostOnClientDisconnected;
        }

        if (IsServer) RefreshHostState();
    }

    public override void OnNetworkDespawn()
    {
        if (Instance == this) Instance = null;

        if (IsServer && NM != null)
        {
            NM.OnClientConnectedCallback -= HostOnClientConnected;
            NM.OnClientDisconnectCallback -= HostOnClientDisconnected;
        }
    }

    // ================= 房主侧 =================

    private void HostOnClientConnected(ulong clientId)
    {
        AssignIndex(clientId);
        RefreshHostState();
    }

    private void HostOnClientDisconnected(ulong clientId)
    {
        _indexOf.Remove(clientId);
        _choice.Remove(clientId);
        RefreshHostState();
    }

    private void AssignIndex(ulong clientId)
    {
        if (_indexOf.ContainsKey(clientId)) return;
        _indexOf[clientId] = _nextIndex++;
        _choice[clientId] = -1;
    }

    private void RefreshHostState()
    {
        if (!IsServer) return;

        BroadcastRoster();

        // ① 还没点「开始游戏」：只看人够不够，不做"收齐就开赛"的判断
        if (!_selectionOpen)
        {
            _msg = _indexOf.Count >= expectedPlayers
                 ? "人都到齐了，点「开始游戏」进入选笔"
                 : $"等待玩家加入：{_indexOf.Count}/{expectedPlayers}";
            return;
        }

        // ② 已开放选笔：等所有人选完
        int total = _indexOf.Count;
        int chosen = _choice.Values.Count(v => v >= 0);

        if (chosen < total)
        {
            _msg = $"等待其他玩家选笔：{chosen}/{total}";
        }
        else
        {
            _msg = "全部选完，开赛！";
            StartMatchOnHost();
        }
    }

    /// <summary>房主：点「开始游戏」，开放选笔阶段（全端同步）</summary>
    public void HostOpenSelection()
    {
        if (!IsServer) return;
        if (_selectionOpen) return;        // 防重入：IMGUI 同一帧可能触发多次

        if (_indexOf.Count < expectedPlayers)
        {
            Debug.LogWarning($"[联机] 还有 {expectedPlayers - _indexOf.Count} 个玩家没进来，不能开始。");
            return;
        }

        _selectionOpen = true;
        SelectionOpenClientRpc();

        Debug.Log($"[联机] 房主点了「开始游戏」，{_indexOf.Count} 名玩家进入选笔阶段。");
        RefreshHostState();
    }

    [ClientRpc]
    private void SelectionOpenClientRpc()
    {
        _selectionOpen = true;
        _msg = "请选择你的笔";
    }

    private void BroadcastRoster()
    {
        var ids = _indexOf.Keys.OrderBy(id => _indexOf[id]).ToArray();
        var indices = ids.Select(id => _indexOf[id]).ToArray();

        int mask = 0;
        foreach (var kv in _choice)
        {
            if (kv.Value >= 0 && _indexOf.TryGetValue(kv.Key, out int idx))
                mask |= 1 << idx;
        }

        // 用 int 而不是 bool 传：NGO 的 RPC 参数对类型挑剔，数值最稳
        RosterClientRpc(ids, indices, mask, _selectionOpen ? 1 : 0);
    }

    [ClientRpc]
    private void RosterClientRpc(ulong[] clientIds, int[] indices, int chosenMask, int selectionOpen)
    {
        _indexOf.Clear();
        for (int i = 0; i < clientIds.Length; i++)
            _indexOf[clientIds[i]] = indices[i];

        _chosenMask = chosenMask;
        _selectionOpen = selectionOpen != 0;

        int total = clientIds.Length;
        int chosen = 0;
        for (int i = 0; i < total; i++) if ((chosenMask & (1 << i)) != 0) chosen++;

        if (!_selectionOpen)
            _msg = total >= expectedPlayers
                 ? "人都到齐了，等房主点「开始游戏」"
                 : $"等待玩家加入：{total}/{expectedPlayers}";
        else if (chosen < total)
            _msg = $"等待其他玩家选笔：{chosen}/{total}";
        else
            _msg = "全部选完，开赛！";
    }

    [ClientRpc]
    private void MatchStartingClientRpc()
    {
        _matchStarted = true;
        _msg = "比赛开始";
    }

    private void StartMatchOnHost()
    {
        if (_matchStarted) return;
        _matchStarted = true;
        MatchStartingClientRpc();

        var setups = _indexOf.OrderBy(kv => kv.Value)
                             .Select(kv =>
                             {
                                 int pi = kv.Value;
                                 int ci = Mathf.Clamp(_choice[kv.Key], 0, _penConfigs.Length - 1);
                                 var cfg = _penConfigs[ci];
                                 return new PlayerSetupData
                                 {
                                     playerId = $"Player{pi + 1}",
                                     playerName = $"玩家{pi + 1}",
                                     penConfig = cfg,
                                     skills = cfg != null && cfg.activeSkills != null
                                              ? new List<SkillConfig>(cfg.activeSkills)
                                              : new List<SkillConfig>()
                                 };
                             })
                             .ToList();

        Debug.Log($"[联机] 房主收齐 {setups.Count} 名玩家的选择，开始比赛。");
        GameManager.Instance.BeginMatch(setups);

        // 地图：房主在 BeginMatch 里已经抽好，把 mapId 下发给客户端装载同一张
        var mm = MapManager.Instance;
        if (mm != null && mm.Current != null)
            HostApplyMap(mm.Current.mapId);   // 走统一入口：空 mapId 会报错，不再静默失败
        else
            Debug.LogWarning("[联机] 房主没有 MapManager 或地图没装载成功，客户端会没有地图。");
    }

    /// <summary>联机：把房主选定的地图下发给所有客户端（客户端不做随机）</summary>
    [ClientRpc]
    private void ApplyMapClientRpc(string mapId)
    {
        if (MapManager.Instance != null)
            MapManager.Instance.ApplyById(mapId);
        else
            Debug.LogWarning("[联机] 客户端场景里没有 MapManager，无法装载地图。");
    }

    /// <summary>房主侧调用：正式开打，让各端把地图显示出来（刚装载时是隐藏的）</summary>
    public void HostShowMap()
    {
        if (IsServer) ShowMapClientRpc();
    }

    /// <summary>
    /// 房主侧调用：把**当前**地图下发给所有客户端。
    /// 每小局 StartNewRound 重抽地图后必须调一次，否则从第 2 小局起各端桌面不一致。
    /// </summary>
    public void HostApplyMap(string mapId)
    {
        if (!IsServer) return;

        // 以前是静默 return，结果「所有 MapConfig 共用一个 mapId」这种配置错误
        // 会表现成「客户端永远显示同一张图」，查起来毫无线索。字段是同步键，必须吵。
        if (string.IsNullOrEmpty(mapId))
        {
            Debug.LogError("[联机] 房主当前地图的 mapId 为空，客户端无法同步（检查 MapConfig 资产）。");
            return;
        }

        ApplyMapClientRpc(mapId);
    }

    [ClientRpc]
    private void ShowMapClientRpc()
    {
        if (MapManager.Instance != null) MapManager.Instance.ShowMap();
    }

    // ================= 上报（客户端 → 房主） =================

    [ServerRpc(RequireOwnership = false)]
    private void SubmitPenServerRpc(int penConfigIndex, ServerRpcParams p = default)
    {
        ulong from = p.Receive.SenderClientId;
        if (!_indexOf.ContainsKey(from)) return;
        if (penConfigIndex < 0 || penConfigIndex >= _penConfigs.Length) return;

        _choice[from] = penConfigIndex;
        Debug.Log($"[联机] 收到 clientId={from}（Player{_indexOf[from] + 1}）选笔：{_penConfigs[penConfigIndex].displayName}");

        RefreshHostState();
    }

    private void SubmitPen(int penConfigIndex)
    {
        if (penConfigIndex < 0 || penConfigIndex >= _penConfigs.Length) return;

        _myChoiceIndex = penConfigIndex;      // 记下自己选了哪支（客户端要用它查自己的技能）
        _localSubmitted = true;
        _msg = "已提交，等待其他玩家…";
        SubmitPenServerRpc(penConfigIndex);   // 房主本地调用也会走同一条路
    }

    // ================= 面板 =================

    private void OnGUI()
    {
        if (!showUI || _matchStarted) return;
        if (_penConfigs == null || _penConfigs.Length == 0) return;
        if (NM == null) return;

        ulong me = NM.LocalClientId;
        _indexOf.TryGetValue(me, out int myIndex);
        bool seated = _indexOf.ContainsKey(me);

        NetUi.Begin();
        GUILayout.BeginArea(new Rect(NetUi.LogicalWidth * 0.5f - 210f, 40f, 420f, 320f), GUI.skin.box);

        GUILayout.Label(seated ? $"你是 Player{myIndex + 1}" : "尚未分配席位");
        GUILayout.Label(_msg);

        GUILayout.Space(6f);

        if (!_selectionOpen)
        {
            // 选笔还没开门：房主看到「开始游戏」按钮（人齐才可点），客户端只等待
            if (IsServer)
            {
                bool enough = _indexOf.Count >= expectedPlayers;

                GUI.enabled = enough;
                if (GUILayout.Button($"开始游戏（已就绪 {_indexOf.Count}/{expectedPlayers}）", GUILayout.Height(44f)))
                    HostOpenSelection();
                GUI.enabled = true;

                if (!enough) GUILayout.Label("等客户端连进来后按钮才能点");
            }
            else
            {
                GUILayout.Label($"等待房主开始游戏…（{_indexOf.Count}/{expectedPlayers}）");
            }
        }
        else
        {
            // 选笔阶段
            bool canPick = seated && !_localSubmitted && ((_chosenMask & (1 << myIndex)) == 0);

            GUI.enabled = canPick;
            for (int i = 0; i < _penConfigs.Length; i++)
            {
                if (GUILayout.Button(_penConfigs[i].displayName, GUILayout.Height(28f)))
                {
                    SubmitPen(i);
                    break;
                }
            }
            GUI.enabled = true;

            GUILayout.Space(6f);
            GUILayout.Label($"已选：{CountChosen()}/{_indexOf.Count}");
        }

        GUILayout.EndArea();
        NetUi.End();
    }

    private int CountChosen()
    {
        int n = 0;
        foreach (var kv in _indexOf)
            if ((_chosenMask & (1 << kv.Value)) != 0) n++;
        return n;
    }
}
