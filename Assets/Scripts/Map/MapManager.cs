using System.Collections.Generic;
using System.Linq;
using UnityEngine;

/// 地图调度：每小局从显式 MapPool 里随机抽一张地图装载（不允许连续重复）。
public class MapManager : MonoBehaviour
{
    public static MapManager Instance { get; private set; }

    [Header("地图池")]
    public MapPool pool;

    [Header("装载")]
    [Tooltip("地图实例的父节点，留空则放场景根")]
    public Transform mapParent;
    [Tooltip("是否避免连续两小局抽到同一张")]
    public bool avoidConsecutiveRepeat = true;
    [Tooltip("固定种子（0=每局真随机）；联机时由主机下发同一种子")]
    public int seed = 0;

    [Header("调试")]
    public bool logEachRoll = true;

    // ---- 运行时状态 ----
    public MapConfig Current { get; private set; }
    public GameObject CurrentInstance { get; private set; }
    public Transform[] SpawnPoints { get; private set; } = new Transform[0];

    public bool TryGetBounds(out Bounds bounds)
    {
        if (CurrentInstance != null)
        {
            var marker = CurrentInstance.GetComponentInChildren<MapBoundsMarker>(true);
            if (marker != null) { bounds = marker.WorldBounds; return true; }
        }
        bounds = default; return false;
    }

    private string _lastMapId;
    private int _rollCount;
    private System.Random _fixedRng;

    private void Awake()
    {
        if (Instance == null) Instance = this;
        else { Destroy(gameObject); return; }

        ValidatePool();
    }

    /// <summary>
    /// 启动自检：pool 里的 mapId 必须非空且唯一。
    /// 重复的 mapId 会让「按 id 同步」退化成「永远第一张」——
    /// 联机表现就是：房主每小局换图，客户端永远显示同一张。
    /// </summary>
    private void ValidatePool()
    {
        if (pool == null || pool.maps == null) return;

        var seen = new Dictionary<string, MapConfig>();
        foreach (var m in pool.maps)
        {
            if (m == null) continue;

            if (string.IsNullOrEmpty(m.mapId))
            {
                Debug.LogError($"[MapManager] 地图配置 {m.name} 的 mapId 为空，联机时无法同步，请填唯一 id。");
                continue;
            }

            if (seen.TryGetValue(m.mapId, out var first))
            {
                Debug.LogError($"[MapManager] mapId 重复：{first.name} 与 {m.name} 都是「{m.mapId}」。" +
                               "联机时客户端只会装载排在前面的那张，请改成唯一 id。");
                continue;
            }

            seen[m.mapId] = m;
        }
    }

    // 调用时机：选笔完成后（GameManager.BeginMatch）以及每小局开始时（StartNewRound）
    public void SelectAndApply()
    {
        var map = PickRandom();
        if (map == null)
        {
            Debug.LogWarning("[MapManager] 地图池为空或没有有效地图，保留当前地图。");
            return;
        }
        Apply(map);
    }

    /// <summary>
    /// 联机：客户端按房主下发的 mapId 装载**同一张**地图（不做任何随机）。
    /// 随机只发生在房主侧，客户端必须走这条确定性路径，否则各端地图不一致。
    /// </summary>
    public void ApplyById(string mapId)
    {
        if (pool == null || pool.maps == null)
        {
            Debug.LogWarning("[MapManager] 没有配置 MapPool，无法按 mapId 装载地图。");
            return;
        }

        var map = pool.maps.FirstOrDefault(m => m != null && m.mapId == mapId && m.mapPrefab != null);
        if (map == null)
        {
            var avail = string.Join(", ", pool.maps.Where(m => m != null).Select(m => m.mapId));
            Debug.LogError($"[MapManager] 找不到 mapId={mapId} 的地图（本端可选：{avail}）。检查客户端的 MapPool 是否与房主一致。");
            return;
        }

        // 已经是这张图就不用销毁重装（每小局房主都会下发一次 mapId）
        if (CurrentInstance != null && Current != null && Current.mapId == mapId) return;

        Apply(map);
        Debug.Log($"[MapManager] 客户端按房主下发装载地图：{map.displayName}({map.mapId})");
    }

    private MapConfig PickRandom()
    {
        if (pool == null || pool.maps == null) return null;

        var candidates = pool.maps.Where(m => m != null && m.mapPrefab != null).ToList();
        if (candidates.Count == 0) return null;
        if (candidates.Count == 1) return candidates[0];

        // 不允许连续重复
        if (avoidConsecutiveRepeat && _lastMapId != null)
        {
            var filtered = candidates.Where(m => m.mapId != _lastMapId).ToList();
            if (filtered.Count > 0) candidates = filtered;
        }

        // 按权重抽
        float total = 0f;
        foreach (var m in candidates) total += Mathf.Max(0.0001f, m.weight);

        float r = Next01() * total;
        float acc = 0f;
        for (int i = 0; i < candidates.Count; i++)
        {
            acc += Mathf.Max(0.0001f, candidates[i].weight);
            if (r <= acc) return candidates[i];
        }
        return candidates[candidates.Count - 1];
    }

    private float Next01()
    {
        if (seed == 0) return Random.value;
        if (_fixedRng == null) _fixedRng = new System.Random(seed);
        return (float)_fixedRng.NextDouble();
    }

    private void Apply(MapConfig map)
    {
        // 1) 卸掉上一张
        if (CurrentInstance != null) Destroy(CurrentInstance);

        // 2) 实例化新地图（地图 prefab 根节点请归零 (0,0,0)）
        CurrentInstance = Instantiate(map.mapPrefab, mapParent);
        CurrentInstance.name = "Map_" + map.mapId;
        CurrentInstance.SetActive(false);   // 先隐藏：选笔 / 开局强化阶段不该看到地图
        Current = map;
        _lastMapId = map.mapId;
        _rollCount++;

        // 3) 收集出生点（按 index 升序 → 对应 playerIndex）
        var markers = CurrentInstance.GetComponentsInChildren<SpawnPointMarker>(true)
                                     .OrderBy(m => m.index)
                                     .ToArray();
        SpawnPoints = markers.Select(m => m.transform).ToArray();

        if (SpawnPoints.Length == 0)
            Debug.LogWarning($"[MapManager] 地图 {map.mapId} 内没有 SpawnPointMarker，出生点将回退到 GameManager 的配置。");
        else if (logEachRoll)
            Debug.Log($"[MapManager] 第 {_rollCount} 次抽图 → {map.displayName}({map.mapId})，出生点 {SpawnPoints.Length} 个");
    }

    /// <summary>
    /// 把当前地图显示出来（正式开打时调用）。
    /// 刚装载的地图默认是隐藏的 —— 这样"选笔 / 开局强化"阶段看不到地图，
    /// 但出生点坐标仍然读得到（Transform 在隐藏状态下依然有效）。
    /// </summary>
    public void ShowMap()
    {
        if (CurrentInstance != null && !CurrentInstance.activeSelf)
        {
            CurrentInstance.SetActive(true);
            Debug.Log($"[MapManager] 显示地图 {Current?.displayName}({Current?.mapId})");
        }
    }
}
