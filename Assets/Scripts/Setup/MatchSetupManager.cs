using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using PenBattleGame.Core;

public class MatchSetupManager : MonoBehaviour
{
    [Header("房间配置")]
    [Range(2, 4)] public int playerCount = 2;

    [Header("联机")]
    [Tooltip("勾上则强制启用本地选笔流程（单机调试用）。" +
             "默认：场景里存在 NetworkManager 时自动停用，改用 NetworkMatchFlow")]
    public bool forceOfflineSetup = false;

    private PenConfig[] _penConfigs;

    private readonly List<PlayerSetupData> _setups = new List<PlayerSetupData>();
    private int _currentIndex = 0;
    private PenConfig _selectedPen;
    private bool _finished = false;
    private GUIStyle _titleStyle;

    private void Start()
    {
        // 判断依据是「场景里有没有 NetworkManager」，**不能**用 NetworkManager.IsListening ——
        // Start() 在场景加载时就跑了，那时玩家还没点 Host/Client，IsListening 恒为 false，
        // 守卫会失效（本地选笔 UI 一直挂着）。
        // NetworkManager.Singleton 在它的 Awake 里赋值，而 Start 必在所有 Awake 之后，所以这里可靠。
        bool hasNetworkManager = Unity.Netcode.NetworkManager.Singleton != null;

        if (hasNetworkManager && !forceOfflineSetup)
        {
            Debug.Log("[选笔] 场景里有 NetworkManager → 走联机选笔（NetworkMatchFlow），本地选笔流程停用。");
            enabled = false;
            return;
        }

        LoadConfigs();
        EventBus.Instance.Publish(new MatchSetupStartEvent { totalPlayers = playerCount });
    }

    private void LoadConfigs()
    {
        _penConfigs = Resources.LoadAll<PenConfig>("Pens");

        if (_penConfigs == null || _penConfigs.Length == 0)
        {
            Debug.LogWarning("MatchSetupManager: Resources/Pens 下没有 PenConfig，使用运行时默认笔");
            _penConfigs = new PenConfig[] { CreateDefaultPen() };
        }
    }

    private static PenConfig CreateDefaultPen()
    {
        var cfg = ScriptableObject.CreateInstance<PenConfig>();
        cfg.configId = "pen_default";
        cfg.displayName = "默认笔";
        return cfg;
    }

    private void OnGUI()
    {
        if (_finished) return;
        if (_titleStyle == null)
            _titleStyle = new GUIStyle(GUI.skin.label) { fontSize = 22, fontStyle = FontStyle.Bold };

        float w = 470f;
        float h = Mathf.Min(Screen.height - 60f, 560f);
        Rect area = new Rect(Screen.width * 0.5f - w * 0.5f, 30f, w, h);

        GUILayout.BeginArea(area, GUI.skin.box);
        GUILayout.Label($"玩家 {_currentIndex + 1} / {playerCount}：选择你的笔", _titleStyle);
        GUILayout.Space(6);
        GUILayout.Label("技能随笔走，选笔即选技能");

        GUILayout.Space(8);
        foreach (var cfg in _penConfigs)
        {
            string prefix = cfg == _selectedPen ? "[√] " : "      ";
            if (GUILayout.Button(prefix + cfg.displayName, GUILayout.Height(28)))
                _selectedPen = cfg;

            // 选中时展开这支笔自带的技能说明
            if (cfg == _selectedPen)
            {
                if (cfg.activeSkills == null || cfg.activeSkills.Count == 0)
                {
                    GUILayout.Label("        技能：无");
                }
                else
                {
                    foreach (var s in cfg.activeSkills)
                        GUILayout.Label($"        主动 · {s.displayName}（{s.targetMode}）：{s.description}");
                }

                if (cfg.passiveSkills != null && cfg.passiveSkills.Count > 0)
                {
                    foreach (var p in cfg.passiveSkills)
                        GUILayout.Label($"        被动 · {p.DisplayName}：{p.Describe()}");
                }
            }
        }

        GUILayout.Space(10);
        GUI.enabled = _selectedPen != null;
        if (GUILayout.Button("确认选择", GUILayout.Height(40))) OnConfirm();
        GUI.enabled = true;
        GUILayout.EndArea();
    }

    private void OnConfirm()
    {
        var setup = new PlayerSetupData
        {
            playerId = $"Player{_currentIndex + 1}",
            playerName = $"玩家{_currentIndex + 1}",
            penConfig = _selectedPen,
            // 技能直接取自笔的配置（技能随笔走）
            skills = _selectedPen.activeSkills != null
                     ? new List<SkillConfig>(_selectedPen.activeSkills)
                     : new List<SkillConfig>()
        };
        _setups.Add(setup);

        EventBus.Instance.Publish(new PlayerSetupConfirmedEvent
        {
            playerId = setup.playerId,
            penConfigId = setup.penConfig.configId,
            skillIds = setup.skills.Select(s => s.skillId).ToList()
        });

        _currentIndex++;
        if (_currentIndex >= playerCount)
        {
            CompleteSetup();
        }
        else
        {
            _selectedPen = null;
        }
    }

    private void CompleteSetup()
    {
        _finished = true;
        EventBus.Instance.Publish(new MatchSetupCompleteEvent { playerIds = _setups.Select(s => s.playerId).ToList() });
        Debug.Log("选笔完成，开始比赛");
        if (GameManager.Instance != null) GameManager.Instance.BeginMatch(_setups);
        Destroy(gameObject);
    }
}
