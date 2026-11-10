using UnityEngine;
using PenBattleGame.Core;

// 燃烧：按"强度层数"结算（不是按持续回合数）
// 回合结束时：每有 1 层扣 1 点耐久，然后层数减半（向下取整）；层数减到 0 就熄灭
public class BurnEffect : StatusEffect
{
    [Header("燃烧强度")]
    [Tooltip("当前燃烧层数：回合结束时每层扣 1 点耐久，之后层数减半向下取整")]
    public int stacks = 0;

    protected override void Awake()
    {
        base.Awake();
        effectId = "Burn";
        duration = 0;   // 燃烧由"层数"驱动，不走基类的"持续回合数"逻辑
    }

    // 叠加燃烧层数（点燃被动 / 燃烧技能都调它）
    public void AddStacks(int amount)
    {
        if (amount <= 0) return;
        stacks += amount;
        Debug.Log($"{_pen?.penId} 燃烧 +{amount} 层，当前 {stacks} 层");
    }

    protected override void OnApply()
    {
        Debug.Log($"燃烧施加在 {_pen?.penId} 上，当前 {stacks} 层");

        // 视觉表现：挂在一个子物体上（燃烧结束时销毁它，不影响笔本身）
        if (transform.Find("BurnVisual") == null)
        {
            var go = new GameObject("BurnVisual");
            go.transform.SetParent(transform, false);
            go.AddComponent<BurnVisual>();
        }
    }

    protected override void OnTick()
    {
        if (_pen == null) return;

        if (stacks <= 0)
        {
            OnExpire();
            return;
        }

        // 1) 每层扣 1 点耐久
        int damage = stacks;
        _pen.ReduceStatusDurability(damage, "Burn");

        // 2) 强度减半（向下取整）
        stacks /= 2;

        EventBus.Instance.Publish(new BurnTickEvent
        {
            penId = _pen.penId,
            remainingDurability = _pen.GetStatusDurability(),
            stacks = stacks
        });

        Debug.Log($"{_pen.penId} 燃烧结算：本次 -{damage} 点耐久，剩余 {stacks} 层");

        // 3) 烧完就熄灭
        if (stacks <= 0) OnExpire();
    }

    protected override void OnExpire()
    {
        Debug.Log($"{_pen?.penId} 的燃烧熄灭了");

        // 顺手清掉视觉（视觉自己也会在检测到 BurnEffect 消失时销毁）
        var vis = transform.Find("BurnVisual");
        if (vis != null) Destroy(vis.gameObject);

        base.OnExpire();
    }
}
