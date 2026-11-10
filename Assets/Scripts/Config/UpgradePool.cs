using System.Collections.Generic;
using UnityEngine;

// 通用强化池：所有玩家都会抽到的那部分候选
// 放一份在 Assets/Resources/Upgrades/UpgradePool.asset。
// 每支笔的专属强化写在 PenConfig.exclusiveUpgrades 里；
// 抽取时 = 通用池 + 该玩家所选笔的专属池，合并后随机取 3 个。
//
// 之后要加新强化：① 需要新效果就先在 UpgradeEffectType 加枚举 + UpgradeSystem 里加分支；
//                ② 然后直接往这里（或 PenConfig 的专属列表）加一条即可，不用改流程代码。
[CreateAssetMenu(menuName = "PenBattle/Upgrade Pool", fileName = "UpgradePool")]
public class UpgradePool : ScriptableObject
{
    [Header("通用强化（所有人共用）")]
    public List<UpgradeEntry> commonUpgrades = new List<UpgradeEntry>();

    private static UpgradePool _cached;

    public static UpgradePool Load()
    {
        if (_cached == null)
            _cached = Resources.Load<UpgradePool>("Upgrades/UpgradePool");

        if (_cached == null)
            Debug.LogWarning("[强化] 找不到 Resources/Upgrades/UpgradePool 资产，跳过强化阶段");

        return _cached;
    }
}
