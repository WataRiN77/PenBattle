using System.Collections.Generic;
using UnityEngine;
using PenBattleGame.Core;

[RequireComponent(typeof(Rigidbody), typeof(Collider))]
public class PenController : MonoBehaviour
{
    [Header("PenPhysicsParams")]
    public float mass = 1f;
    public float bounciness = 0.1f;
    public float friction = 0.3f;
    public float linearDrag = 0.03f;   // 线性阻尼（原来错用了 friction，现在分开）
    public float angularDrag = 0.15f;
    [HideInInspector] public float speedMultiplier = 1f;
    public float maxAngularVelocity = 25f;

    [Header("Flags")]
    public string penId = "Player1";
    public bool isActive = true;

    [Tooltip("表现层可见性（联机准备阶段各端隐藏笔；只关 Renderer，不 SetActive）")]
    public bool isVisible = true;

    [Header("Spawn")]
    public bool randomSpawnRotation = true;                // 出生时是否随机旋转
    public Vector2 spawnRotationYRange = new Vector2(0f, 360f); // 随机 Y 轴旋转角度范围，单位度

    [Header("Skill 临时加成")]
    public float pendingForceBonus = 0f;   // Boost 技能：下次发射力 +value（0.5 = +50%）

    [Header("状态影响")]
    [Tooltip("减速倍率：已改为只影响摩擦力，用于冰冻 / 粘液等减速效果")]
    public float slowMultiplier = 1f;

    [Header("发射力度增益")]
    [Tooltip("发射力倍率，来自 PenConfig（重笔 = 2）")]
    public float launchForceMultiplier = 1f;

    [Header("强化力度加成 / 只来自强化系统的效果")]
    [Tooltip("所有强化力度加成累加后驻留在这里")]
    public float upgradeForceMultiplier = 1f;

    [Header("Max HP")]
    public float maxStatusDurability = 100;
    private float _currentStatusDurability;

    /// <summary>
    /// 当前状态耐久（血条读取用）。
    /// ⚠ 联机时**只有房主端是权威值** —— 客户端的笔不跑玩法逻辑，
    ///   血条要走 `PenNetworkBridge.Durability`（同步过来的值）。
    /// </summary>
    public float StatusDurability => _currentStatusDurability;

    // readonly data
    private Rigidbody _rb;
    private Collider _col;
    private Vector3 _initialPosition;

    // cache
    private float _bouncinessOverride = -1f;
    private float _speedMultiplierOverride = 1f;
    private string _physMatName = "Pen_pm";

    // ===== 修饰量（Modifier）：加算 + 乘算 两条独立通道 =====
    // 约定：任何系统（强化 / 被动 / 状态效果）都**不直接改** mass、friction 等基准字段，
    //       而是改这里的 xxxAdd / xxxMult，然后调 RefreshModifiedStats() 落地。
    //
    //       最终值 = (基准 + 加算和) × 乘算积
    //         xxxAdd  —— 绝对修正，初值 0，多个来源相加（例：质量 +2 点 → massAdd += 2）
    //         xxxMult —— 比例修正，初值 1，多个来源相乘（例：质量 +15% → massMult *= 1.15）
    //
    //       每小局开始 RestoreBaseValues() 统一把 Add 归 0、Mult 归 1。
    // 基准字段（mass/bounciness/friction/...）只由 ApplyConfig 从 PenConfig 写入，运行期视为只读。
    // ⚠️ 加算先于乘算：先修基础值，再按比例放大。若某系统想"先乘后加"，需要另开通道，不要混用。

    // --- 质量 ---
    [Tooltip("质量加算：绝对点数（+2 = 质量 +2）")]
    [HideInInspector] public float massAdd = 0f;
    [Tooltip("质量乘算：1 = 不变，1.15 = +15%")]
    [HideInInspector] public float massMult = 1f;

    // --- 弹性 ---
    [Tooltip("弹性加算：绝对量（+0.1）")]
    [HideInInspector] public float bouncinessAdd = 0f;
    [Tooltip("弹性乘算")]
    [HideInInspector] public float bouncinessMult = 1f;

    // --- 摩擦 ---
    [Tooltip("摩擦加算：绝对量（-0.05 = 更滑）")]
    [HideInInspector] public float frictionAdd = 0f;
    [Tooltip("摩擦乘算：强化[Glide] / 被动[顺滑]（永久层）")]
    [HideInInspector] public float frictionMult = 1f;
    [Tooltip("状态层摩擦乘算：[冰冻]等可解除的效果（与 frictionMult 分开，解除时归 1 即可）")]
    [HideInInspector] public float statusFrictionMult = 1f;

    // --- 线性阻尼 ---
    [Tooltip("线性阻尼加算")]
    [HideInInspector] public float linearDragAdd = 0f;
    [Tooltip("线性阻尼乘算")]
    [HideInInspector] public float linearDragMult = 1f;

    // --- 角阻尼 ---
    [Tooltip("角阻尼加算")]
    [HideInInspector] public float angularDragAdd = 0f;
    [Tooltip("角阻尼乘算")]
    [HideInInspector] public float angularDragMult = 1f;

    // --- 体型 ---
    [Tooltip("体型加算：绝对值，三个轴各加这么多（0.2 = 尺寸 +0.2）")]
    [HideInInspector] public float scaleAdd = 0f;
    [Tooltip("体型乘算：1.2 = 体型 +20%")]
    [HideInInspector] public float scaleMult = 1f;

    // --- 耐久上限 ---
    // maxStatusDurability 字段保留为"有效上限"：RefreshModifiedStats 会把
    //   (基准 + durabilityAdd) × durabilityMult 回写进去，
    // 所以血条、自动回复、回复事件等所有读取点都不用改。
    [Tooltip("耐久上限加算：绝对点数（40 = +40）")]
    [HideInInspector] public float durabilityAdd = 0f;
    [Tooltip("耐久上限乘算：1.2 = +20%")]
    [HideInInspector] public float durabilityMult = 1f;

    // ===== 有效值：物理同步、材质重建、伤害计算一律读这些，不要直接读基准字段 =====
    public float EffectiveMass        => Mathf.Max(0.05f, (mass + massAdd) * massMult);
    public float EffectiveBounciness  => Mathf.Clamp01((bounciness + bouncinessAdd) * bouncinessMult);
    public float EffectiveFriction    => Mathf.Max(0.02f, (friction + frictionAdd) * frictionMult * statusFrictionMult);
    public float EffectiveLinearDrag  => Mathf.Max(0f, (linearDrag + linearDragAdd) * linearDragMult);
    public float EffectiveAngularDrag => Mathf.Max(0f, (angularDrag + angularDragAdd) * angularDragMult);
    public Vector3 EffectiveScale     => Vector3.Max(Vector3.one * 0.1f, (_baseScale + Vector3.one * scaleAdd) * scaleMult);

    // 强化基准值缓存：[Durability]强化是绝对加法（AddMaxDurability），靠这两个值每小局还原
    private float _baseMaxDurability = 100f;
    private Vector3 _baseScale = Vector3.one;


    // 同一段接触会去重：一次碰撞上可能挂多个 Collider，Unity 会按 Collider 重复回调 OnCollisionEnter，
    // 不去重的话同一次撞击会重复广播事件、重复扣一次耐久、障碍物会被重复扣血。
    private readonly Dictionary<string, float> _lastContactTime = new Dictionary<string, float>();
    private const float ContactDebounce = 0.1f;

    [Header("Visual")]
    public Material selectedMaterial;   // 选中时的高亮材质
    public Material defaultMaterial;    // 默认材质

    private Renderer _renderer;
    private Material _originalMaterial;

    private void Awake()
    {
        _rb = GetComponent<Rigidbody>();
        _col = GetComponent<Collider>();
        _initialPosition = transform.position;

        _rb.mass = EffectiveMass;
        _rb.drag = EffectiveLinearDrag;
        _rb.angularDrag = EffectiveAngularDrag;
        _rb.maxAngularVelocity = maxAngularVelocity;

        _currentStatusDurability = maxStatusDurability;

        _renderer = GetComponent<Renderer>();
        if (_renderer != null) _originalMaterial = _renderer.material;

        CaptureBase();  // 记录基准值（ApplyConfig 之后会再刷新一次）
        RefreshModifiedStats();
    }
    private void Start()
    {
        // 这里留作初始化钩子，暂时为空

    }

    public Rigidbody GetRigidbody() => _rb;

    /// <summary>
    /// 选中笔（高亮）
    /// </summary>
    public void SelectPen()
    {
        if (_renderer != null && selectedMaterial != null)
            _renderer.material = selectedMaterial;

        // 这里加选中的特效（待补）
    }

    /// <summary>
    /// 取消选中
    /// </summary>
    public void DeselectPen()
    {
        if (_renderer != null && _originalMaterial != null)
            _renderer.material = _originalMaterial;
    }

    public void ReduceStatusDurability(float amount, string effectSource)
    {
        if (!isActive) return;

        _currentStatusDurability -= amount;
        Debug.Log($"{penId} 状态耐久 -{amount}，剩余 {_currentStatusDurability}，来源：{effectSource}");

        EventBus.Instance.Publish(new PenDamagedEvent
        {
            penId = penId,
            amount = amount,
            remainingDurability = _currentStatusDurability,
            source = effectSource
        });

        if (_currentStatusDurability <= 0)
        {
            EventBus.Instance.Publish(new PenEliminatedEvent
            {
                penId = this.penId,
                killerId = effectSource,
                eliminationReason = effectSource
            });
        }
    }

    // 抬高耐久上限，并把当前耐久一起抬上去（配合[坚硬]用）
    // 抬高耐久上限：走 durabilityAdd 通道，由 Refresh 重算上限并把当前耐久一起抬（语义：新增的上限是满的）
    public void AddMaxDurability(float amount)
    {
        if (amount <= 0f) return;
        durabilityAdd += amount;
        RefreshModifiedStats();
    }

    // 回复耐久（被动[自动回复]、涅槃、回复类强化/技能都用它）
    // 不会超过耐久上限；返回**实际**回复量（已满或已淘汰时返回 0），并广播 PenHealedEvent
    // source 用来标识来源，监听方据此区分是哪套系统回的血（也用于防止递归自触发）
    public float RestoreStatusDurability(float amount, string source = "Unknown")
    {
        if (!isActive || amount <= 0f) return 0f;

        float before = _currentStatusDurability;
        _currentStatusDurability = Mathf.Min(maxStatusDurability, _currentStatusDurability + amount);
        float healed = _currentStatusDurability - before;

        if (healed <= 0f) return 0f;   // 满血，什么都不做（不广播，避免"空回复"触发下游效果）

        Debug.Log($"{penId} 耐久回复 +{healed:F1}（{before:F1} → {_currentStatusDurability:F1}），来源：{source}");

        // 回复事件：强化/被动/UI 靠它感知"这次回了多少"（PenDamagedEvent 的镜像）
        // 耐久数值本身的联机同步仍由 PenNetworkBridge 轮询 StatusDurability 完成，这里无需额外广播
        EventBus.Instance.Publish(new PenHealedEvent
        {
            penId = penId,
            amount = healed,
            remainingDurability = _currentStatusDurability,
            maxDurability = maxStatusDurability,
            source = source
        });

        return healed;
    }

    // 外部改了 friction / bounciness 之后调一次，让材质生效（[顺滑] / 强化用）
    public void RefreshPhysicsMaterial()
    {
        RebuildPhysicMaterial(_physMatName);
    }

    // 外部改了 mass 之后应用到刚体（强化系统用）
    public void ApplyMassToRigidbody()
    {
        if (_rb != null) _rb.mass = EffectiveMass;
    }

    // ====== 碰撞事件处理 ======
    private void OnCollisionEnter(Collision collision)
    {
        if (!isActive) return;

        // 区分碰撞对象的类型：PenController / ObstacleController 都挂在父物体上
        var otherPen = collision.gameObject.GetComponentInParent<PenController>();
        var otherObstacle = collision.gameObject.GetComponentInParent<ObstacleController>();

        float impactSpeed = collision.relativeVelocity.magnitude;

        if (otherPen != null && otherPen.isActive)
        {
            // 同一段接触的第二个 Collider 重复回调 → 直接返回
            if (IsDuplicateContact("pen:" + otherPen.penId)) return;

            // 笔-笔碰撞：双方都会触发 OnCollisionEnter，约定只由 penId 较小的一方广播，
            // 否则监听方会收到两次同一撞击，伤害类被动会被双重触发
            if (string.CompareOrdinal(this.penId, otherPen.penId) < 0)
            {
                EventBus.Instance.Publish(new PenPenCollisionEvent
                {
                    penAId = this.penId,
                    penBId = otherPen.penId,
                    initiatorPenId = GetCollisionInitiator(otherPen),
                    relativeSpeed = impactSpeed,
                    collisionNormal = collision.GetContact(0).normal,
                    collisionPoint = collision.GetContact(0).point
                });
            }

            Debug.Log($"{penId} 与 {otherPen.penId} 发生碰撞");
        }
        else if (otherObstacle != null && otherObstacle.isActive)
        {
            // 同上：避免同一次撞击让障碍物重复扣血
            if (IsDuplicateContact("obs:" + otherObstacle.obstacleId)) return;

            // 笔-障碍物碰撞
            float impactForce = impactSpeed * EffectiveMass * 0.8f;
            EventBus.Instance.Publish(new PenObstacleCollisionEvent
            {
                penId = this.penId,
                obstacleId = otherObstacle.obstacleId,
                impactForce = impactForce,
                collisionNormal = collision.GetContact(0).normal,
                collisionPoint = collision.GetContact(0).point
            });

            // 对障碍物结算伤害（障碍物自己处理）
            // 这里不走技能系统，直接调障碍物的 TakeDamage；也不另发事件，
            // 因为已经在同一帧的物理回调里了，直接调用最简单
            otherObstacle.TakeDamage((int)(impactForce * 0.5f), this.penId);
            Debug.Log($"{penId} 与 {otherObstacle.obstacleId} 发生碰撞");
        }
    }

    // 同一段接触的这个目标在时间窗内只算一次（去重）
    private bool IsDuplicateContact(string key)
    {
        float now = Time.time;
        if (_lastContactTime.TryGetValue(key, out float last) && now - last < ContactDebounce)
        {
            return true;
        }

        _lastContactTime[key] = now;
        return false;
    }

    // 判断本次笔-笔碰撞的发起方（即主动撞击的一方）
    // 1) 优先级：当前回合玩家的笔，正在被弹射 / 放置的那支
    // 2) 兜底：碰撞时速度更快的那支
    private string GetCollisionInitiator(PenController other)
    {
        var gm = GameManager.Instance;
        if (gm != null)
        {
            string current = gm.GetCurrentPlayerId();
            if (current == penId || current == other.penId) return current;
        }

        float mySpeed = _rb != null ? _rb.velocity.magnitude : 0f;
        var otherRb = other.GetRigidbody();
        float otherSpeed = otherRb != null ? otherRb.velocity.magnitude : 0f;

        return mySpeed >= otherSpeed ? penId : other.penId;
    }

    private void OnTriggerEnter(Collider other)
    {
        // 用于检测"出界"：只要碰到桌子边缘，就算一次出界接触（待补）
        if (other.CompareTag("OutOfBounds"))
        {
            if (!isActive) return;
            isActive = false;

            EventBus.Instance.Publish(new PenOutOfBoundsEvent
            {
                penId = this.penId,
                outOfBoundsPosition = transform.position
            });

            EventBus.Instance.Publish(new PenEliminatedEvent
            {
                penId = this.penId,
                killerId = null, // 由 BattleManager 决定是否归属对方
                eliminationReason = "OutOfBounds"
            });

            Debug.Log($"Pen({penId}) is out of desk.");

            // 出界后立刻把笔停住
            _rb.isKinematic = true;
        }
    }

    // ====== 被动 / 状态效果接口 ======
    public void ApplyBouncinessModifier(float value)
    {
        _bouncinessOverride = Mathf.Clamp01(value);   // 弹力不能超过 1，否则物理会异常
        RebuildPhysicMaterial(_physMatName);
    }

    public void ApplySpeedMultiplier(float multiplier) => _speedMultiplierOverride = multiplier;

    public void ClearModifiers()
    {
        _bouncinessOverride = -1f;
        _speedMultiplierOverride = 1f;
        slowMultiplier = 1f;
        RebuildPhysicMaterial(_physMatName);
    }

    public float GetEffectiveBounciness()
    {
        return _bouncinessOverride >= 0 ? _bouncinessOverride : EffectiveBounciness;
    }

    // ====== 发射 ======
    public void Launch(Vector3 direction, float force, Vector3 forcePoint)
    {
        if (!isActive) return;
        // pendingForceBonus：Boost 是一次性的；_speedMultiplierOverride：Speed 是重载窗口；
        // slowMultiplier：粘液等减速效果（已改为只影响摩擦）；upgradeForceMultiplier：强化力度乘算，
        // launchForceMultiplier：笔的个性化属性，重笔 = 2
        float totalForce = force * (1f + pendingForceBonus) * _speedMultiplierOverride
                           * slowMultiplier * upgradeForceMultiplier * launchForceMultiplier;
        pendingForceBonus = 0f;
        _rb.AddForceAtPosition(direction.normalized * totalForce, forcePoint, ForceMode.Impulse);
    }

    // 应用开局所选笔的配置（物理参数 / 耐久 / 材质 / 物理材质）
    public void ApplyConfig(PenConfig cfg)
    {
        if (cfg == null) return;

        mass = cfg.mass;
        bounciness = cfg.bounciness;
        friction = cfg.friction;
        linearDrag = cfg.linearDrag;
        angularDrag = cfg.angularDrag;
        maxAngularVelocity = cfg.maxAngularVelocity;
        maxStatusDurability = cfg.maxStatusDurability;
        launchForceMultiplier = cfg.launchForceMultiplier;

        if (_rb != null)
        {
            _rb.mass = EffectiveMass;
            _rb.drag = EffectiveLinearDrag;        // 线性阻尼（不再误用 friction）
            _rb.angularDrag = EffectiveAngularDrag;
            _rb.maxAngularVelocity = maxAngularVelocity;
        }

        // 弹性 / 摩擦通过 PhysicMaterial 应用到所有碰撞体（预制体上可能不止一个 Collider）
        RebuildPhysicMaterial(cfg.configId + "_pm");

        _currentStatusDurability = maxStatusDurability;

        if (cfg.penMaterial != null && _renderer != null)
        {
            _renderer.material = cfg.penMaterial;
            _originalMaterial = cfg.penMaterial;
        }

        // 配置应用完成后，把当前值当作强化基准
        CaptureBase();
        RefreshModifiedStats();
    }

    // 记录强化基准（强化效果都在基准值上算，可重复应用不叠加）
    private void CaptureBase()
    {
        _baseMaxDurability = maxStatusDurability;
        _baseScale = transform.localScale;
    }

    // 每小局开始 / 重新应用强化前调用：所有修饰乘数归 1。
    // 基准字段（mass/friction/...）运行期从不被改，所以无需"还原"——重置的只是乘数。
    public void RestoreBaseValues()
    {
        massAdd = 0f;          massMult = 1f;
        bouncinessAdd = 0f;    bouncinessMult = 1f;
        frictionAdd = 0f;      frictionMult = 1f;
        statusFrictionMult = 1f;
        linearDragAdd = 0f;    linearDragMult = 1f;
        angularDragAdd = 0f;   angularDragMult = 1f;
        scaleAdd = 0f;         scaleMult = 1f;
        durabilityAdd = 0f;    durabilityMult = 1f;
        upgradeForceMultiplier = 1f;

        RefreshModifiedStats();   // 上限回落到基准，当前耐久自动夹紧
    }

    // ===== 修饰量落地：改完任何乘数后调一次，把有效值写进物理组件 =====
    public void RefreshModifiedStats()
    {
        if (_rb != null)
        {
            _rb.mass = EffectiveMass;
            _rb.drag = EffectiveLinearDrag;          // 暂无阻尼类乘数，需要时照 friction 模式加
            _rb.angularDrag = EffectiveAngularDrag;
        }

        transform.localScale = EffectiveScale;

        // 耐久上限：(基准 + 加算) × 乘算，回写进 maxStatusDurability 字段。
        // 上限被抬高时当前耐久跟着抬高（保持"新增的上限是满的"语义）；上限下降只夹紧、不额外扣血。
        float newMax = Mathf.Max(1f, (_baseMaxDurability + durabilityAdd) * durabilityMult);
        if (newMax > maxStatusDurability)
            _currentStatusDurability += newMax - maxStatusDurability;
        maxStatusDurability = newMax;
        _currentStatusDurability = Mathf.Min(_currentStatusDurability, newMax);

        RebuildPhysicMaterial(_physMatName);   // 内部读 GetEffectiveBounciness / EffectiveFriction
    }

    // 根据当前的有效弹力 / 摩擦重建物理材质（碰撞体用）
    private void RebuildPhysicMaterial(string matName)
    {
        _physMatName = matName;

        var cols = GetComponentsInChildren<Collider>();   // 注意：包含全部子物体
        foreach (var col in cols)
        {
            if (col == null) continue;
            col.material = new PhysicMaterial(matName)
            {
                bounciness = GetEffectiveBounciness(),     // 受 Bounce 被动影响
                dynamicFriction = EffectiveFriction,
                staticFriction = EffectiveFriction
            };
        }
    }

    /// <summary>
    /// 传送这支笔。
    /// ? 必须写刚体的 position / rotation，不能只写 transform。
    /// 本项目 ProjectSettings/DynamicsManager.asset 里 m_AutoSyncTransforms = 0，
    /// 只写 transform 会在下一次物理同步时被旧的位姿覆盖（表现为瞬间弹回原位置）。
    /// </summary>
    public void TeleportTo(Vector3 position)
    {
        if (_rb != null)
        {
            _rb.position = position;
            _rb.velocity = Vector3.zero;
            _rb.angularVelocity = Vector3.zero;
        }
        transform.position = position;   // 同一帧内让显示也不错位
    }

    /// <summary>
    /// 准备阶段：选中 / 放置强化等把笔冻住（不算淘汰、也不会互相推挤）。
    /// 正式发射时由 StartNewRound / ResetForRound / ResetPen 自动恢复为动态刚体。
    /// </summary>
    public void SetKinematic(bool value)
    {
        if (_rb == null) return;

        _rb.isKinematic = value;
        if (value)
        {
            _rb.velocity = Vector3.zero;
            _rb.angularVelocity = Vector3.zero;
        }
    }

    /// <summary>
    /// 给客户端按配置同步材质（纯表现层切换，只改材质，不动物理）
    /// 参数来自 ApplyConfig；客户端不需要重建弹性 / 摩擦 / PhysicMaterial。
    /// </summary>
    public void SetPenMaterial(Material mat)
    {
        if (mat == null) return;

        if (_renderer == null) _renderer = GetComponentInChildren<Renderer>();
        if (_renderer != null) _renderer.material = mat;
    }

    private Renderer[] _renderers;

    /// <summary>
    /// 准备阶段：选中 / 放置强化等临时关掉笔的渲染。
    /// ? 不要用 gameObject.SetActive(false)，那会让 NetworkObject 被判定为 despawn、笔直接消失。
    /// 渲染列表必须**每一次调用时重新收集**（临时挂上的特效子物体也算），否则燃烧 / 冰霜特效会漏掉。
    /// </summary>
    public void SetVisible(bool value)
    {
        isVisible = value;

        // 每次都重新收集：燃烧火苗 / 冰霜覆盖层是运行期才挂上去的子物体，
        // 只缓存一次的话它们会躲过隐藏（准备阶段笔看不见、火苗还飘着）。
        _renderers = GetComponentsInChildren<Renderer>(true);

        foreach (var r in _renderers)
            if (r != null) r.enabled = value;
    }

    public void ResetPen(Vector3 spawnPosition)
    {
        // 重置时的朝向（初始出生 & 每小局回合重置都走这里）
        Quaternion rot = transform.rotation;
        if (randomSpawnRotation)
        {
            float yaw = Random.Range(spawnRotationYRange.x, spawnRotationYRange.y);
            rot = Quaternion.Euler(90f, yaw, 0f);
        }

        if (_rb != null)
        {
            _rb.isKinematic = false;
            _rb.position = spawnPosition;    // ? 关键：写刚体，不能只写 transform
            _rb.rotation = rot;
            _rb.velocity = Vector3.zero;
            _rb.angularVelocity = Vector3.zero;
        }

        transform.position = spawnPosition;
        transform.rotation = rot;

        isActive = true;
        SetVisible(true);   // 正式发射要重新启用（准备阶段被 SetVisible(false) 藏起来了）
        ClearModifiers();

        _currentStatusDurability = maxStatusDurability;
    }

    // 获取速度（给 BattleManager 读取）
    public Vector3 GetVelocity() => _rb.velocity;

    // 获取当前耐久（UI 显示用）
    public float GetStatusDurability() => _currentStatusDurability;
}
