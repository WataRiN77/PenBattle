using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

/// 联机「强化选择」的桥接（房主权威）。
///
/// 流程：
///   房主算出选项（UpgradePhaseManager.RollOptions）
///     → [ClientRpc] ShowOptionsClientRpc 广播「轮到谁 + 选项是什么」
///     → 只有该玩家自己的客户端会把按钮显示出来（其余端收到但 UI 直接忽略）
///     → 玩家点击 → [ServerRpc] ChooseServerRpc
///     → 房主校验「你确实是该玩家的所有者」→ UpgradePhaseManager.Select(index)
///
/// 【场景准备】挂在和 NetworkMatchFlow 同一个物体上即可（那边已经有 NetworkObject）。
[RequireComponent(typeof(NetworkObject))]
public class NetworkUpgradeBridge : NetworkBehaviour
{
    public static NetworkUpgradeBridge Instance { get; private set; }

    // 各端本地：当前展示的选项（由房主广播而来）
    private int _shownPlayerIndex = -1;
    private int _remaining;
    private string[] _optionNames = new string[0];
    private string[] _optionDescs = new string[0];
    private bool _submitted;

    private static NetworkManager NM => NetworkManager.Singleton;

    public override void OnNetworkSpawn() => Instance = this;
    public override void OnNetworkDespawn() { if (Instance == this) Instance = null; }

    // ================= 房主 → 各端 =================

    private const char OptionSeparator = '\n';   // 选项之间
    private const char FieldSeparator = '\t';    // 名称与描述之间

    /// <summary>房主侧调用：广播「轮到 playerIndex 选，选项是这些」</summary>
    public void HostShowOptions(int playerIndex, int remaining, List<UpgradeEntry> options)
    {
        if (!IsServer) return;

        // ⚠ NGO 的 RPC **不支持 string[]**（ILPP 会直接报 "Don't know how to serialize System.String[]"）。
        // 所以这里打包成单个 string 再传：选项之间用 '\n'，名称/描述之间用 '\t'。
        var sb = new System.Text.StringBuilder();
        for (int i = 0; i < options.Count; i++)
        {
            if (i > 0) sb.Append(OptionSeparator);

            string name = options[i] != null ? options[i].DisplayName : "(空选项)";
            string desc = options[i] != null ? options[i].Describe() : "";

            // 把可能出现在文案里的分隔符替换掉，避免解析错位
            sb.Append(name.Replace(OptionSeparator, ' ').Replace(FieldSeparator, ' '));
            sb.Append(FieldSeparator);
            sb.Append(desc.Replace(OptionSeparator, ' ').Replace(FieldSeparator, ' '));
        }

        ShowOptionsClientRpc(playerIndex, remaining, sb.ToString());
    }

    /// <summary>房主侧调用：强化阶段整体结束，收起所有端的面板</summary>
    public void HostHideAll()
    {
        if (IsServer) HideClientRpc();
    }

    [ClientRpc]
    private void ShowOptionsClientRpc(int playerIndex, int remaining, string packed)
    {
        _shownPlayerIndex = playerIndex;
        _remaining = remaining;
        ParseOptions(packed);
        _submitted = false;
    }

    /// <summary>把房主打包过来的选项字符串还原成 名称/描述 两组</summary>
    private void ParseOptions(string packed)
    {
        if (string.IsNullOrEmpty(packed))
        {
            _optionNames = new string[0];
            _optionDescs = new string[0];
            return;
        }

        var lines = packed.Split(OptionSeparator);
        _optionNames = new string[lines.Length];
        _optionDescs = new string[lines.Length];

        for (int i = 0; i < lines.Length; i++)
        {
            var parts = lines[i].Split(FieldSeparator);
            _optionNames[i] = parts.Length > 0 ? parts[0] : "";
            _optionDescs[i] = parts.Length > 1 ? parts[1] : "";
        }
    }

    [ClientRpc]
    private void HideClientRpc()
    {
        _shownPlayerIndex = -1;
        _optionNames = new string[0];
        _optionDescs = new string[0];
    }

    // ================= 客户端 → 房主 =================

    [ServerRpc(RequireOwnership = false)]
    private void ChooseServerRpc(int optionIndex, ServerRpcParams p = default)
    {
        var mgr = UpgradePhaseManager.Instance;     // 只有房主会走到这里
        if (mgr == null) return;

        var chooser = mgr.CurrentPlayer;
        if (chooser == null) return;

        if (!IsAllowedChooser(chooser.playerIndex, p.Receive.SenderClientId))
        {
            Debug.LogWarning($"[联机] 拒绝越权强化选择：clientId={p.Receive.SenderClientId} " +
                             $"不是 Player{chooser.playerIndex + 1} 的所有者");
            return;
        }

        mgr.Select(optionIndex);
    }

    private static bool IsAllowedChooser(int playerIndex, ulong senderClientId)
    {
        var flow = NetworkMatchFlow.Instance;
        if (flow == null) return true;                                   // 没有席位信息 → 放行（单机兜底）
        if (!flow.TryGetClientId(playerIndex, out ulong owner)) return true;  // 查不到 → 放行
        return owner == senderClientId;
    }

    // ================= 面板（只有本人看得到） =================

    private void OnGUI()
    {
        if (_shownPlayerIndex < 0) return;

        var flow = NetworkMatchFlow.Instance;
        int me = flow != null ? flow.MyPlayerIndex : -1;

        // 关键：不是轮到我 → 直接不画（这就是"各端各自选"的实现方式）
        if (me != _shownPlayerIndex) return;

        float h = 66f + _optionNames.Length * 32f;
        NetUi.Begin();
        // 放在选笔面板下方（y=340 逻辑像素），避开左上角的联机面板
        GUILayout.BeginArea(new Rect(NetUi.LogicalWidth * 0.5f - 220f, 340f, 440f, h), GUI.skin.box);

        GUILayout.Label($"强化选择（还要选 {_remaining} 个）");

        GUI.enabled = !_submitted;
        for (int i = 0; i < _optionNames.Length; i++)
        {
            if (GUILayout.Button($"{_optionNames[i]}　|　{_optionDescs[i]}", GUILayout.Height(28f)))
            {
                _submitted = true;
                ChooseServerRpc(i);
                break;   // 同帧避免重复点击
            }
        }
        GUI.enabled = true;

        if (_submitted) GUILayout.Label("已提交，等待其他玩家…");

        GUILayout.EndArea();
        NetUi.End();
    }
}
