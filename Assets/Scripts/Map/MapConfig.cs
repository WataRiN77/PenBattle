using UnityEngine;

/// 单张地图的数据定义。一个 MapConfig 对应一个地图 prefab。
[CreateAssetMenu(fileName = "MapConfig", menuName = "PenBattle/Map Config", order = 0)]
public class MapConfig : ScriptableObject
{
    [Header("标识")]
    public string mapId = "map_01";
    public string displayName = "标准课桌";

    [Header("地图本体")]
    [Tooltip("地图预制体：内含 桌面/墙/障碍/出界区/出生点(SpawnPointMarker)")]
    public GameObject mapPrefab;

    [Header("池子调控")]
    [Tooltip("权重，越大越容易抽到；默认 1")]
    public float weight = 1f;
}
