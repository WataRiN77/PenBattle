using UnityEngine;

// 冲击波：立刻把半径内的其它笔向外推开，并生成一圈扩散的视觉
// 主动技能以后也能直接调 Spawn(...)
public class ShockwaveEffect : MonoBehaviour
{
    private const float DefaultDuration = 0.65f;  // 比原来 0.5 稍长：直径 20 的圈扩散太快会糊成一片
    private const float StartRadius    = 0.15f;   // 起始半径（世界单位）
    private const float ExpandPortion  = 0.55f;   // 前 55% 时间用来扩散，剩下时间停在最大半径上淡出
    private const float RingInnerRatio = 0.86f;   // 亮环的内径比例（0.86 = 环宽占半径的 14%）
    private const float HeightOffset   = 0.10f;   // 抬高一点：贴着桌面会 z-fighting，太低会被桌面挡掉

    // 用常规透明混合而不是加色：桌面是浅色的，加色会把颜色冲白，边缘反而糊掉
    private static readonly Color RingColor = new Color(0.42f, 0.90f, 1f, 0.95f);
    private static readonly Color FillColor = new Color(0.30f, 0.78f, 1f, 0.35f);

    private static Mesh s_ringMesh;
    private static Mesh s_fillMesh;

    private Transform _ring;
    private Transform _fill;
    private Material _matRing;
    private Material _matFill;
    private float _radius;
    private float _duration;
    private float _t;

    /// <summary>
    /// 在 center 处爆发一道冲击波：推开半径内的其它笔 + 播放视觉
    /// center 传笔的世界坐标，视觉会贴着笔的高度显示（桌面顶面可能高于 y=0，写死高度会被埋进桌子里）
    /// </summary>
    /// <param name="ownerPenId">爆心所属的笔，这支笔不会被推开（传 null 表示全都推）</param>
    public static void Spawn(Vector3 center, float radius, float force,
                             string ownerPenId = null, float duration = DefaultDuration)
    {
        Debug.Log($"[冲击波] 参数：半径 {radius:0.##}，推力 {force:0.##}");

        // 推力只在房主侧算（权威）；视觉要各端都看得见，所以走联机广播
        Push(center, radius, force, ownerPenId);

        var sync = NetworkMatchSync.Instance;
        if (sync != null && sync.IsSpawned && sync.IsServer)
            sync.HostBroadcastShockwave(center, radius, duration);   // 房主也走 RPC 回来，避免出两份视觉
        else
            SpawnVisual(center, radius, duration);
    }

    /// <summary>
    /// 只播视觉、不推笔。客户端走这条；房主经 ClientRpc 回来也走这条。
    /// 联机时**不要**让两端各算一次推力，否则同一支笔会被推两次。
    /// </summary>
    public static void SpawnVisual(Vector3 center, float radius, float duration)
    {
        var go = new GameObject("ShockwaveFX");
        // 用笔的高度再抬高一点：桌面顶面可能高于 y=0（本项目桌面顶面在 y≈1.5），
        // 写死高度会被埋进桌子里 —— 那样半透明的圈就"看不太见"了
        go.transform.position = new Vector3(center.x, center.y + HeightOffset, center.z);
        go.AddComponent<ShockwaveEffect>().Init(radius, duration);
    }

    // 把半径内的其它笔按距离衰减向外推开
    private static void Push(Vector3 center, float radius, float force, string ownerPenId)
    {
        var gm = GameManager.Instance;
        if (gm == null)
        {
            Debug.LogError("[冲击波] GameManager 为空，推力没有结算");
            return;
        }

        Vector3 flatCenter = new Vector3(center.x, 0f, center.z);
        int pushedCount = 0;

        foreach (var p in gm.players)
        {
            if (p == null)
            {
                Debug.LogWarning("[冲击波] 跳过：players 里有空条目");
                continue;
            }
            if (p.isEliminated)
            {
                Debug.Log($"[冲击波] 跳过 {p.playerId}：本小局已淘汰");
                continue;
            }
            if (p.pen == null)
            {
                Debug.LogWarning($"[冲击波] 跳过 {p.playerId}：PlayerData.pen 为空");
                continue;
            }
            if (!p.pen.isActive)
            {
                Debug.Log($"[冲击波] 跳过 {p.playerId}：笔未激活（isActive=false）");
                continue;
            }
            if (ownerPenId != null && p.pen.penId == ownerPenId)
            {
                Debug.Log($"[冲击波] 跳过 {p.playerId}：爆心自己（penId={p.pen.penId}）");
                continue;
            }

            // 距离优先读刚体位置：autoSyncTransforms 关闭时，transform 在两次物理步之间可能是旧值
            var targetRb = p.pen.GetRigidbody();
            Vector3 pos = targetRb != null ? targetRb.position : p.pen.transform.position;

            Vector3 delta = new Vector3(pos.x, 0f, pos.z) - flatCenter;
            float dist = delta.magnitude;
            if (dist > radius)
            {
                Debug.Log($"[冲击波] 跳过 {p.playerId}：距离 {dist:0.##} > 半径 {radius:0.##}" +
                          $"（爆心 {flatCenter}，目标 {new Vector3(pos.x, 0f, pos.z)}）");
                continue;
            }

            Vector3 dir;
            if (dist > 0.01f)
            {
                dir = delta / dist;
            }
            else
            {
                // 正好叠在爆心上：给个随机水平方向，避免除零
                float a = Random.Range(0f, Mathf.PI * 2f);
                dir = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
            }

            float falloff = 1f - Mathf.Clamp01(dist / radius);   // 越近推得越狠
            float impulse = force * (0.35f + 0.65f * falloff);

            if (targetRb == null)
            {
                Debug.LogWarning($"[冲击波] {p.playerId} 没有 Rigidbody，推不动");
                continue;
            }
            if (targetRb.isKinematic)
            {
                Debug.LogWarning($"[冲击波] {p.playerId} 的刚体是 kinematic，AddForce 不会生效");
                continue;
            }

            targetRb.WakeUp();
            targetRb.AddForce(dir * impulse, ForceMode.Impulse);
            pushedCount++;

            Debug.Log($"[冲击波] 推开 {p.pen.penId}：距离 {dist:0.##}，冲量 {impulse:0.##}");
        }

        if (pushedCount == 0)
            Debug.LogWarning($"[冲击波] 本次没有推开任何笔（players {gm.players.Count} 人，半径 {radius:0.##}）");
    }

    private void Init(float radius, float duration)
    {
        _radius = Mathf.Max(0.1f, radius);
        _duration = Mathf.Max(0.05f, duration);

        // 两层：一圈亮环定边界，一片很淡的内填充给"能量感"。
        // 之前是单个实心薄片 —— 半透明色块边缘和桌面糊在一起，看起来不像圆。
        _matRing = CreateMaterial(RingColor);
        _matFill = CreateMaterial(FillColor);

        _ring = SpawnLayer("Ring", GetRingMesh(), _matRing);
        _fill = SpawnLayer("Fill", GetFillMesh(), _matFill);

        float r = StartRadius;
        if (_ring != null) _ring.localScale = new Vector3(r, 1f, r);
        // 填充只铺到环的内径为止，两层不重叠 —— 半透明面叠在一起会二次变深，出现一道脏边
        if (_fill != null) _fill.localScale = new Vector3(r * RingInnerRatio, 1f, r * RingInnerRatio);

        Debug.Log($"[冲击波] 视觉：最大半径 {_radius:0.##}（直径 {_radius * 2f:0.##}），" +
                  $"环材质 {(_matRing != null ? _matRing.shader.name : "null")}");
    }

    private Transform SpawnLayer(string layerName, Mesh mesh, Material mat)
    {
        if (mesh == null || mat == null) return null;

        var go = new GameObject(layerName);
        go.transform.SetParent(transform, false);
        go.AddComponent<MeshFilter>().sharedMesh = mesh;

        var mr = go.AddComponent<MeshRenderer>();
        mr.sharedMaterial = mat;
        mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        mr.receiveShadows = false;
        mr.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
        mr.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;

        return go.transform;
    }

    private void Update()
    {
        if (_ring == null) { Destroy(gameObject); return; }

        _t += Time.deltaTime;
        float k = Mathf.Clamp01(_t / _duration);

        // 扩散用 easeOut：一开始冲得快，后面收得慢，读起来更像冲击波
        float grow = Mathf.Clamp01(k / ExpandPortion);
        float ease = 1f - Mathf.Pow(1f - grow, 3f);
        float r = Mathf.Lerp(StartRadius, _radius, ease);

        if (_ring != null) _ring.localScale = new Vector3(r, 1f, r);
        if (_fill != null) _fill.localScale = new Vector3(r * RingInnerRatio, 1f, r * RingInnerRatio);

        // 扩散阶段保持亮度，之后整体（颜色 + alpha）一起衰减到 0
        float fade = k < ExpandPortion
            ? 1f
            : Mathf.Lerp(1f, 0f, (k - ExpandPortion) / (1f - ExpandPortion));

        // 环到最后会略微变亮，像能量被甩出去；alpha 单独夹住，别超过 1
        float ringBoost = fade * Mathf.Lerp(1f, 1.35f, grow);
        ApplyColor(_matRing, new Color(RingColor.r * ringBoost, RingColor.g * ringBoost,
                                       RingColor.b * ringBoost, RingColor.a * fade));
        ApplyColor(_matFill, new Color(FillColor.r * fade, FillColor.g * fade,
                                       FillColor.b * fade, FillColor.a * fade));

        if (k >= 1f) Destroy(gameObject);
    }

    private void OnDestroy()
    {
        if (_matRing != null) Destroy(_matRing);
        if (_matFill != null) Destroy(_matFill);
    }

    // ===== Shader / 颜色兼容（URP 与内置管线都能用）=====

    private static Material CreateMaterial(Color color)
    {
        var sh = PickShader();
        if (sh == null)
        {
            Debug.LogWarning("[冲击波] 找不到可用 Shader，视觉可能不可见");
            return null;
        }

        var m = new Material(sh) { name = "ShockwaveFX" };
        ApplyColor(m, color);

        // 透明 + 常规 alpha 混合（不用加色：浅色桌面上加色会被冲成白，边缘反而看不见）
        if (m.HasProperty("_Surface"))  m.SetFloat("_Surface", 1f);       // 0=Opaque 1=Transparent
        if (m.HasProperty("_Blend"))    m.SetFloat("_Blend", 0f);         // 0=Alpha
        if (m.HasProperty("_SrcBlend")) m.SetFloat("_SrcBlend", 5f);      // SrcAlpha
        if (m.HasProperty("_DstBlend")) m.SetFloat("_DstBlend", 10f);     // OneMinusSrcAlpha
        if (m.HasProperty("_ZWrite"))   m.SetFloat("_ZWrite", 0f);
        if (m.HasProperty("_Cull"))     m.SetFloat("_Cull", 0f);          // 双面（万一法线朝下也看得见）
        if (m.HasProperty("_AlphaClip")) m.SetFloat("_AlphaClip", 0f);

        m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
        m.renderQueue = 3000;                                           // Transparent 队列，压在桌面之上
        return m;
    }

    // ===== 程序化几何：不依赖任何美术资源 =====

    /// <summary>亮环（内径 0.86 → 外径 1），用来"定边界"</summary>
    private static Mesh GetRingMesh()
    {
        if (s_ringMesh == null) s_ringMesh = BuildAnnulus(RingInnerRatio, false);
        return s_ringMesh;
    }

    /// <summary>内部填充（中心 → 外径 1），很淡，给能量感</summary>
    private static Mesh GetFillMesh()
    {
        if (s_fillMesh == null) s_fillMesh = BuildAnnulus(0f, true);
        return s_fillMesh;
    }

    /// <summary>
    /// 生成水平放置的圆环带网格，外径固定为 1（用 localScale 缩放半径）。
    /// innerRatio = 0 时退化成实心圆盘。
    /// 顶点色：内缘亮、外缘暗，让环看起来有厚度而不是一片死色。
    /// </summary>
    private static Mesh BuildAnnulus(float innerRatio, bool fadeToEdge)
    {
        const int seg = 72;
        const int ringVerts = (seg + 1) * 2;

        var verts = new Vector3[ringVerts];
        var cols = new Color[ringVerts];
        var uvs = new Vector2[ringVerts];
        var tris = new int[seg * 6];

        float innerAlpha = fadeToEdge ? 1f : 0.85f;
        float outerAlpha = fadeToEdge ? 0f : 0.25f;

        for (int i = 0; i <= seg; i++)
        {
            float t = i / (float)seg;
            float a = t * Mathf.PI * 2f;
            float cs = Mathf.Cos(a), sn = Mathf.Sin(a);

            verts[i * 2]     = new Vector3(cs * innerRatio, 0f, sn * innerRatio);
            verts[i * 2 + 1] = new Vector3(cs, 0f, sn);

            cols[i * 2]     = new Color(1f, 1f, 1f, innerAlpha);
            cols[i * 2 + 1] = new Color(1f, 1f, 1f, outerAlpha);

            uvs[i * 2]     = new Vector2(t, 0f);
            uvs[i * 2 + 1] = new Vector2(t, 1f);
        }

        // 绕序：让法线朝 +Y（从上往下看是正面，背面剔除时也不会消失）
        int vi = 0;
        for (int i = 0; i < seg; i++)
        {
            int b = i * 2;
            tris[vi++] = b;     tris[vi++] = b + 2; tris[vi++] = b + 1;
            tris[vi++] = b + 1; tris[vi++] = b + 2; tris[vi++] = b + 3;
        }

        var mesh = new Mesh { name = "ShockwaveAnnulus" };
        mesh.vertices = verts;
        mesh.colors = cols;
        mesh.uv = uvs;
        mesh.triangles = tris;
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();
        return mesh;
    }

    private static Shader PickShader()
    {
        // URP 优先，其次内置管线
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
