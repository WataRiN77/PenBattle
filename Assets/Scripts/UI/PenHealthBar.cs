using UnityEngine;

/// 挂在笔上的**世界空间血条**。
///
/// 两种工作方式（优先前者）：
///   ① **预制体里做好**：把 `barRoot` / `fillBar` 在预制体里搭好并拖进来（推荐 —— 能在 Editor 里直接看和调）。
///      菜单 `Tools → PenBattle → 给笔预制体加血条` 可以一键生成这套结构。
///   ② **运行时自动生成**：两个引用都留空时，自动建两条 Quad（不需要任何美术资源）。
///
/// 其它行为：
///   · 血条是笔的**子物体**（预制体 scale 已是 1，本地单位 == 世界单位，直接挂没问题）；
///   · 每帧让 `barRoot` 对齐相机旋转（billboard）—— 直接写世界旋转，父物体翻滚也照样正对屏幕；
///   · 笔隐身时血条一起隐身（准备阶段看不到笔，也不该看到血条）；
///   · 血量取值：联机优先读 `PenNetworkBridge`（分子 `Durability` / 分母 `MaxDurability`）；
///     没有桥接（单机）时回退读 `PenController`。两者的同步值都拿负数/0 当"未初始化"哨兵。
// 执行顺序必须排在 CinemachineBrain（默认 1000）之后：
// 相机在 LateUpdate 里被 Cinemachine 移动，血条又要读相机方向 ——
// 若先于它执行，拿到的是上一帧的相机位姿，相机震动时血条会抖/滞后一帧。
[DefaultExecutionOrder(2000)]
[DisallowMultipleComponent]
public class PenHealthBar : MonoBehaviour
{
    [Header("预制体里做好的血条（留空 = 运行时自动生成）")]
    [Tooltip("血条容器，默认找名为 HealthBar 的子物体")]
    [SerializeField] private Transform barRoot;

    [Tooltip("填充条，默认找 HealthBar/Fill")]
    [SerializeField] private Transform fillBar;

    [Tooltip("填充条的 Renderer（改颜色用），留空自动取")]
    [SerializeField] private Renderer fillRenderer;

    [Header("位置偏移（相机坐标系，运行时每帧重算）")]
    [Tooltip("往【屏幕下方】挪多少世界单位 —— 正数往下。0 就是贴在笔中心")]
    [SerializeField] private float screenDownOffset = 0.30f;

    [Tooltip("往【相机方向】推多少世界单位 —— 推近一点就不会被笔身挡住")]
    [SerializeField] private float towardCameraOffset = 0.20f;

    [Header("自动生成时的参数（预制体已做好则忽略）")]
    [SerializeField] private bool autoCreateIfMissing = true;
    [Tooltip("血条离笔顶多高 —— 倍数，1 = 笔的包围盒高度")]
    [SerializeField] private float autoHeightFactor = 0.85f;
    [Tooltip("血条宽度 / 笔高度的比例")]
    [SerializeField] private float autoWidthFactor = 1.4f;
    [SerializeField] private float autoMinWidth = 0.4f;
    [SerializeField] private float autoMaxWidth = 1.8f;

    private PenController _pen;
    private PenNetworkBridge _bridge;
    private Renderer _penRenderer;

    private Material _bgMat;
    private Material _fillMat;

    private Vector3 _fillBaseScale;
    private Vector3 _fillBasePos;

    private float _shownRatio = -1f;
    private bool _shownVisible = true;

    private void Awake()
    {
        _pen = GetComponent<PenController>();
        _bridge = GetComponent<PenNetworkBridge>();
        _penRenderer = GetComponentInChildren<Renderer>();

        ResolveBar();
    }

    private void OnDestroy()
    {
        // 只清理运行时自己造的材质（预制体里的材质资产不能动）
        if (_bgMat != null) Destroy(_bgMat);
        if (_fillMat != null) Destroy(_fillMat);
    }

    // ================= 解析血条结构 =================

    private void ResolveBar()
    {
        // ① 预制体里做好的（拖了引用就直接用，没拖就按名字找）
        if (barRoot == null) barRoot = transform.Find("HealthBar");
        if (barRoot != null && fillBar == null) fillBar = barRoot.Find("Fill");

        // ② 都没有 → 运行时生成
        if (barRoot == null && autoCreateIfMissing) BuildRuntimeBar();

        if (barRoot == null || fillBar == null)
        {
            enabled = false;
            return;
        }

        if (fillRenderer == null) fillRenderer = fillBar.GetComponent<Renderer>();

        _fillBaseScale = fillBar.localScale;
        _fillBasePos = fillBar.localPosition;

        // 材质：预制体里给了就用它的，没给就补一个运行时材质（保证不会是粉红）
        EnsureMaterial(barRoot.Find("BG"), ref _bgMat, new Color(0.08f, 0.08f, 0.10f, 0.85f));
        EnsureMaterial(fillBar, ref _fillMat, new Color(0.35f, 0.85f, 0.35f, 1f));
    }

    private void EnsureMaterial(Transform t, ref Material cache, Color c)
    {
        if (t == null || cache != null) return;

        var r = t.GetComponent<Renderer>();
        if (r == null) return;

        if (r.sharedMaterial == null)
        {
            cache = MakeMaterial(c);
            r.sharedMaterial = cache;
        }
        else
        {
            cache = r.sharedMaterial;      // 预制体里已有材质 → 沿用，不改它
        }
    }

    private void BuildRuntimeBar()
    {
        float h = (_penRenderer != null) ? Mathf.Max(_penRenderer.bounds.size.y, 0.01f) : 1f;

        float w = Mathf.Clamp(h * autoWidthFactor, autoMinWidth, autoMaxWidth);
        float barH = w * 0.12f;

        var root = new GameObject("HealthBar").transform;
        root.SetParent(transform, false);
        root.localPosition = new Vector3(0f, h * autoHeightFactor, 0f);

        barRoot = root;
        MakeQuad("BG", root, w, barH, Vector3.zero);
        fillBar = MakeQuad("Fill", root, w, barH * 0.72f, new Vector3(0f, 0f, -0.002f));
    }

    private Transform MakeQuad(string n, Transform parent, float w, float h, Vector3 localPos)
    {
        var go = GameObject.CreatePrimitive(PrimitiveType.Quad);
        go.name = n;

        var col = go.GetComponent<Collider>();
        if (col != null) Destroy(col);           // 血条不参与物理/射线

        go.transform.SetParent(parent, false);
        go.transform.localPosition = localPos;
        go.transform.localScale = new Vector3(w, h, 1f);

        var mr = go.GetComponent<MeshRenderer>();
        if (mr != null)
        {
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            mr.receiveShadows = false;
        }

        return go.transform;
    }

    // ================= 每帧更新 =================

    private void LateUpdate()
    {
        if (barRoot == null || fillBar == null || _pen == null) return;

        // 笔隐身（准备阶段）→ 血条一起隐身
        bool visible = _penRenderer == null || _penRenderer.enabled;
        if (visible != _shownVisible)
        {
            _shownVisible = visible;
            barRoot.gameObject.SetActive(visible);
        }
        if (!visible) return;

        // 位置 + 朝向：全部在【相机坐标系】里算。
        // 血条虽然是笔的子物体，但笔的根物体带着 -90° 的 X 旋转，
        // 直接用局部坐标会把"上/下"变成"沿桌面平移"→ 血条就贴在笔身中央。
        // 所以这里每帧直接写世界位置 + 世界旋转，完全不受笔翻滚影响。
        var cam = Camera.main;
        if (cam != null)
        {
            barRoot.rotation = cam.transform.rotation;

            Vector3 offset = (-cam.transform.up * screenDownOffset)            // 屏幕下方
                           + (-cam.transform.forward * towardCameraOffset);    // 朝相机推近，避免被笔身挡住

            barRoot.position = transform.position + offset;
        }

        // 长度
        float ratio = CurrentRatio();
        if (Mathf.Approximately(ratio, _shownRatio)) return;

        _shownRatio = ratio;

        fillBar.localScale = new Vector3(_fillBaseScale.x * Mathf.Max(ratio, 0.002f),
                                         _fillBaseScale.y,
                                         _fillBaseScale.z);

        // 轴心在中心 → 往左挪"缩掉长度的一半"，视觉上从左往右收缩
        float shift = -_fillBaseScale.x * (1f - ratio) * 0.5f;
        fillBar.localPosition = _fillBasePos + new Vector3(shift, 0f, 0f);

        if (fillRenderer != null)
        {
            // 用 .material 拿实例，避免改到预制体共享的那个材质
            ApplyColor(fillRenderer.material, RatioColor(ratio));
        }
    }

    /// 当前血量比例（0~1）
    private float CurrentRatio()
    {
        float max = CurrentMaxDurability();
        if (max <= 0f) return 1f;

        // 联机：读同步值（负数 = 还没收到过，回退本地）；单机：读本地值。
        // ⚠️ 判断必须是 >= 0 而不是 > 0：耐久真被打到 0 时，用 > 0 会判成"没数据"，
        //    回退去读本地满血值 → 血条弹回满。所以同步值拿 -1 当"未初始化"哨兵。
        float cur = (_bridge != null && _bridge.Durability >= 0f)
            ? _bridge.Durability
            : (_pen != null ? _pen.StatusDurability : max);

        return Mathf.Clamp01(cur / max);
    }

    /// 血条分母（耐久上限）。
    /// ⚠️ 客户端不跑 ApplyConfig / ApplyAll，本地的 maxStatusDurability 停在预制体默认值；
    ///    房主点了「耐久上限 +20%」之后，只读本地值会让两边血条比例永远对不上，
    ///    所以联机时优先读同步来的 MaxDurability。
    private float CurrentMaxDurability()
    {
        if (_bridge != null && _bridge.MaxDurability > 0f) return _bridge.MaxDurability;
        return (_pen != null) ? _pen.maxStatusDurability : 100f;
    }

    /// 满血绿 → 半血黄 → 残血红
    private static Color RatioColor(float r)
    {
        var cLow = new Color(0.90f, 0.25f, 0.22f);
        var cMid = new Color(0.95f, 0.80f, 0.25f);
        var cHigh = new Color(0.35f, 0.85f, 0.35f);

        return (r > 0.5f)
            ? Color.Lerp(cMid, cHigh, (r - 0.5f) * 2f)
            : Color.Lerp(cLow, cMid, r * 2f);
    }

    // ================= 材质（沿用项目 ShockwaveEffect 的 URP 兜底写法）=================

    private static Material MakeMaterial(Color c)
    {
        var sh = PickShader();
        if (sh == null) return null;

        var m = new Material(sh);
        ApplyColor(m, c);

        if (m.HasProperty("_Surface")) m.SetFloat("_Surface", 1f);
        if (m.HasProperty("_Blend")) m.SetFloat("_Blend", 0f);
        if (m.HasProperty("_ZWrite")) m.SetFloat("_ZWrite", 0f);
        return m;
    }

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

#if UNITY_EDITOR
    /// <summary>给编辑器脚本用：把做好的血条结构填进来</summary>
    public void EditorAssign(Transform root, Transform fill)
    {
        barRoot = root;
        fillBar = fill;
        fillRenderer = (fill != null) ? fill.GetComponent<Renderer>() : null;
    }
#endif
}
