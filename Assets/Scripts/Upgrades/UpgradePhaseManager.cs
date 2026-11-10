using System;
using System.Collections.Generic;
using UnityEngine;
using PenBattleGame.Core;

// 强化选择流程：开局强化 / 每小局败者强化 共用
//
// 抽取规则：
//   候选池 = 通用强化池(UpgradePool) + 该玩家所选笔的专属强化(PenConfig.exclusiveUpgrades)
//   每次选择时，从这个合并池里随机抽 3 个给玩家选（每次都会重新抽）
//
// 由 GameManager 调 StartPhase(...) 启动；选完自动应用
// （UpgradeSystem.ApplyAll 基于基准值重算，不会叠加）
// 自带一个 IMGUI 调试选择界面（阶段 6 做正式 UI 时可关掉 showDebugUI）
public class UpgradePhaseManager : MonoBehaviour
{
    [Header("抽取")]
    [Tooltip("每次给玩家几个选项")]
    public int optionCount = 3;

    [Header("调试")]
    [Tooltip("打开后左下角出现简易选择界面，方便现在就测试")]
    public bool showDebugUI = true;

    private static UpgradePhaseManager _instance;
    public static UpgradePhaseManager Instance
    {
        get
        {
            if (_instance == null)
            {
                var go = new GameObject("UpgradePhaseManager");
                _instance = go.AddComponent<UpgradePhaseManager>();
            }
            return _instance;
        }
    }

    private List<PlayerData> _queue = new List<PlayerData>();
    private List<UpgradeEntry> _options = new List<UpgradeEntry>();   // 本次抽到的选项
    private int _index;
    private int _remaining;              // 当前玩家还剩几个没选
    private int _countPerPlayer;
    private bool _beforeMatch;
    private bool _active;
    private Action _onComplete;

    // 给正式 UI 用
    public PlayerData CurrentPlayer =>
        (_active && _index >= 0 && _index < _queue.Count) ? _queue[_index] : null;

    public List<UpgradeEntry> CurrentOptions => _options;

    private void Awake()
    {
        if (_instance != null && _instance != this)
        {
            Destroy(gameObject);
            return;
        }
        _instance = this;
    }

    /// <summary>
    /// 启动一次强化阶段。候选为空或没有可抽选项时直接回调 onComplete（不会卡住流程）。
    /// </summary>
    public void StartPhase(List<PlayerData> eligible, int countPerPlayer, bool beforeMatch, Action onComplete)
    {
        if (eligible == null || eligible.Count == 0)
        {
            onComplete?.Invoke();
            return;
        }

        _queue = new List<PlayerData>(eligible);
        _countPerPlayer = Mathf.Max(1, countPerPlayer);
        _beforeMatch = beforeMatch;
        _onComplete = onComplete;
        _index = -1;
        _active = true;

        EventBus.Instance.Publish(new UpgradePhaseStartEvent
        {
            roundNumber = GameManager.Instance != null ? GameManager.Instance.RoundNumber : 0,
            eligiblePlayerIds = _queue.ConvertAll(p => p.playerId),
            upgradeCount = _countPerPlayer,
            beforeMatch = beforeMatch
        });

        Debug.Log($"[强化] 强化阶段开始（{(beforeMatch ? "开局" : "败者")}）：{_queue.Count} 人，每人 {_countPerPlayer} 个");

        // 强化阶段（尤其是"开局强化"）不该看到笔 —— 在这里再兜一次底。
        // 把"准备阶段笔不可见"收敛到这一个明确边界上，避免建笔时机或可见性被别处改动后漏掉。
        foreach (var p in _queue)
        {
            if (p == null || p.pen == null) continue;

            p.pen.SetKinematic(true);
            p.pen.SetVisible(false);

            var bridge = p.pen.GetComponent<PenNetworkBridge>();
            if (bridge != null) bridge.HostSetVisible(false);   // 联机：同时同步给客户端
        }

        NextPlayer();
    }

    private void NextPlayer()
    {
        _index++;
        if (_index >= _queue.Count)
        {
            Finish();
            return;
        }

        _remaining = _countPerPlayer;
        RollOptions();
    }

    // 合并"通用池 + 该玩家笔的专属池"，随机抽 N 个
    private void RollOptions()
    {
        var player = _queue[_index];

        var pool = new List<UpgradeEntry>();

        var common = UpgradePool.Load();
        if (common != null && common.commonUpgrades != null)
        {
            foreach (var u in common.commonUpgrades)
                if (u != null) pool.Add(u);
        }

        if (player.penConfig != null && player.penConfig.exclusiveUpgrades != null)
        {
            foreach (var u in player.penConfig.exclusiveUpgrades)
                if (u != null) pool.Add(u);
        }

        _options = TakeRandom(pool, Mathf.Max(1, optionCount));

        var names = new List<string>();
        foreach (var o in _options) names.Add(o.DisplayName);

        Debug.Log($"[强化] {player.playerName}（{player.penConfig?.displayName}）抽到：{string.Join(" / ", names)}");

        EventBus.Instance.Publish(new UpgradeOptionsRolledEvent
        {
            playerId = player.playerId,
            options = new List<UpgradeEntry>(_options)
        });

        // 联机：把「轮到谁 + 选项」广播出去，只有该玩家自己的客户端会把按钮显示出来
        if (NetworkUpgradeBridge.Instance != null)
        {
            NetworkUpgradeBridge.Instance.HostShowOptions(player.playerIndex, _remaining, _options);
        }
        else if (Unity.Netcode.NetworkManager.Singleton != null)
        {
            // 联机模式下桥接缺失 = 强化面板谁都看不到，必须吼出来，别静默失败
            Debug.LogError("[强化] 联机模式下找不到 NetworkUpgradeBridge！请到挂了 NetworkMatchFlow 的" +
                           "那个物体上 Add Component → NetworkUpgradeBridge，否则强化面板不会显示给任何人。");
        }
    }

    // 洗牌后取前 n 个（不放回，保证互不重复）
    private static List<UpgradeEntry> TakeRandom(List<UpgradeEntry> source, int n)
    {
        var copy = new List<UpgradeEntry>(source);

        for (int i = copy.Count - 1; i > 0; i--)
        {
            int j = UnityEngine.Random.Range(0, i + 1);
            var tmp = copy[i];
            copy[i] = copy[j];
            copy[j] = tmp;
        }

        if (copy.Count <= n) return copy;

        return copy.GetRange(0, n);
    }

    /// <summary>由 UI / 调试界面调用：给当前玩家选中第 index 个选项</summary>
    public void Select(int optionIndex)
    {
        if (!_active || _index < 0 || _index >= _queue.Count) return;
        if (optionIndex < 0 || optionIndex >= _options.Count) return;

        var player = _queue[_index];
        var src = _options[optionIndex];
        if (player == null || src == null) return;

        // 拷贝一份：选项对象来自配置资产，不能直接塞进玩家的强化列表
        // （用 Clone 而不是手写字段，否则新增字段时容易漏拷，行为型强化会整个丢掉）
        var copy = src.Clone();
        player.upgrades.Add(copy);

        // 基于基准值重算，所以重复调用不会叠加
        UpgradeSystem.ApplyAll(player.pen, player.upgrades);

        EventBus.Instance.Publish(new UpgradeChosenEvent
        {
            playerId = player.playerId,
            upgradeName = copy.DisplayName
        });

        Debug.Log($"[强化] {player.playerName} 选择了 {copy.DisplayName}：{copy.Describe()}");

        _remaining--;
        if (_remaining > 0)
        {
            RollOptions();   // 同一个人多次选择时重新抽
            return;
        }

        NextPlayer();
    }

    private void Finish()
    {
        _active = false;
        _options.Clear();

        EventBus.Instance.Publish(new UpgradePhaseEndEvent
        {
            roundNumber = GameManager.Instance != null ? GameManager.Instance.RoundNumber : 0,
            beforeMatch = _beforeMatch
        });

        Debug.Log("[强化] 强化阶段结束");

        // 联机：收起各端的强化面板
        if (NetworkUpgradeBridge.Instance != null)
            NetworkUpgradeBridge.Instance.HostHideAll();

        var cb = _onComplete;
        _onComplete = null;
        cb?.Invoke();
    }

    // ===== 简易选择界面（阶段 6 换成正式 UI 后可关掉）=====

    private void OnGUI()
    {
        // 联机（场景里有 NetworkManager）时不在这里画 —— UI 交给 NetworkUpgradeBridge，
        // 它只给"轮到的那名玩家"显示，避免房主上看见所有人的选项
        if (Unity.Netcode.NetworkManager.Singleton != null) return;

        if (!showDebugUI || !_active) return;
        if (_index < 0 || _index >= _queue.Count) return;

        var player = _queue[_index];
        float h = 78f + _options.Count * 32f;

        NetUi.Begin();
        GUILayout.BeginArea(new Rect(20f, 20f, 360f, h), GUI.skin.box);
        GUILayout.Label($"强化选择：{player.playerName}（第 {_index + 1}/{_queue.Count} 位，还需 {_remaining} 个）");
        GUILayout.Label($"笔：{player.penConfig?.displayName}   专属 {player.penConfig?.exclusiveUpgrades?.Count ?? 0} 条");

        for (int i = 0; i < _options.Count; i++)
        {
            string label = _options[i] != null
                ? $"{_options[i].DisplayName}  |  {_options[i].Describe()}"
                : "(空条目)";

            if (GUILayout.Button(label, GUILayout.Height(26f)))
            {
                Select(i);
                break;   // 选中后立即刷新，避免同帧重复点击
            }
        }

        GUILayout.EndArea();
        NetUi.End();
    }
}
