using UnityEngine;

/// <summary>
/// 乌鸦群生成器：一次刷一小群乌鸦（默认 6 只，可调 5-8）。
/// 挂到一个空物体上（放在乌鸦房间的中心位置），把做好的乌鸦 Prefab 拖进来。
/// 两种孵化时机：
/// - spawnOnStart = true 且 wakeDistance = 0：进场景就孵化
/// - wakeDistance > 0：玩家靠近才孵化（推荐：玩家进乌鸦房才惊起一群，恐怖感更好）
/// </summary>
public class CrowFlock : MonoBehaviour
{
    [Header("鸟群配置")]
    [Tooltip("乌鸦 Prefab（做好一只挂齐组件的乌鸦，拖成 Prefab 后拖到这里）")]
    public CrowAI crowPrefab;

    [Tooltip("一次刷几只（5-8 只手感最好）")]
    public int flockSize = 6;

    [Tooltip("在生成点周围多大的圆里散开")]
    public float spawnRadius = 3f;

    [Header("孵化时机")]
    [Tooltip("勾上 = Start 时直接孵化（wakeDistance 不填时）")]
    public bool spawnOnStart = true;

    [Tooltip("大于 0 时：玩家进入这个距离才孵化（进房惊鸟效果）。0 = 不启用")]
    public float wakeDistance = 0f;

    private bool spawned = false;

    private void Start()
    {
        if (spawnOnStart && wakeDistance <= 0f)
            Spawn();
    }

    private void Update()
    {
        if (!spawned && wakeDistance > 0f)
        {
            GameObject playerObj = GameObject.FindGameObjectWithTag("Player");
            if (playerObj != null &&
                Vector2.Distance(transform.position, playerObj.transform.position) <= wakeDistance)
            {
                Spawn();
            }
        }
    }

    /// <summary> 在生成点半径内随机散开刷一群乌鸦 </summary>
    public void Spawn()
    {
        if (spawned) return;
        if (crowPrefab == null)
        {
            Debug.LogWarning("[乌鸦群] crowPrefab 没拖，刷不出乌鸦！", gameObject);
            return;
        }

        spawned = true;
        for (int i = 0; i < flockSize; i++)
        {
            // 圆内随机点（至少离中心 0.5 格，防止全叠在一个点上）
            Vector2 offset = Random.insideUnitCircle * spawnRadius;
            if (offset.magnitude < 0.5f) offset = offset.normalized * 0.5f;
            Instantiate(crowPrefab, (Vector2)transform.position + offset, Quaternion.identity);
        }
        Debug.Log("[乌鸦群] 已孵化 " + flockSize + " 只乌鸦", gameObject);
    }

    // 选中生成器时在 Scene 视图画个圈，方便小泽看生成范围
    private void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.yellow;
        Gizmos.DrawWireSphere(transform.position, spawnRadius);
        if (wakeDistance > 0f)
        {
            Gizmos.color = Color.red;
            Gizmos.DrawWireSphere(transform.position, wakeDistance);
        }
    }
}
