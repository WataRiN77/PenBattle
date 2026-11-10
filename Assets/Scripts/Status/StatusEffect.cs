using PenBattleGame.Core;
using UnityEngine;

public abstract class StatusEffect : MonoBehaviour
{
    [Header("Base")]
    public string effectId = "Status_Base";
    // 持续回合数（一回合 = 小局内每位存活玩家各行动一次）
    public int duration = 3;
    public bool isActive = true;

    [Header("联机镜像")]
    [Tooltip("客户端镜像实例：只做表现（视觉/层数显示），不结算伤害、不改物理参数")]
    public bool visualOnly;

    protected PenController _pen;
    protected int _tickCount = 0;

    protected virtual void Awake()
    {
        _pen = GetComponent<PenController>();
        if (_pen == null)
        {
            Debug.LogError("StatusEffect 必须挂载在 PenController 同一物体上");
            enabled = false;
        }
    }

    protected virtual void Start()
    {
        OnApply();

        // 镜像实例不订阅回合事件：客户端不跑玩法逻辑，伤害由房主结算后同步耐久
        if (visualOnly) return;

        // 订阅"回合结束"事件：小局内每位存活玩家各行动一次后触发
        EventBus.Instance.Subscribe<TurnEndedEvent>(OnTurnEnded);
    }

    protected virtual void OnApply()
    {
        Debug.Log($"{effectId} 施加在 {_pen?.penId} 上，持续 {duration} 回合");
    }
    protected virtual void OnTick()
    {
        _tickCount++;
        if (duration > 0 && _tickCount >= duration)
        {
            OnExpire();
        }
    }
    protected virtual void OnExpire()
    {
        Debug.Log($"{effectId} 在 {_pen?.penId} 上消失");
        EventBus.Instance.Unsubscribe<TurnEndedEvent>(OnTurnEnded);
        Destroy(this);
    }

    // 提前移除效果（如被驱散）
    public virtual void RemoveEffect()
    {
        OnExpire();
    }

    // 事件回调
    private void OnTurnEnded(TurnEndedEvent evt)
    {
        if (visualOnly) return;                      // 双保险：镜像永远不结算
        if (!isActive || _pen == null || !_pen.isActive) return;
        OnTick();
    }
}
