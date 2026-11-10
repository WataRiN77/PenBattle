using MoreMountains.Feedbacks;
using PenBattleGame.Core;
using UnityEngine;

/// 把 PenBattle 的事件接到 Feel 的反馈上（**表现层桥接**）。
///
/// 设计原则：
///   · 代码只负责「事件 → (位置, 强度)」的映射，**反馈内容全部在 Inspector 里配**，改手感不用改代码。
///   · 世界层（火花 / 碎片 / 音效，按撞击点播）和屏幕层（震屏 / 顿帧，全屏）**刻意分成两个 MMF_Player**：
///     联机时"远端玩家互撞"不该震自己的屏幕，但世界层的火花照常出现。
///   · 联机注意：反馈**只在各端本地播**，绝不参与房主的权威逻辑。
///
/// 用法：挂在场景任意物体上（例如 GAME_MANAGER），把 5 个 MMF_Player 拖进对应槽位。
public class FeelFeedbackBridge : MonoBehaviour
{
    [Header("Feedback 引用（把场景里的 MMF Player 拖进来）")]
    [Tooltip("笔对笔碰撞·世界层：撞击点火花 / 碎片 / 音效")]
    public MMF_Player hitWorldFeedback;

    [Tooltip("笔对笔碰撞·屏幕层：震屏 / 顿帧（只在本端参与的碰撞时满强度）")]
    public MMF_Player hitScreenFeedback;

    [Tooltip("撞障碍物：闷响 / 小火花")]
    public MMF_Player obstacleHitFeedback;

    [Tooltip("笔被淘汰：慢动作 / 强震 / 闷响（全屏，所有端都播）")]
    public MMF_Player eliminateFeedback;

    [Tooltip("本端发射：弹弓音 / 笔身拉伸")]
    public MMF_Player launchFeedback;

    [Header("强度换算：相对速度 → intensity")]
    [Tooltip("低于这个相对速度完全不播（避免桌上轻碰刷屏）")]
    public float minRelativeSpeed = 2f;

    [Tooltip("达到这个相对速度时 intensity = 1")]
    public float maxRelativeSpeed = 25f;

    [Header("强度换算：撞击力度 → intensity")]
    public float minImpactForce = 3f;
    public float maxImpactForce = 20f;

    [Header("强度换算：发射力度")]
    public float maxLaunchForce = 10f;

    [Header("联机")]
    [Tooltip("与本端无关的碰撞（远端玩家互撞）时，屏幕层反馈的强度倍率。" +
             "1 = 一视同仁、所有碰撞都满强度震（当前选定：方案 A）；调小则远端互撞震得轻一些")]
    [Range(0f, 1f)] public float remoteScreenIntensityScale = 1f;

    private void OnEnable()
    {
        var bus = EventBus.Instance;
        if (bus == null) return;

        // 订阅"表现层事件"而不是玩法事件：房主广播后各端都会发这个，客户端也能播反馈；
        // 玩法侧（被动技能荆棘/点燃/冰冻）只订阅原始的碰撞事件，不会受影响。
        bus.Subscribe<CollisionFeedbackEvent>(OnCollisionFeedback);
        bus.Subscribe<ObstacleFeedbackEvent>(OnObstacleFeedback);
        bus.Subscribe<EliminateFeedbackEvent>(OnEliminateFeedback);
        bus.Subscribe<InputLaunchEvent>(OnInputLaunch);
    }

    private void OnDisable()
    {
        var bus = EventBus.Instance;
        if (bus == null) return;

        bus.Unsubscribe<CollisionFeedbackEvent>(OnCollisionFeedback);
        bus.Unsubscribe<ObstacleFeedbackEvent>(OnObstacleFeedback);
        bus.Unsubscribe<EliminateFeedbackEvent>(OnEliminateFeedback);
        bus.Unsubscribe<InputLaunchEvent>(OnInputLaunch);
    }

    // ================= 事件处理 =================

    private void OnCollisionFeedback(CollisionFeedbackEvent evt)
    {
        float k = Normalize(evt.relativeSpeed, minRelativeSpeed, maxRelativeSpeed);

        // 诊断：把每次碰撞的原始速度和换算结果打出来，方便定位"是没触发还是被阈值挡了"
        Debug.Log($"[Feel] 碰撞 speed={evt.relativeSpeed:F2} → k={k:F2} " +
                  $"(阈值 {minRelativeSpeed}~{maxRelativeSpeed}；World={(hitWorldFeedback != null)}, Screen={(hitScreenFeedback != null)})");

        if (k <= 0f) return;                       // 轻碰不播，防止刷屏

        // 世界层：撞击点火花 / 音效，任何端的碰撞都该看得见
        hitWorldFeedback?.PlayFeedbacks(evt.point, k);

        // 屏幕层：默认 remoteScreenIntensityScale = 1 → 所有碰撞一视同仁（方案 A）；
        // 想只保留"和我有关才震"，把它调小即可。
        float screenScale = (IsMine(evt.penAId) || IsMine(evt.penBId)) ? 1f : remoteScreenIntensityScale;
        if (screenScale > 0f)
            hitScreenFeedback?.PlayFeedbacks(evt.point, k * screenScale);
    }

    private void OnObstacleFeedback(ObstacleFeedbackEvent evt)
    {
        float k = Normalize(evt.impactForce, minImpactForce, maxImpactForce);
        if (k <= 0f) return;

        obstacleHitFeedback?.PlayFeedbacks(evt.point, k);
    }

    private void OnEliminateFeedback(EliminateFeedbackEvent evt)
    {
        // 淘汰是全局事件 → 所有端都满强度播（不带位置，全屏类反馈不需要）
        eliminateFeedback?.PlayFeedbacks();
    }

    private void OnInputLaunch(InputLaunchEvent evt)
    {
        if (!IsMine(evt.penId)) return;            // 只有自己发射才播，别人发射别在自己耳边响

        float k = Normalize(evt.launchForce, 0f, Mathf.Max(0.01f, maxLaunchForce));
        launchFeedback?.PlayFeedbacks(Vector3.zero, Mathf.Max(0.15f, k));
    }

    // ================= 工具 =================

    /// 把 value 从 [min, max] 归一化到 (0, 1]；低于 min 返回 0（= 不播）
    private static float Normalize(float value, float min, float max)
    {
        if (value <= min) return 0f;
        if (max <= min) return 1f;
        return Mathf.Clamp01(Mathf.InverseLerp(min, max, value));
    }

    /// 这支笔是不是本端的？（单机永远为 true；联机按同步过来的席位判断）
    private static bool IsMine(string penId)
    {
        var flow = NetworkMatchFlow.Instance;
        if (flow == null) return true;

        string myId = flow.MyPlayerId;
        return !string.IsNullOrEmpty(myId) && penId == myId;
    }
}
