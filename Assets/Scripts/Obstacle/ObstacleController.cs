using PenBattleGame.Core;
using UnityEngine;

[RequireComponent(typeof(Rigidbody), typeof(Collider))]
public class ObstacleController : MonoBehaviour
{
    [Header("标识")]
    public string obstacleId = "Wall_01";

    [Header("耐久度")]
    public int maxDurability = 3;
    public int currentDurability { get; private set; }

    [Header("是否可破坏")]
    public bool isIndestructible = false;

    [Header("物理参数")]
    public float mass = 100f;
    public float bounciness = 0.4f;

    // 内部引用
    private Rigidbody _rb;
    private Collider _col;
    private bool _isActive = true;
    public bool isActive => _isActive;

    private void Awake()
    {
        _rb = GetComponent<Rigidbody>();
        _col = GetComponent<Collider>();

        _rb.mass = mass;
        _rb.useGravity = false;
        _rb.drag = 0.1f;
        _rb.angularDrag = 0.2f;

        currentDurability = maxDurability;
    }

    public void TakeDamage(int damage, string attackerPenId)
    {
        if (!_isActive || isIndestructible) return;
        if (damage <= 0) return;

        int old = currentDurability;
        currentDurability = Mathf.Max(0, currentDurability - damage);

        EventBus.Instance.Publish(new ObstacleDamagedEvent
        {
            obstacleId = this.obstacleId,
            oldDurability = old,
            newDurability = currentDurability,
            attackerPenId = attackerPenId
        });

        if (currentDurability <= 0)
        {
            DestroyObstacle(attackerPenId);
        }
    }

    public void DestroyObstacle(string destroyerPenId)
    {
        if (!_isActive) return;
        _isActive = false;

        EventBus.Instance.Publish(new ObstacleDestroyedEvent
        {
            obstacleId = this.obstacleId,
            position = transform.position,
            destroyerPenId = destroyerPenId
        });

        // 触发破碎特效（可以实例化碎片预制体）
        // 然后销毁自己
        Destroy(gameObject, 0.1f);
    }

    public void ResetObstacle(Vector3 position)
    {
        gameObject.SetActive(true);

        // 写刚体：autoSyncTransforms=0 时只写 transform.position 会被物理同步覆盖
        if (_rb != null)
        {
            _rb.position = position;
            _rb.velocity = Vector3.zero;
            _rb.angularVelocity = Vector3.zero;
        }
        transform.position = position;

        _isActive = true;
        currentDurability = maxDurability;
    }

    private void OnCollisionEnter(Collision collision)
    {
        // 障碍物被撞飞后自身也可以触发一些逻辑

    }
}