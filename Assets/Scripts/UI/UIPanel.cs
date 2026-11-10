using UnityEngine;

/// 所有界面的基类：统一的显示 / 隐藏 + 淡入淡出 + 交互开关。
///
/// 这个类**只管"显不显示、能不能点"**，完全不碰外观 ——
/// 美术想怎么换图、换布局都行，换完只要保证物体上有 CanvasGroup + 这个组件即可。
///
/// 用法：
///   panel.Show();  /  panel.Hide();
///   需要瞬间切换（比如开局）用 ShowImmediate() / HideImmediate()。
[RequireComponent(typeof(CanvasGroup))]
public class UIPanel : MonoBehaviour
{
	[Header("淡入淡出（0 = 瞬间切换）")]
	[SerializeField] private float fadeDuration = 0.15f;

	[Header("初始状态")]
	[SerializeField] private bool startHidden = true;

	private CanvasGroup _group;
	private float _targetAlpha;

	public bool IsVisible { get; private set; }

	protected virtual void Awake()
	{
		_group = GetComponent<CanvasGroup>();

		if (startHidden) HideImmediate();
		else ShowImmediate();
	}

	private void Update()
	{
		if (_group == null) return;

		if (fadeDuration <= 0f)
		{
			_group.alpha = _targetAlpha;
		}
		else
		{
			// 用 unscaledDeltaTime：即使游戏里做了慢动作/顿帧（淘汰那套），UI 淡入淡出也不受影响
			_group.alpha = Mathf.MoveTowards(_group.alpha, _targetAlpha,
											 Time.unscaledDeltaTime / fadeDuration);
		}

		// 只有完全显示出来才允许交互，避免淡出过程中还能点到
		bool interactive = IsVisible && _group.alpha > 0.99f;
		_group.interactable = interactive;
		_group.blocksRaycasts = interactive;
	}

	// ================= 对外接口 =================

	public virtual void Show()
	{
		if (_group == null) _group = GetComponent<CanvasGroup>();

		IsVisible = true;
		_targetAlpha = 1f;

		// 注意：**不要** SetActive(false) —— 那会让 Awake 不再执行、
		// 也会让协程/事件订阅失效。用 CanvasGroup.alpha = 0 来"隐身"更安全。
		gameObject.SetActive(true);
	}

	public virtual void Hide()
	{
		IsVisible = false;
		_targetAlpha = 0f;
	}

	public void ShowImmediate()
	{
		if (_group == null) _group = GetComponent<CanvasGroup>();

		IsVisible = true;
		_targetAlpha = 1f;
		_group.alpha = 1f;
		gameObject.SetActive(true);
	}

	public void HideImmediate()
	{
		if (_group == null) _group = GetComponent<CanvasGroup>();

		IsVisible = false;
		_targetAlpha = 0f;
		_group.alpha = 0f;
		_group.interactable = false;
		_group.blocksRaycasts = false;
	}
}
