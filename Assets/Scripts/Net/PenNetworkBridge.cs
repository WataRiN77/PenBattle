using System.Linq;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;

/// 挂在笔预制体上（与 PenController 同一物体）。
///
/// 职责：
///   1. **非权威端 kinematic** —— 位置交给 NetworkTransform，本地不跑物理。
///      （Physics.autoSyncTransforms = 0 时，客户端若仍是动态刚体，物理会覆盖同步进来的位置。）
///   2. **归属 penId 同步** —— 客户端要知道"哪支笔是谁的"。
///   3. **可见性同步**（默认隐藏）—— 准备阶段（选笔 / 开局强化）各端都看不到笔。
///   4. **外观同步**：按 configId 查本地同一份 PenConfig 取材质（各笔颜色不同）。
///   5. 显示/隐藏只关 **Renderer**，绝不用 SetActive(false)（会被判定为 despawn）。
///
/// 6. **状态效果同步（表现层镜像）** —— 燃烧层数 / 冰冻剩余回合 / 粘液层数。
///    客户端不跑玩法逻辑：镜像出来的 StatusEffect 都带 `visualOnly`，只负责把层数喂给
///    `BurnVisual` / `FreezeVisual`；伤害走 _durability，物理参数只在房主侧生效。
///    （视觉效果靠 `GetComponentInParent<BurnEffect>()` 取层数，所以"让客户端也有组件"
///     比"给视觉再开一条数据通道"改动小得多。）
///
/// 7. **血条分母同步** —— 客户端不跑 ApplyConfig / ApplyAll，本地的耐久上限停在预制体默认值，
///    光同步"掉血"会让两边血条比例永远对不上（分子一样分母不同）。这里补一条纯数据通道
///    `_maxDurability`。体型（Size 强化）反而不需要额外通道 —— 预制体的 NetworkTransform
///    已开 SyncScaleX/Y/Z，localScale 会自动同步。
///    **仍然不挂任何被动组件** —— 被动 OnEnable 会订阅 9 个事件，客户端挂上就真的会执行玩法逻辑
///    （点燃扣血、冲击波推刚体、粘液改摩擦），和"客户端不参与计算"直接冲突。
[RequireComponent(typeof(NetworkObject))]
public class PenNetworkBridge : NetworkBehaviour
{
    // 0 = 隐藏，1 = 显示（用 int 而非 bool：NGO 对数值类型最稳）
    private readonly NetworkVariable<int> _visible = new NetworkVariable<int>(
        0, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    // 这支笔归谁（"Player1"…）
    private readonly NetworkVariable<FixedString32Bytes> _penId = new NetworkVariable<FixedString32Bytes>(
        default, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    // 这支笔的配置 id（决定材质/颜色）。客户端按它在自己本地查同一份 PenConfig。
    private readonly NetworkVariable<FixedString32Bytes> _configId = new NetworkVariable<FixedString32Bytes>(
        default, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    // 当前状态耐久（血条用）：房主轮询权威值写入，各端读取。
    // 客户端不跑玩法逻辑，自己并不知道掉没掉血 —— 必须同步。
    // 初始 -1 =「还没同步过」（0 是合法值，不能拿 0 当哨兵，否则真被打到 0 时会被误判成"没数据"）。
    private readonly NetworkVariable<float> _durability = new NetworkVariable<float>(
        -1f, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    // 耐久上限（血条**分母**）：客户端不跑 ApplyAll，本地的 maxStatusDurability 停在预制体默认值。
    // 房主点了「耐久上限 +20%」之后，分子一样分母不同 → 两边血条比例永远对不上。
    // 0 =「还没同步过」。
    private readonly NetworkVariable<float> _maxDurability = new NetworkVariable<float>(
        0f, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    // 笔的缩放（体型强化）**不需要单独通道**：
    // 笔预制体的 NetworkTransform 已开 SyncScaleX/Y/Z，RefreshModifiedStats 写进 localScale
    // 的结果由它自动同步过去；客户端 ResetPen 改写 localScale 也不会回流到房主（非权威端不上报）。
    // 千万别再手写一条 _scale 通道 —— 会和 NetworkTransform 抢着写同一个值。

    // ---- 状态效果：只同步"看得见"的那部分（层数 / 剩余回合）----
    // 伤害由 _durability 负责，物理参数只在房主侧起作用，客户端不需要也不该重算。
    private readonly NetworkVariable<int> _burnStacks = new NetworkVariable<int>(
        0, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    private readonly NetworkVariable<int> _slimeStacks = new NetworkVariable<int>(
        0, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    // 0 = 没被冻住；>0 = 剩余回合
    private readonly NetworkVariable<int> _freezeTurns = new NetworkVariable<int>(
        0, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    private readonly NetworkVariable<float> _freezeMult = new NetworkVariable<float>(
        0.5f, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    /// <summary>血条读这个。联机时是同步值；单机（无桥接）时血条回退读 PenController。
    /// 返回负数表示还没收到过同步值。</summary>
    public float Durability => _durability.Value;

    /// <summary>血条分母（耐久上限）。0 表示还没收到过同步值。</summary>
    public float MaxDurability => _maxDurability.Value;

    private Rigidbody _rb;
    private PenController _pen;

    private float _durabilityPollTimer;
    private const float DurabilityPollInterval = 0.15f;   // 掉血是低频事件，0.15s 足够

    private void Awake()
    {
        _rb = GetComponent<Rigidbody>();
        _pen = GetComponent<PenController>();
    }

    public override void OnNetworkSpawn()
    {
        // ① 客户端只跟随同步，不本地模拟
        if (!IsServer && _rb != null)
        {
            _rb.isKinematic = true;
            Debug.Log($"[联机] {name}：客户端侧设为运动学（只跟随同步，不本地模拟）");
        }

        _penId.OnValueChanged += OnPenIdChanged;
        _configId.OnValueChanged += OnConfigIdChanged;
        _visible.OnValueChanged += OnVisibleChanged;
        _burnStacks.OnValueChanged += OnBurnStacksChanged;
        _slimeStacks.OnValueChanged += OnSlimeStacksChanged;
        _freezeTurns.OnValueChanged += OnFreezeChanged;

        ApplyPenId();
        ApplyConfigVisual();
        ApplyVisibility();

        // 晚进来的客户端不会收到"初始值变化"回调，必须主动建一次镜像
        ApplyStatusMirror();

        // 血条：这里自动挂上 —— 这样不用手改预制体，房主和客户端都会生成
        if (GetComponent<PenHealthBar>() == null) gameObject.AddComponent<PenHealthBar>();

        // 房主：立刻写入初始值，避免各端在第一次轮询前读到哨兵值
        if (IsServer && _pen != null)
        {
            _durability.Value = _pen.StatusDurability;
            _maxDurability.Value = _pen.maxStatusDurability;
        }
    }

    /// <summary>房主侧：轮询耐久变化并同步出去</summary>
    private void Update()
    {
        if (!IsServer || _pen == null) return;

        _durabilityPollTimer -= Time.deltaTime;
        if (_durabilityPollTimer > 0f) return;
        _durabilityPollTimer = DurabilityPollInterval;

        if (!Mathf.Approximately(_durability.Value, _pen.StatusDurability))
            _durability.Value = _pen.StatusDurability;

        // 血条分母：耐久上限会被「耐久强化 / 坚硬」改动，客户端自己算不出来
        if (!Mathf.Approximately(_maxDurability.Value, _pen.maxStatusDurability))
            _maxDurability.Value = _pen.maxStatusDurability;

        PollStatusEffects();
    }

    /// <summary>
    /// 房主侧：把当前状态效果的层数/剩余回合写进 NetworkVariable（有变化才写）。
    /// 轮询而不是在每个施加点埋 hook —— 状态的来源太多（技能/被动/强化/地图），
    /// 漏埋一处就不同步，轮询天然全覆盖。
    /// </summary>
    private void PollStatusEffects()
    {
        var burn = _pen.GetComponent<BurnEffect>();
        int burnStacks = burn != null && burn.stacks > 0 ? burn.stacks : 0;
        if (_burnStacks.Value != burnStacks) _burnStacks.Value = burnStacks;

        var slime = _pen.GetComponent<SlimeEffect>();
        int slimeStacks = slime != null && slime.stacks > 0 ? slime.stacks : 0;
        if (_slimeStacks.Value != slimeStacks) _slimeStacks.Value = slimeStacks;

        var freeze = _pen.GetComponent<FreezeEffect>();
        int turnsLeft = freeze != null ? freeze.RemainingTurns : 0;
        if (_freezeTurns.Value != turnsLeft) _freezeTurns.Value = turnsLeft;
        if (turnsLeft > 0 && freeze != null &&
            !Mathf.Approximately(_freezeMult.Value, freeze.frictionMultiplier))
            _freezeMult.Value = freeze.frictionMultiplier;
    }

    public override void OnNetworkDespawn()
    {
        _penId.OnValueChanged -= OnPenIdChanged;
        _configId.OnValueChanged -= OnConfigIdChanged;
        _visible.OnValueChanged -= OnVisibleChanged;
        _burnStacks.OnValueChanged -= OnBurnStacksChanged;
        _slimeStacks.OnValueChanged -= OnSlimeStacksChanged;
        _freezeTurns.OnValueChanged -= OnFreezeChanged;
    }

    // ================= 归属 =================

    private void OnPenIdChanged(FixedString32Bytes _, FixedString32Bytes __) => ApplyPenId();

    private void ApplyPenId()
    {
        if (_pen == null) return;

        string id = _penId.Value.ToString();
        if (!string.IsNullOrEmpty(id) && _pen.penId != id)
        {
            _pen.penId = id;
            if (!IsServer) Debug.Log($"[联机] {name}：归属同步为 {id}");
        }
    }

    public void HostSetPenId(string playerId)
    {
        if (!IsServer) return;
        _penId.Value = playerId;
        ApplyPenId();
    }

    // ================= 外观：材质 =================

    private void OnConfigIdChanged(FixedString32Bytes _, FixedString32Bytes __) => ApplyConfigVisual();

    private void ApplyConfigVisual()
    {
        if (_pen == null) return;

        string cfgId = _configId.Value.ToString();
        if (string.IsNullOrEmpty(cfgId)) return;

        // 两端资源一致（同一个 Resources/Pens），按 configId 查本地同一份配置
        var cfg = Resources.LoadAll<PenConfig>("Pens").FirstOrDefault(c => c != null && c.configId == cfgId);
        if (cfg == null)
        {
            Debug.LogWarning($"[联机] 本地找不到 configId={cfgId} 的 PenConfig，笔的外观不会同步。");
            return;
        }

        _pen.SetPenMaterial(cfg.penMaterial);
        if (!IsServer) Debug.Log($"[联机] {name}：外观同步为 {cfg.displayName}");
    }

    public void HostSetConfigId(string configId)
    {
        if (!IsServer) return;
        _configId.Value = configId;
    }

    // ================= 可见性 =================

    private void OnVisibleChanged(int _, int __) => ApplyVisibility();

    private void ApplyVisibility()
    {
        if (_pen != null) _pen.SetVisible(_visible.Value != 0);
    }

    public void HostSetVisible(bool visible)
    {
        if (!IsServer) return;

        _visible.Value = visible ? 1 : 0;
        ApplyVisibility();
    }

    // ================= 状态效果：客户端镜像 =================

    private void OnBurnStacksChanged(int _, int __) => ApplyStatusMirror();
    private void OnSlimeStacksChanged(int _, int __) => ApplyStatusMirror();
    private void OnFreezeChanged(int _, int __) => ApplyStatusMirror();

    /// <summary>
    /// 客户端：按同步值把状态效果的**表现**重建出来。
    /// 只在客户端跑 —— 房主身上本来就是真效果，再 AddComponent 会打架。
    /// </summary>
    private void ApplyStatusMirror()
    {
        if (IsServer || _pen == null) return;

        MirrorBurn(_burnStacks.Value);
        MirrorSlime(_slimeStacks.Value);
        MirrorFreeze(_freezeTurns.Value, _freezeMult.Value);
    }

    private void MirrorBurn(int stacks)
    {
        var burn = _pen.GetComponent<BurnEffect>();

        if (stacks <= 0)
        {
            if (burn != null) burn.RemoveEffect();   // 自带视觉清理
            return;
        }

        if (burn == null) burn = StatusEffectManager.AddEffect<BurnEffect>(_pen);
        if (burn == null) return;

        // visualOnly 必须在 Start 之前设：AddComponent 只同步跑 Awake，Start 在下一帧
        burn.visualOnly = true;
        burn.SetStacks(stacks);
    }

    private void MirrorSlime(int stacks)
    {
        var slime = _pen.GetComponent<SlimeEffect>();

        if (stacks <= 0)
        {
            if (slime != null) slime.RemoveEffect();
            return;
        }

        if (slime == null) slime = StatusEffectManager.AddEffect<SlimeEffect>(_pen);
        if (slime == null) return;

        slime.visualOnly = true;
        slime.SetStacks(stacks);
    }

    private void MirrorFreeze(int turnsLeft, float frictionMult)
    {
        var freeze = _pen.GetComponent<FreezeEffect>();

        if (turnsLeft <= 0)
        {
            if (freeze != null) freeze.RemoveEffect();
            return;
        }

        if (freeze == null) freeze = StatusEffectManager.AddEffect<FreezeEffect>(_pen);
        if (freeze == null) return;

        freeze.ApplyMirror(turnsLeft, frictionMult);
    }
}
