#if UNITY_EDITOR
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// 一键搭建 UI 框架（UGUI）。
/// 菜单：Tools → PenBattle → 搭建 UI 框架
///
/// 生成的结构：
///   UIRoot (Canvas + CanvasScaler + GraphicRaycaster + UIManager)
///   ├── ConnectPanel    连接（模态）
///   ├── PenSelectPanel  选笔（模态）
///   ├── UpgradePanel    强化（模态）
///   ├── MatchHud        对局 HUD（常驻，左上角）
///   └── ResultPanel     结算（模态）
///
/// 每个面板内部只放三样东西：`BG`（背景图）、`Title`（标题文字）、`Content`（内容区，空的）。
/// **美术随便换**：换 Sprite、改布局、往 Content 里塞你自己的东西都行，
/// 只要别删掉面板根物体上的 `CanvasGroup` + `UIPanel`。
public static class UIFrameworkBuilder
{
	private static readonly Vector2 ReferenceResolution = new Vector2(1920f, 1080f);

	[MenuItem("Tools/PenBattle/搭建 UI 框架")]
	public static void Build()
	{
		// ---------- 0. 清掉旧的 ----------
		var old = GameObject.Find("UIRoot");
		if (old != null)
		{
			if (!EditorUtility.DisplayDialog("搭建 UI 框架",
				"场景里已经有一个 UIRoot，要删掉重建吗？（它会清掉你在里面做的改动）", "重建", "取消"))
				return;

			Object.DestroyImmediate(old);
		}

		// ---------- 1. EventSystem ----------
		if (Object.FindObjectOfType<EventSystem>() == null)
		{
			new GameObject("EventSystem", typeof(EventSystem), typeof(StandaloneInputModule));
		}

		// ---------- 2. Canvas ----------
		var root = new GameObject("UIRoot",
			typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster), typeof(UIManager));

		var canvas = root.GetComponent<Canvas>();
		canvas.renderMode = RenderMode.ScreenSpaceOverlay;

		var scaler = root.GetComponent<CanvasScaler>();
		scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
		scaler.referenceResolution = ReferenceResolution;
		scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
		scaler.matchWidthOrHeight = 0.5f;          // 宽高各占一半，横竖变化都不至于太离谱

		// ---------- 3. 面板 ----------
		var connect = MakeModalPanel(root.transform, "ConnectPanel", "连接");
		var penSelect = MakeModalPanel(root.transform, "PenSelectPanel", "选择你的笔");
		var upgrade = MakeModalPanel(root.transform, "UpgradePanel", "强化");
		var result = MakeModalPanel(root.transform, "ResultPanel", "结算");
		var hud = MakeHudPanel(root.transform, "MatchHud");

		// ---------- 4. 面板引用填进 UIManager ----------
		var manager = root.GetComponent<UIManager>();
		var so = new SerializedObject(manager);
		so.FindProperty("connectPanel").objectReferenceValue = connect;
		so.FindProperty("penSelectPanel").objectReferenceValue = penSelect;
		so.FindProperty("upgradePanel").objectReferenceValue = upgrade;
		so.FindProperty("hudPanel").objectReferenceValue = hud;
		so.FindProperty("resultPanel").objectReferenceValue = result;
		so.ApplyModifiedPropertiesWithoutUndo();

		Undo.RegisterCreatedObjectUndo(root, "Build UI Framework");
		Selection.activeGameObject = root;

		Debug.Log("[UI] 框架已生成：UIRoot → 5 个面板。\n" +
				  "· 每个面板里有 BG / Title / Content，直接在 Scene 里改、往 Content 里塞自定义内容\n" +
				  "· 面板根物体上的 CanvasGroup + UIPanel 别删（显示逻辑靠它们）\n" +
				  "· 文字如果显示不出来：Window → TextMeshPro → Import TMP Essential Resources");
	}

	// ================= 面板构建 =================

	/// 全屏模态面板：暗底 + 居中标题 + 内容区
	private static UIPanel MakeModalPanel(Transform parent, string name, string title)
	{
		var go = new GameObject(name, typeof(RectTransform), typeof(CanvasGroup), typeof(Image), typeof(UIPanel));
		go.transform.SetParent(parent, false);

		var rt = go.GetComponent<RectTransform>();
		Stretch(rt);

		var bg = go.GetComponent<Image>();
		bg.color = new Color(0.05f, 0.06f, 0.09f, 0.72f);   // 占位暗底，换成你的图
		bg.raycastTarget = true;

		MakeTitle(go.transform, title, new Vector2(0f, 340f), 64f);
		MakeContentRoot(go.transform, new Vector2(1600f, 760f));

		return go.GetComponent<UIPanel>();
	}

	/// 常驻 HUD：左上角一条，不挡操作
	private static UIPanel MakeHudPanel(Transform parent, string name)
	{
		var go = new GameObject(name, typeof(RectTransform), typeof(CanvasGroup), typeof(UIPanel));
		go.transform.SetParent(parent, false);

		var rt = go.GetComponent<RectTransform>();
		rt.anchorMin = new Vector2(0f, 1f);
		rt.anchorMax = new Vector2(0f, 1f);
		rt.pivot = new Vector2(0f, 1f);
		rt.anchoredPosition = new Vector2(24f, -24f);
		rt.sizeDelta = new Vector2(520f, 160f);

		// HUD 不挡射线（血条/笔还要能点）
		go.GetComponent<CanvasGroup>().blocksRaycasts = false;

		var bg = go.AddComponent<Image>();
		bg.color = new Color(0f, 0f, 0f, 0.45f);
		bg.raycastTarget = false;

		MakeTitle(go.transform, "对局信息", new Vector2(0f, 44f), 34f, TextAlignmentOptions.Left);
		MakeContentRoot(go.transform, new Vector2(470f, 110f), new Vector2(0f, -18f));

		return go.GetComponent<UIPanel>();
	}

	private static void MakeTitle(Transform parent, string text, Vector2 anchoredPos,
								  float fontSize, TextAlignmentOptions align = TextAlignmentOptions.Center)
	{
		var go = new GameObject("Title", typeof(RectTransform));
		go.transform.SetParent(parent, false);

		var rt = go.GetComponent<RectTransform>();
		rt.anchorMin = new Vector2(0.5f, 0.5f);
		rt.anchorMax = new Vector2(0.5f, 0.5f);
		rt.anchoredPosition = anchoredPos;
		rt.sizeDelta = new Vector2(1400f, 120f);

		var tmp = go.AddComponent<TextMeshProUGUI>();
		tmp.text = text;
		tmp.fontSize = fontSize;
		tmp.alignment = align;
		tmp.color = Color.white;
		tmp.raycastTarget = false;
	}

	/// 内容区：留给美术/策划往里放卡片、按钮、列表
	private static void MakeContentRoot(Transform parent, Vector2 size, Vector2? anchoredPos = null)
	{
		var go = new GameObject("Content", typeof(RectTransform));
		go.transform.SetParent(parent, false);

		var rt = go.GetComponent<RectTransform>();
		rt.anchorMin = new Vector2(0.5f, 0.5f);
		rt.anchorMax = new Vector2(0.5f, 0.5f);
		rt.anchoredPosition = anchoredPos ?? Vector2.zero;
		rt.sizeDelta = size;
	}

	private static void Stretch(RectTransform rt)
	{
		rt.anchorMin = Vector2.zero;
		rt.anchorMax = Vector2.one;
		rt.offsetMin = Vector2.zero;
		rt.offsetMax = Vector2.zero;
	}
}
#endif
