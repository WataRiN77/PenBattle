using UnityEngine;

// 燃烧的视觉表现：在笔身上放几片"火苗"面片（billboard），强度/大小/火舌密度随燃烧层数增强
// 由 BurnEffect 挂在一个子物体上（所以 Destroy(gameObject) 只销毁视觉，不会伤到笔）
public class BurnVisual : MonoBehaviour
{
    [Header("火苗")]
    [Tooltip("火苗面片数量")]
    public int flameCount = 2;

    [Tooltip("火苗大小倍率：调小 = 火更紧贴笔身，调大 = 火更蓬松")]
    public float sizeMultiplier = 0.25f;

    [Tooltip("燃烧层数达到这个值时视觉最强")]
    public float maxStacksForFullIntensity = 10f;

    private BurnEffect _burn;
    private Transform[] _flames;
    private Vector3[] _baseScales;
    private Material _material;
    private float _phase;

    private void Awake()
    {
        _burn = GetComponentInParent<BurnEffect>();
        _phase = Random.Range(0f, 20f);
        Build();
    }

    private void Build()
    {
        // 1) 先量出笔的本地半长，再建火苗（避免把自己量进去）
        var pen = GetComponentInParent<PenController>();
        float halfLen = MeasureHalfLength(pen);

        // 2) 材质：优先用 Resources 里的（打包也安全），找不到再按 shader 名找
        _material = LoadMaterial();
        if (_material == null)
        {
            enabled = false;
            return;
        }

        // 3) 生成火苗面片
        int count = Mathf.Max(1, flameCount);
        _flames = new Transform[count];
        _baseScales = new Vector3[count];

        float spread = halfLen * 0.12f;
        float h = halfLen * 1.1f * sizeMultiplier;

        for (int i = 0; i < count; i++)
        {
            var quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
            quad.name = $"Flame_{i}";
            quad.transform.SetParent(transform, false);
            Destroy(quad.GetComponent<Collider>());   // 纯视觉，不能参与物理

            float along = count > 1 ? i / (float)(count - 1) : 0.5f;
            float y = Mathf.Lerp(-halfLen * 0.85f, halfLen * 0.85f, along);

            quad.transform.localPosition = new Vector3(
                Random.Range(-spread, spread), y, Random.Range(-spread, spread));

            var scale = new Vector3(h * 0.6f, h, 1f);
            quad.transform.localScale = scale;
            _baseScales[i] = scale;

            var quadRenderer = quad.GetComponent<Renderer>();
            quadRenderer.material = _material;
            quadRenderer.enabled = pen == null || pen.isVisible;   // 笔隐藏时火苗别抢先露头
            _flames[i] = quad.transform;
        }

        Debug.Log($"[燃烧] 火苗已生成：{count} 片，本地半长基准 {halfLen:0.####}，尺寸 {h:0.####}");
    }

    // 取笔的"本地半长"：模型包围盒优先（永远对应本地 Y），其次胶囊体，最后兜底
    private static float MeasureHalfLength(PenController pen)
    {
        if (pen == null) return 0.5f;

        var rend = pen.GetComponentInChildren<Renderer>();
        if (rend != null)
        {
            float e = rend.localBounds.extents.y;
            if (e > 0.0001f) return e;
        }

        var cap = pen.GetComponentInChildren<CapsuleCollider>();
        if (cap != null && cap.height > 0.0001f) return cap.height * 0.5f;

        return 0.5f;
    }

    private Material LoadMaterial()
    {
        var shared = Resources.Load<Material>("VFX/Flame");
        if (shared == null)
        {
            var sh = Shader.Find("PenBattle/Flame");
            if (sh == null)
            {
                Debug.LogWarning("[燃烧] 找不到 VFX/Flame 材质，也找不到 PenBattle/Flame shader，火焰无法显示");
                return null;
            }
            shared = new Material(sh);
        }

        return new Material(shared);   // 每支笔一份实例，各自控制强度
    }

    private void Update()
    {
        // 燃烧结束后自动清理（只销毁这个视觉子物体，不影响笔）
        if (_burn == null)
        {
            Destroy(gameObject);
            return;
        }

        if (_material == null) return;

        float t = Mathf.Clamp01(_burn.stacks / Mathf.Max(1f, maxStacksForFullIntensity));

        // 层数越高：越亮、烧得越快、火舌越密、覆盖越"满"
        _material.SetFloat("_Intensity", Mathf.Lerp(0.85f, 1.8f, t));
        _material.SetFloat("_Speed", Mathf.Lerp(0.8f, 2.2f, t));
        _material.SetFloat("_CellDensity", Mathf.Lerp(3.5f, 8f, t));
        _material.SetFloat("_Density", Mathf.Lerp(1.1f, 2.0f, t));
        _material.SetFloat("_TongueAmount", Mathf.Lerp(0.5f, 0.85f, t));
        _material.SetFloat("_Opacity", Mathf.Lerp(1.7f, 3.0f, t));
        _material.SetFloat("_Contrast", Mathf.Lerp(1.0f, 0.6f, t));

        float size = Mathf.Lerp(0.85f, 1.45f, t);
        var cam = Camera.main;

        for (int i = 0; i < _flames.Length; i++)
        {
            var f = _flames[i];
            if (f == null) continue;

            // 火苗抖动
            float flicker = size * (1f + 0.13f * Mathf.Sin(Time.time * 13f + i * 2.7f + _phase));

            // 面向相机（billboard），带一点左右摆动
            if (cam != null)
            {
                float roll = Mathf.Sin(Time.time * 5f + i + _phase) * 5f;
                f.rotation = cam.transform.rotation * Quaternion.Euler(0f, 0f, roll);
            }

            f.localScale = new Vector3(_baseScales[i].x, _baseScales[i].y * flicker, _baseScales[i].z);
        }
    }

    private void OnDestroy()
    {
        if (_material != null) Destroy(_material);
    }
}
