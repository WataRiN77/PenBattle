using UnityEngine;

/// UI 总控：注册所有面板，提供统一的打开/关闭入口。
/// 挂在 `UIRoot` 上（和 Canvas 同一个物体）。
///
/// 规则：
///   · `Connect` / `PenSelect` / `Upgrade` / `Result` 是**模态**的（同一时刻只显示一个）；
///   · `Hud` 是**常驻**的，不参与互斥（对局中一直在）。
public class UIManager : MonoBehaviour
{
	public static UIManager Instance { get; private set; }

	public enum PanelId { Connect, PenSelect, Upgrade, Hud, Result }

	[Header("面板引用（在 Inspector 里拖，或用菜单一键生成）")]
	[SerializeField] private UIPanel connectPanel;
	[SerializeField] private UIPanel penSelectPanel;
	[SerializeField] private UIPanel upgradePanel;
	[SerializeField] private UIPanel hudPanel;
	[SerializeField] private UIPanel resultPanel;

	private UIPanel _currentModal;

	private void Awake()
	{
		Instance = this;
	}

	private void OnDestroy()
	{
		if (Instance == this) Instance = null;
	}

	public UIPanel Get(PanelId id)
	{
		switch (id)
		{
			case PanelId.Connect: return connectPanel;
			case PanelId.PenSelect: return penSelectPanel;
			case PanelId.Upgrade: return upgradePanel;
			case PanelId.Hud: return hudPanel;
			case PanelId.Result: return resultPanel;
			default: return null;
		}
	}

	/// <summary>显示一个面板（模态面板会自动关掉其它模态）</summary>
	public void Show(PanelId id)
	{
		var p = Get(id);
		if (p == null)
		{
			Debug.LogWarning($"[UI] 面板 {id} 没有在 UIManager 上绑定引用。");
			return;
		}

		if (id == PanelId.Hud)
		{
			p.Show();          // HUD 常驻，不和别人抢
			return;
		}

		if (_currentModal != null && _currentModal != p) _currentModal.Hide();
		_currentModal = p;
		p.Show();
	}

	public void Hide(PanelId id)
	{
		var p = Get(id);
		if (p == null) return;

		if (p == _currentModal) _currentModal = null;
		p.Hide();
	}

	/// <summary>关掉当前所有模态面板（HUD 保留）</summary>
	public void HideAllModals()
	{
		if (_currentModal == null) return;

		_currentModal.Hide();
		_currentModal = null;
	}

	// ================= 常用组合（流程里直接调，省得记状态）=================

	public void EnterLobby()        // 刚进游戏 / 断线后：只显示连接面板
	{
		Show(PanelId.Connect);
		Hide(PanelId.Hud);
	}

	public void EnterPenSelect()    // 开始游戏 → 选笔
	{
		Show(PanelId.PenSelect);
	}

	public void EnterUpgrade()      // 强化阶段
	{
		Show(PanelId.Upgrade);
	}

	public void EnterMatch()        // 正式开打：关掉所有模态，只留 HUD
	{
		HideAllModals();
		Show(PanelId.Hud);
	}

	public void EnterResult()       // 结算
	{
		Show(PanelId.Result);
	}
}
