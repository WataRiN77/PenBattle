using UnityEngine;

public enum SkillType
{
    Boost,    // 自身：下次发射力 +value（0.5 = +50%）
    Bounce,   // 自身：弹性覆盖为 value
    Speed,    // 自身：速度倍率 value
    Burn      // 目标：施加燃烧（配合 targetMode 决定打谁）
}

[CreateAssetMenu(fileName = "SkillCfg", menuName = "PenBattle/Skill Config")]
public class SkillConfig : ScriptableObject
{
    [Header("标识")]
    public string skillId = "skill_default";
    public string displayName = "技能";
    public Sprite icon;
    [TextArea] public string description = "技能说明";

    [Header("效果")]
    public SkillType type = SkillType.Boost;
    public float value = 0.5f;          // 含义随 type 而异，见 SkillType 注释

    [Header("目标（决定释放前要不要选目标）")]
    public SkillTargetMode targetMode = SkillTargetMode.Self;

    [Header("使用限制")]
    public int usesPerRound = 1;        // 每小局每人最多可用次数
}
