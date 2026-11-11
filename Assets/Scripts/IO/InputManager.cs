using PenBattleGame.Core;
using UnityEngine;

public class InputManager : MonoBehaviour
{
    [Header("相机")]
    public Camera mainCamera;

    [Header("层级掩码")]
    public LayerMask penLayerMask;      // 笔的层级
    [Tooltip("桌面碰撞体层级；留空则用水平面近似")]
    public LayerMask deskLayerMask;

    [Header("蓄力参数")]
    public float maxLaunchForce = 10f;      // 满力度时的冲量（质量1时≈10m/s）
    public float powerSensitivity = 2f;     // 拖拽距离 → 力度倍率

    [Header("放置参数")]
    public KeyCode togglePlaceKey = KeyCode.P; // 切换 弹笔/放置 模式
    public float placeY = 0.4f;                // 放置高度（与出生点一致）

    // ---------- 模式 ----------
    private enum OperationMode { Launch, Place, Targeting }
    private OperationMode _mode = OperationMode.Launch;

    // ---------- 目标选择状态 ----------
    private SkillConfig _pendingSkill = null;
    private int _pendingSkillIndex = -1;
    private PlayerData _hoverTarget = null;

    // ---------- 弹笔状态机 ----------
    private enum InputState
    {
        Idle,           // 空闲，等待点选笔
        PenSelected,    // 已选中笔，等待在笔身上按下鼠标开始蓄力
        Charging        // 蓄力中，等待松开
    }
    private InputState _state = InputState.Idle;

    private PenController _selectedPen;
    private Vector3 _clickPointOnPen;
    private Vector3 _mouseDownWorldPos;
    private float _currentPower = 0f;

    // 可视化
    private LineRenderer _tractionLine;
    private GameObject _placeMarker;

    /// 本端是否是权威端（房主 / 单机）。联机时客户端只上报、不本地执行。
    private bool IsAuthority => GameManager.Instance == null || GameManager.Instance.IsAuthority;

    private void Awake()
    {
        if (mainCamera == null) mainCamera = Camera.main;
        CreateVisuals();
    }

    private void CreateVisuals()
    {
        GameObject lineObj = new GameObject("TractionLine");
        lineObj.transform.SetParent(transform);
        _tractionLine = lineObj.AddComponent<LineRenderer>();
        _tractionLine.startWidth = 0.06f;
        _tractionLine.endWidth = 0.06f;
        _tractionLine.material = new Material(Shader.Find("Sprites/Default"));
        _tractionLine.startColor = Color.white;
        _tractionLine.endColor = Color.red;
        _tractionLine.positionCount = 2;
        _tractionLine.enabled = false;

        // 放置模式落点指示（扁圆盘）
        _placeMarker = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        _placeMarker.name = "PlaceMarker";
        _placeMarker.transform.localScale = new Vector3(0.8f, 0.01f, 0.8f);
        var markerRenderer = _placeMarker.GetComponent<Renderer>();
        markerRenderer.material = new Material(Shader.Find("Sprites/Default"))
        {
            color = new Color(0.2f, 0.8f, 1f, 0.6f)
        };
        Destroy(_placeMarker.GetComponent<Collider>()); // 不能挡住射线
        _placeMarker.SetActive(false);
    }

    private void Update()
    {
        // 【回合守卫】不是我的回合 → 立刻把本地输入状态清干净。
        //
        // 为什么必须在这里清：Update 是按 _state / _mode 分派的，
        //   _state = PenSelected / Charging 时只会走 HandlePenSelected / HandleCharging，
        //   _mode = Place / Targeting 时只会走 HandlePlace / HandleTargeting，
        //   —— 这些分支都不会重新执行"选笔"（HandleIdle 才做）。
        // 如果回合在玩家按下鼠标的瞬间切走，状态就会残留到下一回合：
        //   轮到自己时点笔没反应（走的是 Charging/Targeting 分支，不是 Idle），
        //   表现就是"点不动笔"，而且只有按右键或 P 才解得开。
        // 联机时回合切换不经过本地事件（客户端的 EventBus 收不到房主的 TurnStarted），
        // 所以只能每帧问一次 IsMyTurn —— 也顺便兜住了超时、淘汰、小局结束这些路径。
        if (_state != InputState.Idle || _mode != OperationMode.Launch || _pendingSkillIndex >= 0)
        {
            var turnGm = GameManager.Instance;
            if (turnGm != null && !turnGm.IsMyTurn(LocalPlayerId()))
            {
                Debug.Log("[操作] 回合已不属于本端，清理残留输入状态");
                CancelAction();
            }
        }

        // P 键切换 弹笔/放置 模式
        if (Input.GetKeyDown(togglePlaceKey))
        {
            _mode = _mode == OperationMode.Launch ? OperationMode.Place : OperationMode.Launch;
            Debug.Log($"操作模式切换为：{_mode}");
            CancelAction();
        }

        // 技能快捷键 1/2/3（仅在弹笔模式且自己的回合）
        if (_mode == OperationMode.Launch)
        {
            for (int i = 0; i < 3; i++)
            {
                if (Input.GetKeyDown(KeyCode.Alpha1 + i)) BeginSkill(i);
            }
        }

        if (_mode == OperationMode.Place)
        {
            HandlePlace();
        }
        else if (_mode == OperationMode.Targeting)
        {
            HandleTargeting();
        }
        else
        {
            switch (_state)
            {
                case InputState.Idle: HandleIdle(); break;
                case InputState.PenSelected: HandlePenSelected(); break;
                case InputState.Charging: HandleCharging(); break;
            }
        }

        // 右键取消（放置模式下同时退出放置模式）
        if (Input.GetMouseButtonDown(1))
        {
            CancelAction();
        }
    }

    // ========== 放置模式 ==========

    private void HandlePlace()
    {
        // 落点指示跟随鼠标
        if (TryGetPlacePosition(out Vector3 hoverPos))
        {
            _placeMarker.SetActive(true);
            _placeMarker.transform.position = hoverPos;
        }
        else
        {
            _placeMarker.SetActive(false);
        }

        if (!Input.GetMouseButtonDown(0)) return;

        var gm = GameManager.Instance;
        if (gm == null) return;

        // 回合校验
        string playerId = LocalPlayerId();   // 联机时是"我的席位"，不是"当前玩家"（否则房主会替客户端放置）
        if (!gm.IsMyTurn(playerId))
        {
            Debug.Log("现在不是你的行动回合，不能放置");
            return;
        }

        if (!TryGetPlacePosition(out Vector3 pos)) return;

        gm.PlayerPlacePen(playerId, pos);   // 放置成功后自动进入物理结算、切下一回合
        CancelAction();                      // 回到弹笔模式
    }

    private bool TryGetPlacePosition(out Vector3 pos)
    {
        pos = Vector3.zero;
        Ray ray = mainCamera.ScreenPointToRay(Input.mousePosition);

        if (deskLayerMask.value != 0 && Physics.Raycast(ray, out RaycastHit hit, 100f, deskLayerMask))
        {
            pos = hit.point;
        }
        else
        {
            Plane plane = new Plane(Vector3.up, new Vector3(0f, placeY, 0f));
            if (!plane.Raycast(ray, out float enter)) return false;
            pos = ray.GetPoint(enter);
        }

        pos.y = placeY;
        return true;
    }

    // ========== 技能与目标选择 ==========

    /// 客户端：把技能用**可序列化的原始参数**上报房主（PlayerData / SkillTargetContext 不过网）
    private void SubmitSkillToHost(int skillIndex, int targetPlayerIndex, Vector3 direction)
    {
        NetworkMatchSync.Instance?.SubmitSkill(skillIndex, targetPlayerIndex, direction);
        Debug.Log($"[联机] 上报技能 #{skillIndex}（目标 playerIndex={targetPlayerIndex}）");
        CancelAction();
    }

    private void BeginSkill(int index)
    {
        var gm = GameManager.Instance;
        if (gm == null) return;

        // ===== 联机客户端：本地 players 是空的，技能表要用"自己选的那支笔"来查 =====
        if (!IsAuthority)
        {
            var flow = NetworkMatchFlow.Instance;
            if (flow == null || !gm.IsMyTurn(flow.MyPlayerId))
            {
                Debug.Log("现在不是你的行动回合");
                return;
            }

            var cfg = flow.MyPenConfig;
            if (cfg == null || cfg.activeSkills == null || index < 0 || index >= cfg.activeSkills.Count) return;

            var clientSkill = cfg.activeSkills[index];

            if (clientSkill.targetMode == SkillTargetMode.Self ||
                clientSkill.targetMode == SkillTargetMode.AllEnemies ||
                clientSkill.targetMode == SkillTargetMode.AllPens)
            {
                SubmitSkillToHost(index, -1, Vector3.zero);
                return;
            }

            _pendingSkill = clientSkill;
            _pendingSkillIndex = index;
            _mode = OperationMode.Targeting;
            DeselectPen();
            if (clientSkill.targetMode == SkillTargetMode.Direction) _tractionLine.enabled = true;

            Debug.Log($"{clientSkill.displayName}：请选择目标（右键取消）");
            return;
        }

        string pid = LocalPlayerId();   // 联机时是"我的席位"，不是"当前玩家"
        if (!gm.IsMyTurn(pid))
        {
            Debug.Log("现在不是你的行动回合");
            return;
        }

        var skill = gm.GetCurrentPlayerSkill(index);
        if (skill == null) return;

        // 无需选择目标的类型：直接释放
        if (skill.targetMode == SkillTargetMode.Self ||
            skill.targetMode == SkillTargetMode.AllEnemies ||
            skill.targetMode == SkillTargetMode.AllPens)
        {
            gm.PlayerUseSkill(pid, index, SkillTargetContext.None);
            return;
        }

        // 需要选择目标：进入 Targeting 模式
        _pendingSkill = skill;
        _pendingSkillIndex = index;
        _mode = OperationMode.Targeting;
        DeselectPen();
        if (skill.targetMode == SkillTargetMode.Direction) _tractionLine.enabled = true;

        Debug.Log($"{skill.displayName}：请选择目标（右键取消）");
    }

    private void HandleTargeting()
    {
        if (_pendingSkill == null) { CancelAction(); return; }

        switch (_pendingSkill.targetMode)
        {
            case SkillTargetMode.EnemySingle: HandleTargetEnemySingle(); break;
            case SkillTargetMode.Direction: HandleTargetDirection(); break;
            default: CancelAction(); break;
        }
    }

    // 点选一只敌方笔
    // 从 "Player2" 解析出 playerIndex（1）
    private static bool TryParsePlayerIndex(string playerId, out int index)
    {
        index = -1;
        const string prefix = "Player";
        if (string.IsNullOrEmpty(playerId) || !playerId.StartsWith(prefix)) return false;
        if (!int.TryParse(playerId.Substring(prefix.Length), out int n) || n <= 0) return false;

        index = n - 1;
        return true;
    }

    private void HandleTargetEnemySingle()
    {
        var gm = GameManager.Instance;
        if (gm == null) return;

        // ===== 联机客户端：用笔身上同步过来的 penId 判断目标，不需要服务端的 PlayerData =====
        if (!IsAuthority)
        {
            var flow = NetworkMatchFlow.Instance;
            if (flow == null) return;

            if (Physics.Raycast(mainCamera.ScreenPointToRay(Input.mousePosition),
                                out RaycastHit clientHit, 100f, penLayerMask))
            {
                var clientPen = clientHit.collider.GetComponentInParent<PenController>();

                // 高亮悬停的敌方笔（对着自己的笔不高亮）
                string myId = flow.MyPlayerId;
                bool isEnemy = clientPen != null
                            && !string.IsNullOrEmpty(clientPen.penId)
                            && clientPen.penId != myId;
                HighlightPenOnClient(isEnemy ? clientPen : null);

                if (Input.GetMouseButtonDown(0))
                {
                    if (clientPen == null || string.IsNullOrEmpty(clientPen.penId) ||
                        clientPen.penId == flow.MyPlayerId)
                    {
                        Debug.Log("请选择一只敌方笔");
                        return;
                    }

                    if (!TryParsePlayerIndex(clientPen.penId, out int targetIndex))
                    {
                        Debug.Log("目标无法识别（penId 未同步？）");
                        return;
                    }

                    SubmitSkillToHost(_pendingSkillIndex, targetIndex, Vector3.zero);
                }
            }
            return;
        }

        if (Physics.Raycast(mainCamera.ScreenPointToRay(Input.mousePosition), out RaycastHit hit, 100f, penLayerMask))
        {
            var pen = hit.collider.GetComponentInParent<PenController>();
            var target = FindPlayerByPen(pen);
            HighlightTarget(target);

            if (Input.GetMouseButtonDown(0))
            {
                if (target != null && !target.isEliminated && target.playerId != gm.GetCurrentPlayerId())
                    ConfirmSkill(SkillTargetContext.ForPlayer(target));
                else
                    Debug.Log("请选择一只敌方笔");
            }
        }
        else
        {
            HighlightTarget(null);
        }
    }

    // 拖拽指定释放方向
    private void HandleTargetDirection()
    {
        Vector3 center = GetMyPenCenter();
        Vector3 current = GetMouseWorldPosition();

        _tractionLine.SetPosition(0, new Vector3(center.x, center.y + 0.05f, center.z));
        _tractionLine.SetPosition(1, new Vector3(current.x, center.y + 0.05f, current.z));

        if (Input.GetMouseButtonUp(0))
        {
            Vector3 dir = new Vector3(center.x - current.x, 0f, center.z - current.z);
            if (dir.sqrMagnitude < 0.001f) { Debug.Log("方向太短，取消"); CancelAction(); return; }
            ConfirmSkill(SkillTargetContext.ForDirection(dir));
        }
    }

    private void ConfirmSkill(SkillTargetContext ctx)
    {
        // 客户端：不能走 PlayerUseSkill（它要 playerId，而客户端 players 是空的），
        // 直接把"方向"这个可序列化参数上报房主。
        if (!IsAuthority)
        {
            SubmitSkillToHost(_pendingSkillIndex, -1, ctx.direction);
            return;
        }

        var gm = GameManager.Instance;
        if (gm != null) gm.PlayerUseSkill(LocalPlayerId(), _pendingSkillIndex, ctx);
        CancelAction();
    }

    private Vector3 GetMyPenCenter()
    {
        var gm = GameManager.Instance;
        var cur = gm != null ? gm.GetCurrentPlayer() : null;
        if (cur != null && cur.pen != null) return cur.pen.transform.position;

        // 联机客户端：本地 players 是空的，改用"我这支笔"（靠同步过来的 penId 认领）
        var flow = NetworkMatchFlow.Instance;
        if (flow != null)
        {
            string myId = flow.MyPlayerId;
            foreach (var pen in FindObjectsOfType<PenController>())
                if (pen != null && pen.penId == myId) return pen.transform.position;
        }

        return Vector3.zero;
    }

    private PlayerData FindPlayerByPen(PenController pen)
    {
        if (pen == null || GameManager.Instance == null) return null;
        return GameManager.Instance.players.Find(p => p.pen == pen);
    }

    private void HighlightTarget(PlayerData p)
    {
        if (_hoverTarget == p) return;
        if (_hoverTarget != null && _hoverTarget.pen != null) _hoverTarget.pen.DeselectPen();
        _hoverTarget = p;
        if (_hoverTarget != null && _hoverTarget.pen != null) _hoverTarget.pen.SelectPen();
    }

    // ===== 联机客户端专用：本地没有 PlayerData，直接对笔对象做高亮 =====
    private PenController _clientHoverPen;

    private void HighlightPenOnClient(PenController pen)
    {
        if (_clientHoverPen == pen) return;

        if (_clientHoverPen != null) _clientHoverPen.DeselectPen();
        _clientHoverPen = pen;
        if (_clientHoverPen != null) _clientHoverPen.SelectPen();
    }

    // ========== 弹笔模式 ==========

    private void HandleIdle()
    {
        if (!Input.GetMouseButtonDown(0)) return;

        if (RaycastPen(out PenController pen, out _))
        {
            SelectPen(pen);
        }
    }

    private void HandlePenSelected()
    {
        if (!Input.GetMouseButtonDown(0)) return;

        if (RaycastPen(out PenController pen, out Vector3 hitPoint))
        {
            if (pen == _selectedPen)
            {
                StartCharging(hitPoint);
            }
            else
            {
                DeselectPen();
                SelectPen(pen);
                StartCharging(hitPoint);
            }
        }
    }

    private void HandleCharging()
    {
        // ① 笔已经不该由我操作了（被淘汰 / 回合被切走）→ 立即复位，别让状态残留
        if (_selectedPen == null || !CanOperate(_selectedPen))
        {
            DeselectPen();
            return;
        }

        // ② 松开 → 发射
        if (Input.GetMouseButtonUp(0))
        {
            LaunchPen();
            return;
        }

        // ③ 【自愈】Charging 的前提是鼠标一直按着。
        //    GetMouseButtonUp 在某些情况下会**丢**：Game 窗口失焦（切到 Console / 浏览器 /
        //    Inspector）、Editor 暂停后恢复、Alt+Tab 出去又回来。
        //    一旦丢了，这里就永远等不到松开 → 状态死在 Charging，而 Update 的分派只会走
        //    HandleCharging，永远回不到 HandleIdle（选笔）→ 表现就是"点笔完全没反应"，
        //    只有按右键才解得开。这里用"当前是否按着"兜一道底。
        //    ⚠️ 顺序必须在 GetMouseButtonUp 之后：松开的那一帧 GetMouseButton 也是 false。
        if (!Input.GetMouseButton(0))
        {
            Debug.Log("[操作] Charging 状态下鼠标已经松开了（松开事件丢失），复位到 Idle");
            DeselectPen();
            return;
        }

        Vector3 currentWorldPos = GetMouseWorldPosition();
        float dragDistance = Vector3.Distance(currentWorldPos, _mouseDownWorldPos);
        _currentPower = Mathf.Clamp(dragDistance * powerSensitivity, 0f, maxLaunchForce);

        UpdateTractionLine(currentWorldPos, _currentPower);

        EventBus.Instance.Publish(new PowerUpdateEvent
        {
            penId = _selectedPen.penId,
            currentPower = _currentPower,
            maxPower = maxLaunchForce,
            normalizedPower = _currentPower / maxLaunchForce
        });
    }

    /// <summary>
    /// 能不能操作这支笔？
    /// 必须**同时**满足：① 这支笔是**我的** ② 现在是**我的回合**。
    /// 只校验"笔的主人 == 当前玩家"是不够的 —— 那样房主在客户端回合会去操作客户端的笔。
    /// </summary>
    private bool CanOperate(PenController pen)
    {
        if (pen == null) return false;

        var gm = GameManager.Instance;
        if (gm == null) return true;

        var flow = NetworkMatchFlow.Instance;

        // 单机（没有联机席位概念）：沿用"当前回合玩家的笔可操作"
        if (flow == null) return gm.IsMyTurn(pen.penId);

        // 联机：笔必须是我的，而且必须轮到我的回合
        string myId = flow.MyPlayerId;

        if (string.IsNullOrEmpty(myId))
        {
            LogOperateBlocked("本端还没分到席位（MyPlayerId 为空）—— 检查 NetworkMatchFlow 的 Roster 是否同步到了");
            return false;
        }

        if (string.IsNullOrEmpty(pen.penId))
        {
            LogOperateBlocked($"{pen.name} 的 penId 还没同步过来（PenNetworkBridge 的 _penId 是空的）");
            return false;
        }

        if (pen.penId != myId)
        {
            LogOperateBlocked($"这支笔不是我的：pen={pen.penId} / my={myId}");
            return false;
        }

        if (!gm.IsMyTurn(myId))
        {
            var sync = NetworkMatchSync.Instance;
            string phaseName = sync != null ? ((GameManager.TurnPhase)sync.Phase).ToString() : "无 sync";
            string currentId = sync != null ? (sync.CurrentPlayerId ?? "-") : "-";
            string index = sync != null ? sync.CurrentPlayerIndex.ToString() : "-";

            LogOperateBlocked($"还没轮到我：my={myId}，phase={phaseName}，current={currentId}，index={index}");
            return false;
        }

        _lastOperateBlockReason = null;
        return true;
    }

    // 只在拦截原因变化时打一次 —— CanOperate 每次点击都会走，直接 Debug.Log 会刷屏
    private string _lastOperateBlockReason;

    private void LogOperateBlocked(string reason)
    {
        if (_lastOperateBlockReason == reason) return;
        _lastOperateBlockReason = reason;
        Debug.LogWarning($"[操作被拒] {reason}");
    }

    /// <summary>
    /// 本端玩家的 id。**联机时必须用"我的席位"**，不能用 `GetCurrentPlayerId()` ——
    /// 后者返回的是"当前该行动的人"，在房主上就是客户端那个玩家，会导致房主替客户端操作。
    /// 单机没有席位概念，才回退到当前玩家。
    /// </summary>
    private string LocalPlayerId()
    {
        var flow = NetworkMatchFlow.Instance;
        if (flow != null && !string.IsNullOrEmpty(flow.MyPlayerId)) return flow.MyPlayerId;

        var gm = GameManager.Instance;
        return gm != null ? gm.GetCurrentPlayerId() : null;
    }

    private bool RaycastPen(out PenController pen, out Vector3 hitPoint)
    {
        pen = null;
        hitPoint = Vector3.zero;
        Ray ray = mainCamera.ScreenPointToRay(Input.mousePosition);

        if (!Physics.Raycast(ray, out RaycastHit hit, 100f, penLayerMask))
        {
            // 最常见的原因：笔不在鼠标下面（位置没同步过来 / 被隐藏 / layer 配错）
            LogOperateBlocked($"射线没打到任何笔（layerMask={penLayerMask.value}）");
            return false;
        }

        PenController target = hit.collider.GetComponentInParent<PenController>();

        if (target == null)
        {
            LogOperateBlocked($"射线打到了 {hit.collider.name}，但它身上没有 PenController");
            return false;
        }

        if (!target.isActive)
        {
            LogOperateBlocked($"{target.name}（penId={target.penId}）的 isActive=false");
            return false;
        }

        // ===== 操作权校验：既要"轮到我的回合"，也要"这支笔是我的" =====
        if (!CanOperate(target))
            return false;

        pen = target;
        hitPoint = hit.point;
        return true;
    }

    private void SelectPen(PenController pen)
    {
        _selectedPen = pen;
        _selectedPen.SelectPen();
        _state = InputState.PenSelected;

        EventBus.Instance.Publish(new PenSelectedEvent { penId = pen.penId });
        Debug.Log($"选中笔: {pen.penId}");
    }

    private void DeselectPen()
    {
        if (_selectedPen != null)
        {
            _selectedPen.DeselectPen();
            _selectedPen = null;
        }
        _tractionLine.enabled = false;
        _state = InputState.Idle;
    }

    private void StartCharging(Vector3 hitPoint)
    {
        _clickPointOnPen = hitPoint;
        _mouseDownWorldPos = GetMouseWorldPosition();

        _state = InputState.Charging;
        _currentPower = 0f;

        _tractionLine.enabled = true;
        UpdateTractionLine(_mouseDownWorldPos, 0f);

        EventBus.Instance.Publish(new PowerStartEvent { penId = _selectedPen.penId });
        Debug.Log($"开始蓄力，施力点: {_clickPointOnPen}");
    }

    private void UpdateTractionLine(Vector3 currentPos, float power)
    {
        Vector3 start = _clickPointOnPen + Vector3.up * 0.05f;
        Vector3 end = new Vector3(currentPos.x, start.y, currentPos.z);

        _tractionLine.SetPosition(0, start);
        _tractionLine.SetPosition(1, end);

        float t = Mathf.Clamp01(power / maxLaunchForce);
        _tractionLine.startColor = Color.Lerp(Color.white, Color.red, t);
        _tractionLine.endColor = Color.Lerp(Color.white, Color.yellow, t);
    }

    private void LaunchPen()
    {
        if (_selectedPen == null) return;

        Vector3 mousePos = GetMouseWorldPosition();
        Vector3 forcePoint = _clickPointOnPen;

        Vector3 direction = new Vector3(forcePoint.x - mousePos.x, 0f, forcePoint.z - mousePos.z);

        if (direction.sqrMagnitude < 0.001f)
            direction = _selectedPen.transform.forward;

        direction.Normalize();

        // 联机（客户端）：不本地执行，上报房主由房主统一结算。
        // 这里刻意**不发** InputLaunchEvent —— 客户端不推进对局，避免本地也跑回合机。
        if (!IsAuthority)
        {
            NetworkMatchSync.Instance?.SubmitLaunch(direction, _currentPower, _clickPointOnPen);
            Debug.Log($"[联机] 上报弹笔 {_selectedPen.penId}，力度 {_currentPower:F2}");

            _selectedPen.DeselectPen();
            _selectedPen = null;
            _tractionLine.enabled = false;
            _state = InputState.Idle;
            return;
        }

        // 房主 / 单机：本地执行（路径与原来完全一致）
        // 先广播"即将发射"，让被动（如[势能]）能在 Launch 之前加成
        EventBus.Instance.Publish(new InputLaunchEvent
        {
            penId = _selectedPen.penId,
            launchDirection = direction,
            launchForce = _currentPower
        });

        _selectedPen.Launch(direction, _currentPower, _clickPointOnPen);

        Debug.Log($"发射 {_selectedPen.penId}，力度 {_currentPower:F2}，方向 {direction}");

        _selectedPen.DeselectPen();
        _selectedPen = null;
        _tractionLine.enabled = false;
        _state = InputState.Idle;
    }

    private void CancelAction()
    {
        _mode = OperationMode.Launch; // 取消/右键后回到弹笔模式
        _pendingSkill = null;
        _pendingSkillIndex = -1;
        HighlightTarget(null);
        HighlightPenOnClient(null);   // 联机客户端的高亮是另一条路径，这里一并清掉
        DeselectPen();
        _tractionLine.enabled = false;
        if (_placeMarker != null) _placeMarker.SetActive(false);
        _state = InputState.Idle;
    }

    private Vector3 GetMouseWorldPosition()
    {
        Ray ray = mainCamera.ScreenPointToRay(Input.mousePosition);
        Plane groundPlane = new Plane(Vector3.up, Vector3.zero);
        if (groundPlane.Raycast(ray, out float enter))
        {
            return ray.GetPoint(enter);
        }
        return mainCamera.ScreenToWorldPoint(new Vector3(Input.mousePosition.x, Input.mousePosition.y, 10f));
    }

    private void OnDestroy()
    {
        if (_tractionLine != null) Destroy(_tractionLine.gameObject);
        if (_placeMarker != null) Destroy(_placeMarker);
    }
}
