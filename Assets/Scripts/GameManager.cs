using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using PenBattleGame.Core;

public class GameManager : MonoBehaviour
{
    public static GameManager Instance { get; private set; }

    [Header("玩家设置")]
    [Range(2, 4)]
    public int totalPlayers = 2;          // 2~4 人
    public List<PlayerData> players = new List<PlayerData>();

    [Header("回合限制")]
    public float maxActionTime = 30f;     // 每回合操作时间（秒）
    public float physicsWaitTime = 3f;    // 物理模拟等待上限（最长等待时间）

    [Header("放置设置")]
    public Transform[] spawnPoints;
    [Tooltip("自动放置的搜索半径")]
    public float spawnRadius = 3f;
    [Tooltip("放置高度（与桌面高度一致）")]
    public float spawnY = 0.4f;
    [Tooltip("自动放置的初始角度（0 = +Z 方向），会按随机角度旋转")]
    public float spawnStartAngle = 0f;
    [Tooltip("自动放置时与障碍物的最小安全距离，避免被挤住转不开")]
    public float minDistanceFromObstacle = 1f;

    // 回合状态
    private int _currentPlayerIndex = 0;
    private TurnPhase _phase = TurnPhase.Idle;
    private float _turnTimer = 0f;
    private bool _actionActive = false;  // 是否处于"已开始但未结束"的行动中，保证 ActionEndedEvent 只广播一次
    private int _turnNumber = 0;         // 该小局内的回合序号（一回合 = 每位存活玩家各行动一次）
    private int _roundNumber = 0;
    private int _aliveCount = 0;
    private float _resolvingTimer = 0f;   // Resolving 看门狗计时（协程被打断时兜底推进）
    private Vector3[] _obstaclePoints;   // 本回合障碍物位置缓存，用于随机摆位

    public enum TurnPhase
    {
        Idle,           // 等待开始
        Action,         // 当前玩家行动中
        Resolving,      // 物理模拟中（笔在飞）
        RoundEnd,       // 小局结束
        MatchEnd        // 比赛结束
    }

    // 事件总线（UI 监听）
    public event Action<PlayerData> OnActionStarted;
    public event Action<PlayerData> OnActionEnded;
    public event Action<PlayerData> OnPlayerEliminated;
    public event Action<PlayerData> OnRoundWinner;
    public event Action<PlayerData> OnMatchWinner;

    // ===== 只读信息 =====
    public int TotalPlayers => players.Count;   // PlayerData 数量，即存活玩家人数
    public TurnPhase Phase => _phase;
    public int RoundNumber => _roundNumber;

    private void Awake()
    {
        if (Instance == null)
            Instance = this;
        else
        {
            Destroy(gameObject);
            return;
        }

        EventBus.Instance.Subscribe<PenEliminatedEvent>(OnPenEliminated);
        EventBus.Instance.Subscribe<InputLaunchEvent>(OnPlayerAction);
    }

    private void Start()
    {
        // 从第 3 步起：不再自动开始，改为等待 MatchSetupManager 选好再调 BeginMatch()。
        // 要跳过选笔直接测试，就写一个测试脚本调 GameManager.Instance.BeginMatch(...)。
    }

    private void Update()
    {
        // 回合倒计时（时间到自动进入结算）—— 只有权威端计时，客户端只做显示
        if (IsAuthority && _phase == TurnPhase.Action)
        {
            _turnTimer -= Time.deltaTime;
            if (_turnTimer <= 0f)
            {
                Debug.Log($"{GetCurrentPlayer()?.playerName} 超时，跳过本回合");
                NextTurn();   // 回合结束的事件由 NextTurn 统一广播
            }
        }

        // 【看门狗】Resolving 正常由 WaitForPhysicsIdle 收尾。
        // 如果协程被打断（订阅者抛异常 / 物体被禁用 / StopAllCoroutines），_phase 会永远停在
        // Resolving —— 而超时倒计时只在 Action 阶段跑，永远救不回来。表现就是：
        // 房主行动完之后两端都点不动、也不再推进。
        // 这里兜一道底：超时就强制走下一回合。
        if (IsAuthority && _phase == TurnPhase.Resolving)
        {
            _resolvingTimer += Time.deltaTime;
            if (_resolvingTimer > physicsWaitTime + 2f)
            {
                Debug.LogError($"[回合] 物理结算卡住 {_resolvingTimer:F1}s（协程可能被打断），强制推进");
                _resolvingTimer = 0f;
                NextTurn();
            }
        }
    }

    // ========== 初始化 ==========

    public void BeginMatch(List<PlayerSetupData> setups)
    {
        // 联机时权威逻辑只跑在主机端，客户端不执行开局流程（数据靠同步）
        var netNM = Unity.Netcode.NetworkManager.Singleton;
        if (netNM != null && netNM.IsListening && !netNM.IsServer)
        {
            Debug.Log("[GameManager] 客户端不执行 BeginMatch（联机时开局逻辑只跑在主机端）");
            return;
        }

        if (setups == null || setups.Count == 0)
        {
            Debug.LogError("BeginMatch: setups 为空，无法开始对局");
            return;
        }
        totalPlayers = setups.Count;

        // 必须先选好笔再摆到桌上读地图出生点（要在 InitializePlayers 之前）
        // 笔的初始位置要在地图出生点确定之后才能算
        if (MapManager.Instance == null)
            Debug.LogError("[GameManager] 场景里找不到 MapManager，无法加载地图");
        else
            MapManager.Instance.SelectAndApply();

        InitializePlayers(setups);
        if (players.Count == 0)
        {
            Debug.LogError("BeginMatch: 没有成功创建任何笔，请检查各玩家的选笔配置与 PenConfig 的 modelPrefab 是否指向了 PenController 的笔预制体。");
            return;
        }

        // 开局强化：每位玩家先选 1 个，选完再开局
        StartUpgradePhase(true, StartMatch);
    }

    private void InitializePlayers(List<PlayerSetupData> setups)
    {
        players.Clear();

        for (int i = 0; i < setups.Count; i++)
        {
            var setup = setups[i];
            string id = setup.playerId;

            // 优先找场景里已经摆好的笔（便于关卡调试），找不到再动态创建
            GameObject penObj = GameObject.Find($"Pen_{id}");
            if (penObj == null)
            {
                GameObject prefab = setup.penConfig.modelPrefab;
                if (prefab == null)
                {
                    Debug.LogError($"GameManager: 找不到 Pen_{id}，请给 PenConfig 指定 modelPrefab，或检查场景");
                    continue;
                }
                penObj = Instantiate(prefab);
            }
            if (penObj == null)
            {
                Debug.LogError($"GameManager: Pen_{id} 实例化失败，请检查预制体");
                continue;
            }

            penObj.name = $"Pen_{id}";

            // 出生前先摆到当前地图的出生点（避免与原点处的障碍物碰撞）
            Vector3 initPos = Vector3.zero;
            var mm = MapManager.Instance;
            if (mm != null && mm.SpawnPoints != null && i < mm.SpawnPoints.Length && mm.SpawnPoints[i] != null)
            {
                initPos = mm.SpawnPoints[i].position;
            }
            else
            {
                // 兜底：否则可能回到 (0,0,0)，容易和其它笔在原点互撞
                int n = Mathf.Max(1, setups.Count);
                float ang = (360f / n) * i * Mathf.Deg2Rad;
                initPos = new Vector3(Mathf.Sin(ang) * 5f, 1f, Mathf.Cos(ang) * 5f);

                Debug.LogError($"GameManager: {id} 用不了这张地图的出生点（MapManager=" +
                               $"{(mm == null ? "null" : "正常")}, SpawnPoints={(mm?.SpawnPoints?.Length ?? -1)}），" +
                               $"已用半径 5 的圆兜底。请检查 MapManager 和 pool 里的地图 prefab 有没有 SpawnPointMarker。");
            }
            // PenController 可能挂在预制体的子物体上，模型层级复杂，用 GetComponentInChildren 找
            PenController pen = penObj.GetComponentInChildren<PenController>(true);
            if (pen == null)
            {
                Debug.LogError($"GameManager: 笔 Pen_{id}（它的根物体）上找不到 PenController。请检查预制体，确认它身上挂好了 PenController 以及 Rigidbody+Collider。");
                continue;
            }

            pen.penId = id;

            // 与回合开始的同步路径对齐：位置交给预制体，再走 ResetPen 直接设到出生位置
            pen.ResetPen(initPos);

            // 准备阶段：选中 / 放置强化前先把笔冻住，原因有两条：
            //   · 冻住后读图时它是静止的，否则笔会一直被压着和桌面上的其它笔互相推挤
            //   · 这样后面准备阶段就不用再额外处理
            // StartNewRound 与 ResetPen 会自动恢复成动态刚体并重新显示
            pen.SetKinematic(true);
            pen.SetVisible(false);

            // 血条：必须挂在"有 PenController 的那个物体"上。
            // 联机时 PenNetworkBridge.OnNetworkSpawn 也会补一次（两边都有 null 检查，不会重复）。
            if (pen.gameObject.GetComponent<PenHealthBar>() == null)
                pen.gameObject.AddComponent<PenHealthBar>();

            // 把出生点登记成出生数据（只在权威端做，摆好位置之后才 Spawn 登记）
            // 出生初始位姿和其它信息一起同步给客户端，客户端不做自己算。
            // 客户端不走这里（BeginMatch 会在客户端提前 return），数据由 NGO 自动送达。
            var netObj = penObj.GetComponent<Unity.Netcode.NetworkObject>();
            var penBridge = penObj.GetComponent<PenNetworkBridge>();
            var netNM = Unity.Netcode.NetworkManager.Singleton;

            if (netObj != null && netNM != null && netNM.IsListening && netNM.IsServer && !netObj.IsSpawned)
                netObj.Spawn();

            // 把"这支笔归谁"同步到网络了。
            // 客户端不用别人指定的 penId，靠同步过去的那支笔的预制体默认值。
            // IsMyTurn 判不出这支笔是不是自己的 —— 客户端什么都不选就行。
            if (penBridge != null && netNM != null && netNM.IsListening && netNM.IsServer)
            {
                penBridge.HostSetPenId(id);

                // 玩家也一样同步给客户端，客户端按 configId 在本地查同一个 PenConfig 取出来
                if (setup.penConfig != null)
                    penBridge.HostSetConfigId(setup.penConfig.configId);
            }

            // 应用开局所选笔的配置（物理参数 / 耐久 / 材质 / 物理材质）
            if (setup.penConfig != null) pen.ApplyConfig(setup.penConfig);

            // 有些笔自带被动技能，必须在 ApplyConfig 之后挂，否则会被配置覆盖成默认值
            PassiveSkillManager.AttachTo(pen, setup.penConfig);

            PlayerData player = new PlayerData
            {
                playerId = id,
                playerName = setup.playerName,
                playerIndex = i,
                pen = pen,
                penConfig = setup.penConfig,
                skills = setup.skills != null ? new List<SkillConfig>(setup.skills) : new List<SkillConfig>(),
                score = 0
            };
            players.Add(player);

            // 出生位置由 StartNewRound -> ResetForRound 统一摆放，此时 players 还没全部加入，避免空引用
        }
    }

    // ========== 出生点 ==========

    // 每小局开始前刷新障碍物位置缓存
    private void CacheObstacles()
    {
        var obs = FindObjectsOfType<ObstacleController>();
        _obstaclePoints = new Vector3[obs.Length];
        for (int i = 0; i < obs.Length; i++)
            _obstaclePoints[i] = obs[i].transform.position;
    }

    // 给指定玩家算出生点：手动指定的优先，其次自动均匀分布，并尽量避开障碍物
    public Vector3 GetSpawnPositionFor(int playerIndex)
    {
        // 1) 当前地图自带的出生点优先（每小局由 MapManager 加载）
        var mm = MapManager.Instance;
        if (mm != null && mm.SpawnPoints != null
            && playerIndex < mm.SpawnPoints.Length && mm.SpawnPoints[playerIndex] != null)
        {
            return mm.SpawnPoints[playerIndex].position;
        }

        // 2) 场景里手动指定的出生点（便于手动摆放）
        if (spawnPoints != null && playerIndex < spawnPoints.Length && spawnPoints[playerIndex] != null)
        {
            Vector3 p = spawnPoints[playerIndex].position;
            return p;
        }

        // 3) 自动在半径圆上均匀分布
        if (mm != null && mm.SpawnPoints != null && mm.SpawnPoints.Length > 0
            && playerIndex >= mm.SpawnPoints.Length)
        {
            Debug.LogWarning($"[出生点] 地图只能提供 {mm.SpawnPoints.Length} 个出生点，第 {playerIndex} 个人改用自动圆形出生点。");
        }

        EnsureObstacleCache();
        int count = Mathf.Max(1, TotalPlayers);
        float step = 360f / count;
        float phi = BestRotation(spawnStartAngle, step, count);
        return CirclePoint(spawnStartAngle + phi + playerIndex * step);
            
    }

    private Vector3 CirclePoint(float angleDeg)
    {
        float rad = angleDeg * Mathf.Deg2Rad;
        return new Vector3(Mathf.Sin(rad) * spawnRadius, spawnY, Mathf.Cos(rad) * spawnRadius);
    }

    // 按旋转偏移（每隔 15°）试选，使"所有玩家的出生点与障碍物距离最大"的那个角度
    private float BestRotation(float start, float stepDeg, int count)
    {
        if (_obstaclePoints == null || _obstaclePoints.Length == 0) return 0f;

        float bestScore = float.MinValue;
        float bestPhi = 0f;

        for (int phi = 0; phi < 360; phi += 15)
        {
            float worst = float.MaxValue;
            for (int k = 0; k < count; k++)
            {
                float d = MinDistanceToObstacles(CirclePoint(start + phi + k * stepDeg));
                if (d < worst) worst = d;
            }
            if (worst > bestScore)
            {
                bestScore = worst;
                bestPhi = phi;
            }
        }
        return bestPhi;
    }

    private float MinDistanceToObstacles(Vector3 p)
    {
        if (_obstaclePoints == null || _obstaclePoints.Length == 0) return float.MaxValue;
        float min = float.MaxValue;
        foreach (var o in _obstaclePoints)
        {
            float d = Vector3.Distance(new Vector3(p.x, 0, p.z), new Vector3(o.x, 0, o.z));
            if (d < min) min = d;
        }
        return min;
    }

    private void EnsureObstacleCache()
    {
        if (_obstaclePoints == null) CacheObstacles();
    }

    // ========== 小局流程 ==========

    public void StartMatch()
    {
        Debug.Log("比赛开始！");
        _roundNumber = 0;
        foreach (var p in players) p.score = 0;

        // 所有玩家初始化积分（全 0），方便重置
        var sync = NetworkMatchSync.Instance;
        if (sync != null)
            foreach (var p in players) sync.HostSetScore(p.playerIndex, 0);

        StartNewRound();
    }

    private void StartNewRound()
    {
        if (!IsAuthority) return;   // 联机时只有房主跑小局

        _roundNumber++;
        PushTurnState();
        Debug.Log($"\n=== 第 {_roundNumber} 小局 ===");

        // 每小局重新抽地图（第 1 小局是"选好的那张"，从第 2 小局起重新抽，自动跳过连续重复）
        // 必须在 ResetForRound（重置笔）之前做
        if (_roundNumber > 1)
        {
            if (MapManager.Instance == null)
                Debug.LogError("[GameManager] 场景里找不到 MapManager，无法重新抽地图");
            else
                MapManager.Instance.SelectAndApply();
        }

        // 【联机】每小局重抽地图后必须把 mapId 下发给客户端。
        // 之前只有开局那一张下发过（StartMatchOnHost），所以从第 2 小局起各端桌面就不一致了。
        // 位置必须在下面的 ShowMap 之前 —— ClientRpc 按序到达，先换图再显示。
        if (MapManager.Instance != null && MapManager.Instance.Current != null
            && NetworkMatchFlow.Instance != null)
            NetworkMatchFlow.Instance.HostApplyMap(MapManager.Instance.Current.mapId);

        // 地图加载时先隐藏 —— 等选笔 / 放置强化阶段结束再显示
        // 必须在 CacheObstacles 之前，否则 FindObjectsOfType 默认找不到未激活的物体
        if (MapManager.Instance != null)
        {
            MapManager.Instance.ShowMap();                                       // 自己端立即显示
            if (NetworkMatchFlow.Instance != null)
                NetworkMatchFlow.Instance.HostShowMap();                         // 广播给客户端
        }

        // 刷新障碍物位置缓存，供后面出生点随机摆位使用
        CacheObstacles();

        foreach (var p in players)
        {
            p.ResetForRound();
            // TODO 第5步：这里补上强化效果的 ResetForRound 响应
        }

        // ResetForRound 会按 isEliminated 重置状态，同时清空该人的淘汰标记，方便下个小局继续用
        NetworkMatchSync.Instance?.HostClearEliminated();

        // 笔的隐藏时机：准备阶段是不可见的，由 ResetPen 在需要时打开
        // 但这里已经先按"显示"广播给客户端了，所以自己端重复一次是无害的
        foreach (var p in players)
        {
            if (p == null || p.pen == null) continue;

            var penBridge = p.pen.GetComponent<PenNetworkBridge>();
            if (penBridge != null) penBridge.HostSetVisible(true);
        }

        _aliveCount = players.Count;
        _currentPlayerIndex = 0;
        _turnNumber = 0;

        var ids = players.Select(p => p.playerId).ToList();

        // 先通知"小局预备"，StatusEffectManager 等订用它，然后再正式开小局
        EventBus.Instance.Publish(new RoundPreStartEvent { roundNumber = _roundNumber, playerIds = ids });
        EventBus.Instance.Publish(new RoundStartEvent { roundNumber = _roundNumber, playerIds = ids });

        StartNewTurn();   // 开始本小局的第 1 个回合
        StartTurn();
    }

    // ========== 回合 / 行动流程 ==========

    // 开始一个"回合"（一回合 = 小局内每位存活玩家各行动一次）
    private void StartNewTurn()
    {
        _turnNumber++;

        // 同样兜异常 —— 它后面紧跟 PushTurnState/StartTurn，抛在这儿会让状态机停住
        try
        {
            EventBus.Instance.Publish(new TurnStartedEvent
            {
                roundNumber = _roundNumber,
                turnNumber = _turnNumber
            });
        }
        catch (Exception ex)
        {
            Debug.LogError($"[回合] TurnStartedEvent 订阅者抛异常（已跳过，回合继续推进）：{ex}");
        }

        PushTurnState();

        Debug.Log($"第 {_roundNumber} 小局 · 第 {_turnNumber} 回合开始 ——");
    }

    // 开始一位玩家的"行动"
    private void StartTurn()
    {
        var currentPlayer = GetCurrentPlayer();
        if (currentPlayer == null || currentPlayer.isEliminated)
        {
            NextTurn();
            return;
        }

        _phase = TurnPhase.Action;
        PushTurnState();                 // 把"该谁动 + 现在什么阶段"推给网络
        _turnTimer = maxActionTime;
        _actionActive = true;
        currentPlayer.hasPlacedThisRound = false;

        Debug.Log($"轮到 {currentPlayer.playerName} 行动 (笔: {currentPlayer.pen.penId})");

        OnActionStarted?.Invoke(currentPlayer);
        EventBus.Instance.Publish(new ActionStartedEvent
        {
            playerId = currentPlayer.playerId,
            timeLimit = maxActionTime
        });
    }

    private void NextTurn()
    {
        if (!IsAuthority) return;   // 联机时只有房主推进回合

        _resolvingTimer = 0f;

        // 一次"行动"结束可能来自：弹完 / 超时 / 行动者被淘汰 / 小局已结束 这几种情况，
        // 由 ActionEndedEvent 的监听方统一广播，保证所有订阅者都不会漏掉
        if (_actionActive)
        {
            _actionActive = false;

            // ⚠️ 订阅者里跑的是被动逻辑（冲击波推刚体、燃烧扣血、自动回复…），任何一处抛异常，
            //    异常都会从 Publish 冒出去，后面的 StartTurn() 就不执行了 —— _phase 永远停在
            //    Resolving，超时倒计时又只在 Action 阶段跑，整局直接死掉且无法自愈。
            //    宁可让某个被动不生效，也不能让回合推进断掉。
            try
            {
                EventBus.Instance.Publish(new ActionEndedEvent { playerId = GetCurrentPlayerId() });
            }
            catch (Exception ex)
            {
                Debug.LogError($"[回合] ActionEndedEvent 订阅者抛异常（已跳过，回合继续推进）：{ex}");
            }

            var ending = GetCurrentPlayer();
            if (ending != null)
            {
                try { OnActionEnded?.Invoke(ending); }
                catch (Exception ex) { Debug.LogError($"[回合] OnActionEnded 回调抛异常（已跳过）：{ex}"); }
            }
        }

        _aliveCount = players.Count(p => !p.isEliminated);
        if (_aliveCount <= 1)
        {
            EndRound();
            return;
        }

        int nextIndex = _currentPlayerIndex;
        int attempts = 0;
        do
        {
            nextIndex = (nextIndex + 1) % players.Count;
            attempts++;
            if (attempts > players.Count) break;
        } while (players[nextIndex].isEliminated);

        // 行动顺序绕回队首 = 每位存活玩家都行动过一次 = 一个"回合"结束
        bool turnWrapped = nextIndex <= _currentPlayerIndex;
        _currentPlayerIndex = nextIndex;

        if (turnWrapped)
        {
            // 先结束这一回合，再开新回合
            // 同样要兜异常：TurnEnded 是所有 DOT / 被动结算的入口，最容易在这里炸
            try
            {
                EventBus.Instance.Publish(new TurnEndedEvent
                {
                    roundNumber = _roundNumber,
                    turnNumber = _turnNumber
                });
            }
            catch (Exception ex)
            {
                Debug.LogError($"[回合] TurnEndedEvent 订阅者抛异常（已跳过，回合继续推进）：{ex}");
            }

            Debug.Log($"第 {_roundNumber} 小局 · 第 {_turnNumber} 回合结束（所有存活玩家各行动一次）——");

            StartNewTurn();
        }

        StartTurn();
    }

    public PlayerData GetCurrentPlayer()
    {
        if (players.Count == 0) return null;
        return players[_currentPlayerIndex];
    }

    public string GetCurrentPlayerId() => GetCurrentPlayer()?.playerId;

    // 取当前玩家的第 index 个技能（给 InputManager 判断要不要选目标）
    public SkillConfig GetCurrentPlayerSkill(int index)
    {
        var p = GetCurrentPlayer();
        if (p == null || p.skills == null || index < 0 || index >= p.skills.Count) return null;
        return p.skills[index];
    }

    /// <summary>
    /// 判断是否在"权威端"（主机 / 房主）
    /// 联机时**只有权威端推进对局**，回合状态、积分结算、淘汰判定全部在这里跑，
    /// 客户端只靠 NetworkMatchSync 同步过来的状态
    /// </summary>
    public bool IsAuthority
    {
        get
        {
            var nm = Unity.Netcode.NetworkManager.Singleton;
            return nm == null || !nm.IsListening || nm.IsServer;
        }
    }

    // penId 即 playerId（"Player1"），便于 InputManager / 网络层对齐
    public bool IsMyTurn(string playerId)
    {
        if (IsAuthority)
        {
            if (_phase != TurnPhase.Action) return false;
            var current = GetCurrentPlayer();
            return current != null && current.playerId == playerId;
        }

        // 客户端：本地 players 是空的，以同步过来的回合状态为准
        var sync = NetworkMatchSync.Instance;
        return sync != null && sync.IsActionPhase && sync.CurrentPlayerId == playerId;
    }

    /// 把本端算出来的回合状态推给下游。状态一改就调一次。
    private void PushTurnState()
    {
        var sync = NetworkMatchSync.Instance;
        if (sync == null || !sync.IsServer) return;

        sync.HostSetRound(_roundNumber);
        sync.HostSetTurn(_turnNumber);
        sync.HostSetCurrentPlayerIndex(_currentPlayerIndex);
        sync.HostSetPhase(_phase);

        // 联机排查用：这一段能直接看出"该谁动"在房主侧算成了什么
        Debug.Log($"[回合] 推送 → 小局 {_roundNumber} · 回合 {_turnNumber} · " +
                  $"Player{_currentPlayerIndex + 1} · {_phase}");
    }

    // ========== 响应玩家操作 ==========

    private void OnPlayerAction(InputLaunchEvent evt)
    {
        // 客户端不推进对局，它的操作都通过 ServerRpc 上报，由房主统一处理
        if (!IsAuthority) return;

        if (!IsMyTurn(evt.penId)) return;
        StartResolutionPhase();
    }

    // 所谓"放置"，本质也是一次行动（每回合限一次），靠 hasPlacedThisRound 控制
    public void PlayerPlacePen(string playerId, Vector3 targetPos)
    {
        if (!IsMyTurn(playerId)) return;

        // 如果是客户端：本地 players 是空的，改为上报给主机执行，主语仍旧要校验一次回合归属
        if (!IsAuthority)
        {
            NetworkMatchSync.Instance?.SubmitPlace(targetPos);
            Debug.Log($"[联机] 上报放置 → {targetPos}");
            return;
        }

        var player = players.Find(p => p.playerId == playerId);
        if (player == null || player.hasPlacedThisRound) return;
        if (player.pen == null) return;

        // 用刚体传送，autoSyncTransforms=0 时只写 transform.position 会被物理同步覆盖
        player.pen.TeleportTo(targetPos);
        player.hasPlacedThisRound = true;

        Debug.Log($"{player.playerName} 放置到 {targetPos}");

        // 放置即视为行动结束，进入物理结算阶段，给一点时间让物理微动
        StartResolutionPhase();
    }

    // 使用技能（旧接口，无目标）
    public void PlayerUseSkill(string playerId, int skillIndex)
        => PlayerUseSkill(playerId, skillIndex, SkillTargetContext.None);

    // 使用技能（给 InputManager 用，带 ctx 参数可选目标）
    public void PlayerUseSkill(string playerId, int skillIndex, SkillTargetContext ctx)
    {
        if (!IsMyTurn(playerId)) return;

        var player = GetCurrentPlayer();
        if (player == null) return;
        if (player.hasUsedSkillThisRound)
        {
            Debug.Log($"{player.playerName} 本回合已用过技能");
            return;
        }
        if (skillIndex < 0 || skillIndex >= player.skills.Count) return;

        var skill = player.skills[skillIndex];
        if (!player.skillUses.TryGetValue(skill.skillId, out int uses) || uses <= 0)
        {
            Debug.Log($"{skill.displayName} 次数已用完");
            return;
        }

        player.skillUses[skill.skillId] = uses - 1;
        player.hasUsedSkillThisRound = true;

        SkillSystem.Execute(skill, player, ctx);

        EventBus.Instance.Publish(new SkillUsedEvent { playerId = playerId, skillId = skill.skillId });
        Debug.Log($"{player.playerName} 使用技能 {skill.displayName}，剩余 {player.skillUses[skill.skillId]} 次");

        // 技能作为回合内的免费前置动作，用完仍需弹笔或放置才能结束回合
    }

    // ========== 物理结算阶段 ==========

    private void StartResolutionPhase()
    {
        if (!IsAuthority) return;               // 联机时只有房主跑物理结算
        if (_phase != TurnPhase.Action) return; // 防止重入 / 重复结算

        _phase = TurnPhase.Resolving;
        _resolvingTimer = 0f;
        PushTurnState();
        Debug.Log("物理模拟中...");
        StartCoroutine(WaitForPhysicsIdle());
    }

    private System.Collections.IEnumerator WaitForPhysicsIdle()
    {
        float elapsed = 0f;
        float checkInterval = 0.2f;

        while (elapsed < physicsWaitTime)
        {
            bool allIdle = true;
            foreach (var p in players)
            {
                if (p.isEliminated) continue;
                var rb = p.pen.GetRigidbody();
                if (rb == null) continue;
                if (rb.velocity.sqrMagnitude > 0.01f || rb.angularVelocity.sqrMagnitude > 0.01f)
                {
                    allIdle = false;
                    break;
                }
            }

            if (allIdle && elapsed > 0.5f) break;

            yield return new WaitForSeconds(checkInterval);
            elapsed += checkInterval;
        }

        Debug.Log("物理已停止，结束回合");

        // 看门狗（Update 里）可能已经抢先推进了 —— 此时 _phase 不再是 Resolving，
        // 这里必须直接退出，否则会把回合连着推两次，直接吞掉一名玩家的行动机会。
        if (_phase != TurnPhase.Resolving) yield break;

        // 统一走 NextTurn，避免跳过先广播回合结束、再决定是开下一回合还是开下个小局
        NextTurn();
    }

    // ========== 淘汰与结算 ==========

    private void OnPenEliminated(PenEliminatedEvent evt)
    {
        var player = players.Find(p => p.pen.penId == evt.penId);
        if (player == null || player.isEliminated) return;

        player.isEliminated = true;
        player.eliminationRank = players.Count(p => p.isEliminated);

        // 把这个人的淘汰状态推给网络
        NetworkMatchSync.Instance?.HostSetEliminated(player.playerIndex, true);

        Debug.Log($"{player.playerName} 被淘汰 (排名: {player.eliminationRank})");
        OnPlayerEliminated?.Invoke(player);

        // 如果淘汰的是当前行动者，就结束他的回合，事件由 NextTurn 广播
        if (GetCurrentPlayerId() == evt.penId && _phase == TurnPhase.Action)
        {
            NextTurn();
        }
    }

    private void EndRound()
    {
        _phase = TurnPhase.RoundEnd;
        // 【联机】阶段变化必须下发：否则客户端的 phase 会停在 Resolving，
        // 强化阶段 / 下一小局开始前，客户端 HUD 一直显示"物理结算"。
        PushTurnState();

        var winner = players.Find(p => !p.isEliminated);
        if (winner == null)
        {
            Debug.LogWarning("死人太多，平局，跳过判定胜负");
            winner = players[UnityEngine.Random.Range(0, players.Count)];
        }

        winner.score++;

        // 把本小局结果推给网络
        NetworkMatchSync.Instance?.HostSetScore(winner.playerIndex, winner.score);

        Debug.Log($"{winner.playerName} 赢得第 {_roundNumber} 小局，当前积分: {winner.score}");

        OnRoundWinner?.Invoke(winner);
        EventBus.Instance.Publish(new RoundEndEvent
        {
            roundNumber = _roundNumber,
            winnerId = winner.playerId,
            endReason = "Elimination"
        });

        if (winner.score >= 3)
        {
            EndMatch(winner);
            return;
        }

        // 强化：小局败者强化。每位败者各选 1 个，所以这里先弹所有人共享的选项，再逐个开界面
        StartUpgradePhase(false, StartNewRound);
    }

    // ========== 强化阶段 ==========

    // 开局强化(beforeMatch = true)：所有玩家各选 1 个
    // 败者强化(beforeMatch = false)：小局败者玩家各选 1 个
    private void StartUpgradePhase(bool beforeMatch, Action onComplete)
    {
        var eligible = beforeMatch
            ? new List<PlayerData>(players)
            : players.FindAll(p => p.isEliminated);

        if (eligible.Count == 0)
        {
            onComplete?.Invoke();
            return;
        }

        UpgradePhaseManager.Instance.StartPhase(eligible, 1, beforeMatch, onComplete);
    }

    private void EndMatch(PlayerData winner)
    {
        _phase = TurnPhase.MatchEnd;
        PushTurnState();   // 【联机】同上：比赛结束也要让客户端知道
        Debug.Log($"{winner.playerName} 获得最终胜利，总积分 {winner.score} 分");
        OnMatchWinner?.Invoke(winner);
        // TODO 第6步：可以在此广播比赛结束事件
    }

    private void OnDestroy()
    {
        EventBus.Instance.Unsubscribe<PenEliminatedEvent>(OnPenEliminated);
        EventBus.Instance.Unsubscribe<InputLaunchEvent>(OnPlayerAction);
    }
}
