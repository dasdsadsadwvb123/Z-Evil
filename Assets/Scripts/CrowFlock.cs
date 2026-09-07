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

    [Header("鸦群音效（孵化时开始循环，全灭自动停止）")]
    [Tooltip("鸦群循环音（叫声+扇翅，小泽自己拖音频文件）；不拖 = 全程静音不报错")]
    public AudioClip flockLoopClip;
    [Tooltip("循环音音量")]
    [Range(0f, 1f)]
    public float volume = 0.7f;

    private bool spawned = false;
    private AudioSource audioSource;   // 循环音音源（自动补在本物体上）
    private int aliveCount = 0;        // 存活乌鸦计数，归零 → 停止循环音

    private void Start()
    {
        EnsureAudioSource();
        if (spawnOnStart && wakeDistance <= 0f)
            Spawn();
    }

    /// <summary> 确保本物体上有 AudioSource（loop 循环、不开机自播） </summary>
    private void EnsureAudioSource()
    {
        audioSource = GetComponent<AudioSource>();
        if (audioSource == null)
            audioSource = gameObject.AddComponent<AudioSource>();
        audioSource.playOnAwake = false;
        audioSource.loop = true;
        audioSource.clip = flockLoopClip;
        audioSource.volume = volume;
    }

    /// <summary> CrowAI 死亡时回调：存活数减一，归零 → 停止循环音（惊群≠死亡，不会误停） </summary>
    public void NotifyCrowDied()
    {
        aliveCount = Mathf.Max(0, aliveCount - 1);
        if (aliveCount == 0 && audioSource != null && audioSource.isPlaying)
        {
            audioSource.Stop();
            Debug.Log("[乌鸦群] 全灭，循环音停止", gameObject);
        }
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
            CrowAI crow = Instantiate(crowPrefab, (Vector2)transform.position + offset, Quaternion.identity);
            crow.ownerFlock = this; // 死亡时回调通知，做全灭计数
        }
        aliveCount = flockSize;

        // 孵化 = 鸦群被惊起：开始循环音（clip 没拖就静音跳过）
        if (flockLoopClip != null && audioSource != null)
            audioSource.Play();

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
