using System.Collections.Generic;
using UnityEngine;

// 冰冻的视觉表现：把笔的网格复制一份、稍微放大，套上"霜冻"材质
// 由 FreezeEffect 挂在一个子物体上（所以销毁它只销毁视觉，不会伤到笔）
//
// 三个关键点：
//  1) 同时处理 MeshRenderer 和 SkinnedMeshRenderer
//  2) 从"整支笔的根节点"往下搜网格 —— PenController 常挂在模型层级的某个子节点上，
//     笔身网格往往是它的兄弟节点，只搜 PenController 下面会漏掉（症状：只有笔尖有霜）
//  3) 花纹用物体空间坐标算（见 Frost.shader 的 _NoiseScaleOS），不依赖 UV
public class FreezeVisual : MonoBehaviour
{
    [Tooltip("霜层比模型放大多少（避免和原表面重合产生闪烁）")]
    public float inflate = 1.002f;

    [Tooltip("结霜蔓延动画时长（秒）")]
    public float spawnDuration = 0.3f;

    [Tooltip("最终结霜覆盖率（1 = 整支笔都结霜）")]
    [Range(0f, 1f)] public float maxProgress = 0.85f;

    [Tooltip("笔身粗细上大约排多少个冰晶（越大颗粒越细）")]
    public float cellsAcrossPen = 4f;

    [Tooltip("沿笔身方向的拉伸系数：<1 表示把晶体沿笔身拉长（细长物体需要，否则纵向会密到像噪点）")]
    [Range(0.05f, 1f)] public float lengthStretch = 0.25f;

    private FreezeEffect _freeze;
    private Material _material;
    private readonly List<GameObject> _overlays = new List<GameObject>();
    private float _t;

    private void Awake()
    {
        _freeze = GetComponentInParent<FreezeEffect>();
        Build();
    }

    private void Build()
    {
        var pen = GetComponentInParent<PenController>();
        if (pen == null)
        {
            enabled = false;
            return;
        }

        _material = LoadMaterial();
        if (_material == null)
        {
            enabled = false;
            return;
        }

        // PenController 可能挂在模型层级的子节点上，笔身网格是它的兄弟节点，
        // 所以从"整支笔的根节点"往下搜。
        // 万一同一个父物体下挂了多支笔，就退回只搜笔自己，避免把别的笔也盖上霜。
        Transform searchRoot = pen.transform.root;
        if (searchRoot != pen.transform &&
            searchRoot.GetComponentsInChildren<PenController>().Length > 1)
        {
            searchRoot = pen.transform;
        }

        // 花纹密度：由"笔身粗细上排多少格"换算成"每本地单位多少格"，与模型真实尺寸无关
        Vector2 penSize = MeasurePenSize(searchRoot);   // x = 本地半长, y = 本地半径
        float diameter = Mathf.Max(penSize.y * 2f, 1e-9f);
        float noiseScaleOS = cellsAcrossPen / diameter;

        _material.SetFloat("_NoiseScaleOS", noiseScaleOS);
        _material.SetFloat("_LengthStretch", lengthStretch);

        // 换算成"看得懂"的数量：横向多少格、沿笔身多少格
        float cellsAcross = diameter * noiseScaleOS;
        float cellsAlong = Mathf.Max(penSize.x * 2f, 1e-9f) * noiseScaleOS * lengthStretch;

        var all = searchRoot.GetComponentsInChildren<Renderer>();
        var skipped = new List<string>();

        foreach (var rend in all)
        {
            if (rend == null)
            {
                skipped.Add("null");
                continue;
            }

            // 只给"笔本身的模型"盖霜：跳过火苗面片以及我们自己的霜层
            if (rend.GetComponentInParent<BurnVisual>() != null)
            {
                skipped.Add($"{rend.name}(燃烧视觉)");
                continue;
            }
            if (rend.gameObject.name.StartsWith("FrostOverlay"))
            {
                skipped.Add($"{rend.name}(霜层)");
                continue;
            }

            Mesh mesh = GetMesh(rend);
            if (mesh == null)
            {
                skipped.Add($"{rend.name}(无网格:{rend.GetType().Name})");
                continue;
            }

            var go = new GameObject("FrostOverlay_" + rend.name);

            // 关键：挂在"源渲染器自己"下面，才能跟着笔一起动
            go.transform.SetParent(rend.transform, false);
            go.transform.localPosition = Vector3.zero;
            go.transform.localRotation = Quaternion.identity;
            go.transform.localScale = Vector3.one * inflate;

            go.AddComponent<MeshFilter>().sharedMesh = mesh;

            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = _material;
            mr.enabled = pen.isVisible;   // 笔隐藏时霜层一起隐藏
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            mr.receiveShadows = false;

            _overlays.Add(go);
        }

        // 诊断日志：把搜索范围里所有渲染器都列出来（含被跳过的原因）
        var covered = new List<string>();
        foreach (var go in _overlays) covered.Add(go.name.Replace("FrostOverlay_", ""));

        Debug.Log($"[冰冻] 搜索根 {searchRoot.name}：共 {all.Length} 个渲染器 → 覆盖 {_overlays.Count} 个 " +
                  $"[{(covered.Count > 0 ? string.Join(", ", covered) : "无")}]；" +
                  $"跳过 [{(skipped.Count > 0 ? string.Join(", ", skipped) : "无")}]；" +
                  $"冰晶格数：横向 {cellsAcross:0.#} 格、沿笔身 {cellsAlong:0.#} 格" +
                  $"（本地半径 {penSize.y:0.#####}，_NoiseScaleOS {noiseScaleOS:0.#}）");

        if (_overlays.Count == 0)
        {
            Debug.LogWarning("[冰冻] 没找到可用的笔网格，霜冻视觉无法显示");
            enabled = false;
            return;
        }

        _material.SetFloat("_Progress", 0f);
    }

    // MeshRenderer 用 MeshFilter.sharedMesh；SkinnedMeshRenderer 用 sharedMesh
    private static Mesh GetMesh(Renderer rend)
    {
        if (rend is SkinnedMeshRenderer skinned) return skinned.sharedMesh;

        var mf = rend.GetComponent<MeshFilter>();
        return mf != null ? mf.sharedMesh : null;
    }

    // 返回 (本地半长, 本地半径)，取范围内第一个有效网格
    private static Vector2 MeasurePenSize(Transform searchRoot)
    {
        foreach (var rend in searchRoot.GetComponentsInChildren<Renderer>())
        {
            if (rend == null) continue;
            if (rend.GetComponentInParent<BurnVisual>() != null) continue;
            if (rend.gameObject.name.StartsWith("FrostOverlay")) continue;

            var e = rend.localBounds.extents;
            float half = Mathf.Max(e.y, 0.0001f);
            float rad = Mathf.Max(Mathf.Max(e.x, e.z), half * 0.02f);
            return new Vector2(half, rad);
        }

        var cap = searchRoot.GetComponentInChildren<CapsuleCollider>();
        if (cap != null && cap.height > 0.0001f)
            return new Vector2(cap.height * 0.5f, Mathf.Max(cap.radius, 0.0001f));

        return new Vector2(0.5f, 0.05f);
    }

    private Material LoadMaterial()
    {
        var shared = Resources.Load<Material>("VFX/Frost");
        if (shared == null)
        {
            var sh = Shader.Find("PenBattle/Frost");
            if (sh == null)
            {
                Debug.LogWarning("[冰冻] 找不到 VFX/Frost 材质，也找不到 PenBattle/Frost shader，霜冻无法显示");
                return null;
            }
            shared = new Material(sh);
        }

        return new Material(shared);   // 每支笔一份实例
    }

    private void Update()
    {
        // 冰缓解除后自动清理
        if (_freeze == null)
        {
            DestroySelf();
            return;
        }

        if (_material == null) return;

        // 结霜蔓延动画：0 -> maxProgress
        if (_t < spawnDuration)
        {
            _t += Time.deltaTime;
            float k = Mathf.Clamp01(_t / Mathf.Max(0.01f, spawnDuration));
            _material.SetFloat("_Progress", Mathf.Lerp(0f, maxProgress, k));
        }
    }

    private void DestroySelf()
    {
        ClearOverlays();
        Destroy(gameObject);
    }

    private void ClearOverlays()
    {
        foreach (var go in _overlays)
        {
            if (go != null) Destroy(go);
        }
        _overlays.Clear();
    }

    private void OnDestroy()
    {
        ClearOverlays();
        if (_material != null) Destroy(_material);
    }
}
