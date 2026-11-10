using UnityEngine;

/// 挂在每张地图的出生点物体上；MapManager 收集后按 index 升序排列。
/// index 对应 GameManager 里的 playerIndex（0 起）。
public class SpawnPointMarker : MonoBehaviour
{
    [Tooltip("出生点序号，从 0 开始；对应玩家索引 playerIndex")]
    public int index = 0;
}
