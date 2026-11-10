using UnityEngine;

/// 可选：挂在每张地图内一个空物体上（自动补 BoxCollider），作为“取景框”。
/// 给 MapCameraFit 提供该地图的取景范围，让不同尺寸/形状的桌面都能被框住。
[RequireComponent(typeof(BoxCollider))]
public class MapBoundsMarker : MonoBehaviour
{
    public Bounds WorldBounds => GetComponent<BoxCollider>().bounds;

    private void Reset()
    {
        var box = GetComponent<BoxCollider>();
        box.isTrigger = true;
        box.center = Vector3.zero;
        box.size = new Vector3(20f, 1f, 20f);
    }
}
