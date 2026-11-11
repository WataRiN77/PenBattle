using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// `ConnectPanel` 的绑定脚本 —— 挂在 ConnectPanel 根物体上，
/// 把面板里的控件拖进下面这些槽位。**没拖的槽位会跳过，不会报错**（方便你分步做）。
///
/// 它负责：
///   · 把「本机 IP / 状态 / 连接信息」显示到文字上；
///   · Host / Client / 断开 三个按钮接到 `NetworkLauncher`；
///   · 把输入框里的地址、端口写回 launcher；
///   · 接管后自动关掉旧的 IMGUI 面板（`hideLegacyImGui`）。
///
/// 状态刷新用 0.2s 轮询而不是事件 —— 状态变化本来就很低频，轮询最省事、也不用改 NetworkLauncher 的架构。
public class ConnectPanelUI : MonoBehaviour
{
	[Header("逻辑引用")]
	[Tooltip("场景里的 NetworkLauncher（一般在 GAME_MANAGER 上）；留空会自动找")]
	[SerializeField] private NetworkLauncher launcher;

	[Header("文字")]
	[Tooltip("显示本机局域网 IP 的 TMP 文本")]
	[SerializeField] private TMP_Text localIpText;

	[Tooltip("状态文字：未启动 / Host 运行中 / 正在连接… / 已连上主机")]
	[SerializeField] private TMP_Text statusText;

	[Tooltip("连接详情（Host 连接数 / Client clientId）—— 可选")]
	[SerializeField] private TMP_Text connectionInfoText;

	[Header("输入框")]
	[Tooltip("目标地址输入框（TMP_InputField）")]
	[SerializeField] private TMP_InputField addressInput;

	[Tooltip("端口输入框（可选，留空就用 launcher 上配的端口）")]
	[SerializeField] private TMP_InputField portInput;

	[Header("按钮")]
	[SerializeField] private Button hostButton;
	[SerializeField] private Button clientButton;
	[SerializeField] private Button disconnectButton;

	[Header("行为")]
	[Tooltip("接管后关掉旧的 IMGUI 面板")]
	[SerializeField] private bool hideLegacyImGui = true;

	[Tooltip("状态刷新间隔（秒）")]
	[SerializeField] private float refreshInterval = 0.2f;

	private float _timer;
	private bool _hiddenForMatch;

	private void Awake()
	{
		if (launcher == null) launcher = FindObjectOfType<NetworkLauncher>();

		if (hideLegacyImGui && launcher != null) launcher.showUI = false;

		// 按钮：先清再绑，避免重复挂
		Bind(hostButton, OnHost);
		Bind(clientButton, OnClient);
		Bind(disconnectButton, OnDisconnect);

		// 输入框初值 = launcher 当前配置
		if (launcher != null)
		{
			//if (addressInput != null) addressInput.text = launcher.address;
			if (portInput != null) portInput.text = launcher.port.ToString();
		}
	}

	private void Start()
	{
		// 进游戏先显示连接面板
		if (UIManager.Instance != null) UIManager.Instance.EnterLobby();

		Refresh();
	}

	private void Update()
	{
		_timer -= Time.unscaledDeltaTime;
		if (_timer > 0f) return;

		_timer = refreshInterval;

		// 对局开始了 → 收起连接面板，只做一次。
		// 先让 UIManager 走一遍 EnterMatch（收模态 + 显示 HUD）；
		// 然后**无条件**把自己关掉 —— 万一 UIManager 上没绑 connectPanel 引用，
		// EnterMatch 是收不掉这个面板的，不能指望它。
		if (!_hiddenForMatch && NetworkLauncher.IsInMatch())
		{
			_hiddenForMatch = true;

			if (UIManager.Instance != null) UIManager.Instance.EnterMatch();
			gameObject.SetActive(false);
			return;
		}

		Refresh();
	}

	// ================= 按钮 =================

	private void OnHost()
	{
		if (launcher == null) return;

		ApplyInputs();
		launcher.StartHost();
		Refresh();
	}

	private void OnClient()
	{
		if (launcher == null) return;

		ApplyInputs();
		launcher.StartClient();
		Refresh();
	}

	private void OnDisconnect()
	{
		if (launcher == null) return;

		launcher.Shutdown();
		Refresh();
	}

	/// 把输入框的内容写回 launcher（Host 不用填地址，所以地址为空时保持原值）
	private void ApplyInputs()
	{
		if (addressInput != null && !string.IsNullOrWhiteSpace(addressInput.text))
			launcher.address = addressInput.text.Trim();

		if (portInput != null && ushort.TryParse(portInput.text, out ushort p) && p > 0)
			launcher.port = p;
	}

	// ================= 刷新显示 =================

	private void Refresh()
	{
		if (launcher == null) return;

		if (localIpText != null) localIpText.text = $"本机 IP：{launcher.LocalIp}";
		if (statusText != null) statusText.text = launcher.Status;

		bool running = launcher.IsRunning;

		// 已在跑就不能再点 Host/Client；没跑就不能点断开
		if (hostButton != null) hostButton.interactable = !running;
		if (clientButton != null) clientButton.interactable = !running;
		if (disconnectButton != null) disconnectButton.interactable = running;

		if (connectionInfoText != null)
			connectionInfoText.text = running ? launcher.ConnectionInfo : "";
	}

	private static void Bind(Button b, UnityEngine.Events.UnityAction action)
	{
		if (b == null) return;

		b.onClick.RemoveAllListeners();
		b.onClick.AddListener(action);
	}
}
