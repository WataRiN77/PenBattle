#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

/// 一键把血条**做进笔预制体**（而不是运行时生成）。
/// 菜单：Tools → PenBattle → 给笔预制体加血条
///
/// 做四件事：
///   1. 在预制体根下建 `HealthBar` / `BG` / `Fill` 三层结构，尺寸按笔的渲染包围盒自动估算
///   2. 创建两个材质资产（`Assets/Resources/Pens/Materials/`，用项目一贯的 URP 兜底 shader）
///   3. 给预制体根挂上 `PenHealthBar` 并填好引用
///   4. 存回预制体
///
/// 跑完可以直接在 Scene 里打开预制体，拖 `HealthBar` 调位置/大小、改材质颜色。
public static class PenHealthBarPrefabSetup
{
	private const string PrefabPath = "Assets/Resources/Pens/Prefab/笔_1.prefab";
	private const string MaterialDir = "Assets/Resources/Pens/Materials";

	[MenuItem("Tools/PenBattle/给笔预制体加血条")]
	public static void Build()
	{
		if (AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath) == null)
		{
			Debug.LogError($"[血条] 找不到预制体：{PrefabPath}");
			return;
		}

		var root = PrefabUtility.LoadPrefabContents(PrefabPath);
		try
		{
			// 0) 清掉旧的（重复跑菜单不会叠加）
			var old = root.transform.Find("HealthBar");
			if (old != null) Object.DestroyImmediate(old.gameObject);

			// 1) 按笔的包围盒估尺寸
			var r = root.GetComponentInChildren<Renderer>();
			float h = (r != null) ? Mathf.Max(r.bounds.size.y, 0.01f) : 1f;
			float w = Mathf.Clamp(h * 1.4f, 0.4f, 1.8f);
			float barH = w * 0.12f;
			float y = h * 0.85f;

			// 2) 建结构
			var bar = new GameObject("HealthBar");
			bar.transform.SetParent(root.transform, false);
			// 运行时位置由 PenHealthBar 按"相机坐标系"每帧重算（屏幕下方 + 朝相机推近），
			// 所以这里的本地坐标一律填 0 —— 填别的只会让 Scene 里看起来偏，实际不影响运行。
			bar.transform.localPosition = Vector3.zero;

			var bgMat = GetOrCreateMaterial("HealthBarBG", new Color(0.08f, 0.08f, 0.10f, 0.85f));
			var fillMat = GetOrCreateMaterial("HealthBarFill", new Color(0.35f, 0.85f, 0.35f, 1f));

			CreateQuad("BG", bar.transform, bgMat, w, barH, Vector3.zero);
			var fill = CreateQuad("Fill", bar.transform, fillMat, w, barH * 0.72f, new Vector3(0f, 0f, -0.002f));

			// 3) 挂组件并填引用
			var comp = root.GetComponent<PenHealthBar>();
			if (comp == null) comp = root.AddComponent<PenHealthBar>();
			comp.EditorAssign(bar.transform, fill.transform);

			// 4) 存回
			PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
			AssetDatabase.SaveAssets();

			Debug.Log($"[血条] 已写进预制体 {PrefabPath}\n" +
					  $"  尺寸：宽 {w:F3} / 高 {barH:F3} / 离笔心 {y:F3}（世界单位）\n" +
					  $"  可在 Scene 里打开预制体微调 HealthBar 的位置与大小");
		}
		finally
		{
			PrefabUtility.UnloadPrefabContents(root);
		}
	}

	// ================= 工具 =================

	private static Transform CreateQuad(string n, Transform parent, Material mat,
										float w, float h, Vector3 localPos)
	{
		var go = GameObject.CreatePrimitive(PrimitiveType.Quad);
		go.name = n;

		var col = go.GetComponent<Collider>();
		if (col != null) Object.DestroyImmediate(col);      // 血条不参与物理/射线

		go.transform.SetParent(parent, false);
		go.transform.localPosition = localPos;
		go.transform.localScale = new Vector3(w, h, 1f);

		var mr = go.GetComponent<MeshRenderer>();
		if (mr != null)
		{
			mr.sharedMaterial = mat;
			mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
			mr.receiveShadows = false;
		}

		return go.transform;
	}

	private static Material GetOrCreateMaterial(string name, Color c)
	{
		if (!AssetDatabase.IsValidFolder(MaterialDir))
			AssetDatabase.CreateFolder("Assets/Resources/Pens", "Materials");

		string path = $"{MaterialDir}/{name}.mat";

		var existing = AssetDatabase.LoadAssetAtPath<Material>(path);
		if (existing != null) return existing;

		var sh = PickShader();
		if (sh == null)
		{
			Debug.LogWarning("[血条] 找不到可用 Shader，材质会缺省（运行时 PenHealthBar 会兜底生成）");
			return null;
		}

		var m = new Material(sh);
		ApplyColor(m, c);

		if (m.HasProperty("_Surface")) m.SetFloat("_Surface", 1f);
		if (m.HasProperty("_Blend")) m.SetFloat("_Blend", 0f);
		if (m.HasProperty("_ZWrite")) m.SetFloat("_ZWrite", 0f);

		AssetDatabase.CreateAsset(m, path);
		return m;
	}

	// 与 PenHealthBar 里同款（编辑器脚本够不到运行时的 private static，各留一份）
	private static Shader PickShader()
	{
		string[] candidates =
		{
			"Universal Render Pipeline/Particles/Unlit",
			"Universal Render Pipeline/Unlit",
			"Sprites/Default",
			"Unlit/Color"
		};

		foreach (var n in candidates)
		{
			var s = Shader.Find(n);
			if (s != null) return s;
		}
		return null;
	}

	private static void ApplyColor(Material m, Color c)
	{
		if (m == null) return;
		if (m.HasProperty("_Color")) m.SetColor("_Color", c);
		if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", c);
		if (m.HasProperty("_TintColor")) m.SetColor("_TintColor", c);
	}
}
#endif
