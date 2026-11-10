using UnityEngine;
using Cinemachine;

/// 可选：让同一个相机自适应不同尺寸/形状的地图。
/// 挂在场景里，指定 Virtual Camera；每张地图 prefab 内放一个 MapBoundsMarker 作为取景框。
public class MapCameraFit : MonoBehaviour
{
    [Tooltip("场景里的 Cinemachine 虚拟相机")]
    public CinemachineVirtualCamera vcam;
    [Tooltip("取景留白：1.15 = 四周多留 15%")]
    public float padding = 1.15f;
    [Tooltip("把相机水平居中到地图")]
    public bool centerHorizontally = true;

    private void LateUpdate()
    {
        if (vcam == null || MapManager.Instance == null) return;
        if (!MapManager.Instance.TryGetBounds(out Bounds b)) return;

        // 地图在"选笔 / 开局强化"阶段是隐藏的，此时 Collider.bounds 可能取到空值，
        // 直接拿去算距离会让相机钻到桌子底下 —— 判一下尺寸再拟合。
        if (b.size.sqrMagnitude < 0.0001f) return;

        Fit(b);
    }

    public void Fit(Bounds b)
    {
        float aspect = (float)Screen.width / Mathf.Max(1, Screen.height);
        Vector3 p = vcam.transform.position;

        if (vcam.m_Lens.Orthographic)
        {
            float needV = b.extents.z * padding;
            float needH = (b.extents.x * padding) / aspect;
            vcam.m_Lens.OrthographicSize = Mathf.Max(needV, needH);
        }
        else
        {
            float halfTan = Mathf.Tan(vcam.m_Lens.FieldOfView * 0.5f * Mathf.Deg2Rad);
            float needV = (b.extents.z * padding) / halfTan;
            float needH = (b.extents.x * padding) / (halfTan * aspect);
            p.z = b.center.z - Mathf.Max(needV, needH);   // 相机在 -Z 方向俯视
        }

        if (centerHorizontally) p.x = b.center.x;
        vcam.transform.position = p;
    }
}
