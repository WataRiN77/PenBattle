using System.Linq;
using System.Net;
using System.Net.Sockets;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using UnityEngine;

/// <summary>
/// 联机入口（局域网 / 直连 IP）。
///
/// 【怎么用】一台点「作为 Host 开始」，另一台把「目标地址」改成 Host 面板上显示的
/// 局域网 IP，再点「作为 Client 连接」。
/// 端口默认 7777，Host 监听 0.0.0.0（局域网内其他机器才能连进来）。
///
/// 【场景准备】场景里需要一个 NetworkManager 物体：
///   1. 右键 Create Empty → 命名 NetworkManager
///   2. Add Component → NetworkManager
///   3. Add Component → Unity Transport（IP 和端口留给本脚本控制）
///   然后把这个脚本挂到任意物体上（比如 GAME_MANAGER），运行后左上角会出现面板。
///
/// 【关于互联网房间】原计划的「房间号 + 密码」走 Unity Relay，但 Unity 自 2026-06-30 起
/// 停止向中国大陆/港澳组织提供 Relay / Lobby 等全球 Gaming Services，请求返回 HTTP 451。
/// 所以这条路暂时不通，先只做局域网。将来要接互联网联机，见 Docs/联机改造方案.md §2.4。
/// </summary>
public class NetworkLauncher : MonoBehaviour
{
    [Header("连接")]
    [Tooltip("目标地址：Client 填 Host 的局域网 IP；Host 一般保持 127.0.0.1")]
    public string address = "127.0.0.1";
    public ushort port = 7777;

    [Header("界面")]
    public bool showUI = true;

    private string _status = "未启动";
    private string _localIp = "-";

    private static NetworkManager NM => NetworkManager.Singleton;

    private void Awake()
    {
        _localIp = GetLocalIPv4();
    }

    private void Start()
    {
        // ⚠ 这里的检查不能放 Awake：NetworkManager.Singleton 由 NGO 自己的 Awake 赋值，
        // 而 Unity 不保证不同物体之间的 Awake 执行顺序（本脚本可能先跑，此时 Singleton 还是 null）。
        // 放到 Start 就一定安全：Start 在所有 Awake 之后执行。
        if (NM == null)
        {
            Debug.LogError("[联机] 场景里没有可用的 NetworkManager（可能没建，或被禁用了）。" +
                           "请新建空物体 → Add Component → NetworkManager → Add Component → Unity Transport。");
            return;
        }

        Subscribe();
    }

    private void OnDestroy() => Unsubscribe();

    // ---------------- 事件订阅 ----------------

    private void Subscribe()
    {
        if (NM == null) return;
        NM.OnServerStarted += OnServerStarted;
        NM.OnClientConnectedCallback += OnClientConnected;
        NM.OnClientDisconnectCallback += OnClientDisconnected;
    }

    private void Unsubscribe()
    {
        if (NM == null) return;
        NM.OnServerStarted -= OnServerStarted;
        NM.OnClientConnectedCallback -= OnClientConnected;
        NM.OnClientDisconnectCallback -= OnClientDisconnected;
    }

    private void OnServerStarted()
    {
        _status = "Host 运行中";
        Debug.Log($"[联机] Host 已启动。局域网 IP = {_localIp}，端口 {port}（把这个 IP 告诉别人来连）");
    }

    private void OnClientConnected(ulong clientId)
    {
        if (NM == null) return;

        if (NM.IsServer)
            Debug.Log($"[联机] 客户端接入：clientId={clientId}，当前连接数 {NM.ConnectedClientsIds.Count}");
        else
        {
            _status = "已连上主机";
            Debug.Log($"[联机] 已连上主机！本机 clientId={clientId}");
        }
    }

    private void OnClientDisconnected(ulong clientId)
    {
        if (NM == null) return;

        // 主机侧：只是某个客户端走了
        if (NM.IsServer && !NM.IsClient)
        {
            Debug.Log($"[联机] 客户端断开：clientId={clientId}，剩余 {Mathf.Max(0, NM.ConnectedClientsIds.Count - 1)} 人");
            return;
        }

        // 客户端侧：连不上/被踢时 NGO 会给原因
        string reason = NM.DisconnectReason;
        _status = string.IsNullOrEmpty(reason) ? "已断开" : $"连接失败：{reason}";
        Debug.Log($"[联机] 本端断开：clientId={clientId}，原因={(string.IsNullOrEmpty(reason) ? "无" : reason)}");
    }

    // ---------------- 操作 ----------------

    public void StartHost()
    {
        if (!Ready()) return;

        // 监听 0.0.0.0，局域网内其他机器才能连进来
        Transport().SetConnectionData("127.0.0.1", port, "0.0.0.0");

        if (NM.StartHost())
        {
            _status = "Host 运行中";
            Debug.Log($"[联机] StartHost 成功。局域网 IP = {_localIp}，端口 {port}");
        }
        else
        {
            _status = "Host 启动失败";
            Debug.LogError("[联机] StartHost 失败（多半是端口被占用）。");
        }
    }

    public void StartClient()
    {
        if (!Ready()) return;

        Transport().SetConnectionData(address, port);

        if (NM.StartClient())
        {
            _status = $"正在连接 {address}:{port} …";
            Debug.Log($"[联机] StartClient → {address}:{port}");
        }
        else
        {
            _status = "Client 启动失败";
            Debug.LogError("[联机] StartClient 失败。");
        }
    }

    public void Shutdown()
    {
        if (NM == null) return;
        if (NM.IsClient || NM.IsServer)
        {
            NM.Shutdown();
            _status = "已断开";
            Debug.Log("[联机] 已 Shutdown。");
        }
    }

    private bool Ready()
    {
        if (NM == null || Transport() == null)
        {
            _status = "NetworkManager/UnityTransport 未就绪";
            Debug.LogError("[联机] NetworkManager 或 UnityTransport 未就绪，无法启动。");
            return false;
        }
        return true;
    }

    private UnityTransport Transport() => NM != null ? NM.GetComponent<UnityTransport>() : null;

    private static string GetLocalIPv4()
    {
        try
        {
            return Dns.GetHostEntry(Dns.GetHostName())
                      .AddressList
                      .FirstOrDefault(a => a.AddressFamily == AddressFamily.InterNetwork)
                      ?.ToString() ?? "-";
        }
        catch { return "-"; }
    }

    // ---------------- 给正式 UI 读的只读接口 ----------------

    /// <summary>当前状态文字（未启动 / Host 运行中 / 正在连接… / 已连上主机 / 连接失败：…）</summary>
    public string Status => _status;

    /// <summary>本机局域网 IP（Awake 时取一次）</summary>
    public string LocalIp => _localIp;

    /// <summary>是否已经在跑（Host 或 Client）</summary>
    public bool IsRunning
    {
        get
        {
            var nm = NetworkManager.Singleton;
            return nm != null && (nm.IsClient || nm.IsServer);
        }
    }

    /// <summary>
    /// 对局是否已经开始。大厅/连接类界面用它决定该不该收起自己。
    ///
    /// 判据优先用 `NetworkMatchFlow.MatchStarted` —— 它只取决于「开始游戏」那一帧，
    /// 房主写下、客户端由 RPC 收到，两端一致，不受各自对局状态影响；
    /// 没有 flow（单机直接调 BeginMatch 测试）时，退回看 GameManager 的对局状态。
    /// </summary>
    public static bool IsInMatch()
    {
        var flow = NetworkMatchFlow.Instance;
        if (flow != null && flow.MatchStarted) return true;

        var gm = GameManager.Instance;
        return gm != null && (gm.RoundNumber > 0 || gm.Phase != GameManager.TurnPhase.Idle);
    }

    /// <summary>连接详情（Host 显示连接数，Client 显示 clientId）—— 没连上时为空</summary>
    public string ConnectionInfo
    {
        get
        {
            var nm = NetworkManager.Singleton;
            if (nm == null || !(nm.IsClient || nm.IsServer)) return "";

            return nm.IsHost
                ? $"Host：连接数 {nm.ConnectedClientsIds.Count}"
                : $"Client：clientId={nm.LocalClientId}，已连接={nm.IsConnectedClient}";
        }
    }

    // ---------------- 简易面板（正式 UI 接好之前先用这个） ----------------

    private void OnGUI()
    {
        if (!showUI) return;

        // 进了对局，这块联机面板就没用了，还挡视野 —— 直接收起来。
        // （正式 UI 走 UIManager.EnterMatch；这里是 IMGUI 兜底，两边判据同一套。）
        if (IsInMatch()) return;

        bool running = NM != null && (NM.IsClient || NM.IsServer);

        NetUi.Begin();
        GUILayout.BeginArea(new Rect(20f, 20f, 380f, 300f), GUI.skin.box);

        GUILayout.Label("联机（局域网）");
        GUILayout.Label($"本机局域网 IP：{_localIp}");
        GUILayout.Label($"状态：{_status}");

        GUILayout.Space(6f);

        GUILayout.BeginHorizontal();
        GUILayout.Label("目标地址", GUILayout.Width(64f));
        address = GUILayout.TextField(address ?? "", GUILayout.Width(160f));
        GUILayout.EndHorizontal();

        GUILayout.BeginHorizontal();
        GUILayout.Label("端口", GUILayout.Width(64f));
        string portText = GUILayout.TextField(port.ToString(), GUILayout.Width(160f));
        if (ushort.TryParse(portText, out ushort parsedPort)) port = parsedPort;
        GUILayout.EndHorizontal();

        GUILayout.Space(8f);

        GUI.enabled = !running;
        if (GUILayout.Button("作为 Host 开始", GUILayout.Height(28f))) StartHost();
        if (GUILayout.Button("作为 Client 连接", GUILayout.Height(28f))) StartClient();

        GUI.enabled = running;
        if (GUILayout.Button("断开", GUILayout.Height(24f))) Shutdown();
        GUI.enabled = true;

        if (running && NM != null)
        {
            GUILayout.Space(4f);
            GUILayout.Label(NM.IsHost
                ? $"Host：连接数 {NM.ConnectedClientsIds.Count}"
                : $"Client：clientId={NM.LocalClientId}，已连接={NM.IsConnectedClient}");
        }

        GUILayout.EndArea();
        NetUi.End();
    }
}
