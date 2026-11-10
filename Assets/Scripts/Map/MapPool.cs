using System.Collections.Generic;
using UnityEngine;

/// 显式地图池：在 Inspector 里增删 / 排序 / 调权重，方便调控。
[CreateAssetMenu(fileName = "MapPool", menuName = "PenBattle/Map Pool", order = 0)]
public class MapPool : ScriptableObject
{
    [Tooltip("参与随机的地图（显式配置，不做文件夹扫描）")]
    public List<MapConfig> maps = new List<MapConfig>();
}
