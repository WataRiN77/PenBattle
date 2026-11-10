using System.Collections.Generic;

[System.Serializable]
public class PlayerSetupData
{
    public string playerId;
    public string playerName;
    public PenConfig penConfig;
    public List<SkillConfig> skills = new List<SkillConfig>();
}
