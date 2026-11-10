using System.Collections.Generic;
using UnityEngine;

[CreateAssetMenu(fileName = "PenCfg", menuName = "PenBattle/Pen Config")]
public class PenConfig : ScriptableObject
{
    [Header("标识")]
    public string configId = "pen_default";
    public string displayName = "默认笔";
    public Sprite icon;

    [Header("模型/材质")]
    public GameObject modelPrefab;   // 可选：将来做模型替换，本步暂不使用
    public Material penMaterial;

    [Header("物理参数")]
    public float mass = 1f;
    public float bounciness = 0.1f;    // 真实笔 ~0.05~0.15
    public float friction = 0.3f;      // 真实笔 ~0.25~0.35
    public float linearDrag = 0.03f;   // 线性阻尼（原来错用了 friction，现在分开）
    public float angularDrag = 0.15f;
    public float maxAngularVelocity = 25f;

    [Header("发射力")]
    [Tooltip("发射力倍率。重笔可以设成 2，即同等蓄力下打出去的力量是其它笔的两倍")]
    public float launchForceMultiplier = 1f;

    [Header("耐久")]
    public float maxStatusDurability = 100f;

    [Header("技能（技能随笔走；当前每笔 1 个，用 List 为将来扩展预留）")]
    public List<SkillConfig> activeSkills = new List<SkillConfig>();

    [Header("被动技能（同样随笔走；直接在下面编辑，不需要单独做资产）")]
    public List<PassiveSkillEntry> passiveSkills = new List<PassiveSkillEntry>();

    [Header("专属强化（只有选了这支笔的玩家才可能抽到）")]
    [Tooltip("选择强化时，会与通用强化池合并后随机抽 3 个给玩家选")]
    public List<UpgradeEntry> exclusiveUpgrades = new List<UpgradeEntry>();
}
