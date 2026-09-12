using UnityEngine;
using UnityEngine.Events;
using System.Collections;

/// <summary>
/// 乌鸦 AI：1 滴血（HealthSystem maxHealth=1）+ 飘忽飞行 + 惊群四散 + 俯冲啄击。
/// 挂到乌鸦物体上（需要：SpriteRenderer + CircleCollider2D（实体，别勾 Is Trigger）
/// + HealthSystem（maxHealth=1、destroyOnDeath=true））。走自由向量飞行，
/// 不走丧尸的网格步进——所以碰墙靠物理碰撞自动滑动（Dynamic 刚体 + 无重力）。
/// 惊群：任何一只乌鸦被打中/打死 → 静态广播 → 全场乌鸦四散 1.5 秒再回压。
/// </summary>
public class CrowAI : MonoBehaviour
{
    /// <summary> 乌鸦状态：巡航绕圈 → 追击 → 俯冲 → 拉起 → 惊群四散 </summary>
    private enum CrowState { Cruise, Chase, Dive, Recover, Scatter }

    [Header("巡航飞行")]
    [Tooltip("绕巢点转圈的速度")]
    public float cruiseSpeed = 2f;
    [Tooltip("绕巢点的半径（格子）")]
    public float orbitRadius = 2.5f;
    [Tooltip("飘忽摆动幅度（越大越难用手枪命中）")]
    public float wanderStrength = 1.2f;

    [Header("追击")]
    [Tooltip("进入这个距离开始追玩家")]
    public float aggroRange = 6f;
    [Tooltip("追击速度（比普通丧尸 4 略慢，能跑掉但很紧）")]
    public float chaseSpeed = 3.2f;
    [Tooltip("跑出这个距离放弃追击，回去绕圈")]
    public float deaggroRange = 9f;

    [Header("俯冲啄击")]
    [Tooltip("每隔几秒尝试一次俯冲")]
    public float swoopInterval = 3f;
    [Tooltip("俯冲冲刺速度（快，有威胁）")]
    public float swoopSpeed = 7f;
    [Tooltip("俯冲最长持续秒数（冲完自动拉起）")]
    public float swoopMaxTime = 0.8f;
    [Tooltip("啄击判定距离")]
    public float attackRange = 0.7f;
    [Tooltip("啄击伤害")]
    public int attackDamage = 1;
    [Tooltip("啄完拉起的时间（给玩家反击窗口）")]
    public float recoverTime = 0.5f;

    [Header("惊群四散")]
    [Tooltip("受惊后乱飞几秒")]
    public float scatterTime = 1.5f;
    [Tooltip("受惊乱飞速度")]
    public float scatterSpeed = 6f;

    [Header("掉落（小概率掉弹药）")]
    [Tooltip("弹药包 PickupItem 的 Prefab（参考密码机钥匙 Prefab 做法），不拖就必不掉")]
    public PickupItem dropPickupPrefab;
    [Range(0f, 1f)]
    [Tooltip("掉落概率（0.3 = 30%）")]
    public float dropChance = 0.3f;

    [Header("死亡表现（尸体）")]
    [Tooltip("死亡图：被打死后 SpriteRenderer 换成这张（不拖 = 保持原图不换）")]
    public Sprite deathSprite;
    [Tooltip("尸体停留秒数（0 = 永久不消失）")]
    public float corpseDuration = 2f;
    [Tooltip("死亡哀叫：死亡瞬间短促叫一声（不拖 = 静音，不影响其他）")]
    public AudioClip deathCryClip;

    [HideInInspector]
    [Tooltip("生成我的鸟群（CrowFlock 自动填，全灭计数用；手动摆的乌鸦留空即可）")]
    public CrowFlock ownerFlock;

    [Header("占位动画（小泽接真翅膀动画后取消勾选）")]
    [Tooltip("用 翻转朝向 + Y缩放正弦 模拟扇翅")]
    public bool usePlaceholderFlap = true;
    [Tooltip("扇翅频率")]
    public float flapSpeed = 10f;

    [Header("死亡事件（可接音效/剧情计数）")]
    public UnityEvent onCrowDeath;

    // ======== 惊群广播：所有乌鸦共用一个静态事件 ========
    private static event System.Action OnCrowDisturbed;
    /// <summary> 惊动全场乌鸦（任何一只被打中/打死时调用） </summary>
    public static void DisturbFlock()
    {
        OnCrowDisturbed?.Invoke();
    }

    private Rigidbody2D rb;
    private SpriteRenderer sr;
    private HealthSystem myHealth;
    private Transform player;
    private HealthSystem playerHealth;

    private CrowState state = CrowState.Cruise;
    private Vector2 nestPoint;      // 出生点 = 绕圈中心
    private float orbitAngle;       // 绕圈当前角度
    private float wanderSeed;       // 每只乌鸦不同的摆动相位（群飞不整齐才像活物）
    private float swoopTimer;       // 距下次俯冲的计时
    private float stateTimer;       // 当前状态已持续的时间
    private Vector2 diveDir;        // 俯冲方向（锁定俯冲瞬间的方向）
    private Vector2 scatterDir;     // 受惊乱飞方向
    private bool dealtDamage;       // 本次俯冲是否已经啄到（一次俯冲只伤一次）
    private bool dead = false;
    private Vector3 baseScale;      // 占位扇翅用

    private void Start()
    {
        rb = GetComponent<Rigidbody2D>();
        if (rb == null) rb = gameObject.AddComponent<Rigidbody2D>();
        rb.gravityScale = 0f;      // 乌鸦会飞：无重力
        rb.freezeRotation = true;  // 碰撞时不打转

        sr = GetComponent<SpriteRenderer>();

        myHealth = GetComponent<HealthSystem>();
        if (myHealth != null)
        {
            myHealth.deathTriggerName = "";   // 乌鸦没有死亡动画，防止触发不存在的 Animator 参数
            myHealth.destroyOnDeath = false;  // 死后生命周期由 CrowAI 接管（尸体表现 + 计时销毁）
            myHealth.OnDamaged += OnHurt;     // 被打中 → 惊群
            myHealth.OnDeath += OnCrowKilled;
        }

        GameObject playerObj = GameObject.FindGameObjectWithTag("Player");
        if (playerObj != null)
        {
            player = playerObj.transform;
            playerHealth = playerObj.GetComponent<HealthSystem>();
        }

        nestPoint = rb.position;
        orbitAngle = Random.Range(0f, 360f) * Mathf.Deg2Rad;
        wanderSeed = Random.Range(0f, 100f);
        swoopTimer = Random.Range(0f, swoopInterval); // 错开俯冲节奏，不然全群齐冲像阅兵
        baseScale = transform.localScale;

        OnCrowDisturbed += HandleDisturbed; // 订阅惊群广播
    }

    private void OnDestroy()
    {
        // 退订静态事件，防止残留引用报错
        OnCrowDisturbed -= HandleDisturbed;
        if (myHealth != null)
        {
            myHealth.OnDamaged -= OnHurt;
            myHealth.OnDeath -= OnCrowKilled;
        }
    }

    private void Update()
    {
        if (dead) return;
        stateTimer += Time.deltaTime;

        switch (state)
        {
            case CrowState.Cruise:  DoCruise();  break;
            case CrowState.Chase:   DoChase();   break;
            case CrowState.Dive:    DoDive();    break;
            case CrowState.Recover: DoRecover(); break;
            case CrowState.Scatter: DoScatter(); break;
        }

        UpdatePlaceholderAnim();
    }

    // ======== 各状态行为 ========

    /// <summary> 巡航：绕出生点转圈 + 正弦飘移 </summary>
    private void DoCruise()
    {
        orbitAngle += (cruiseSpeed / Mathf.Max(0.5f, orbitRadius)) * Time.deltaTime;
        Vector2 orbitPoint = nestPoint + new Vector2(Mathf.Cos(orbitAngle), Mathf.Sin(orbitAngle)) * orbitRadius;

        Vector2 vel = (orbitPoint - rb.position).normalized * cruiseSpeed;
        vel += PerpOf(vel) * Mathf.Sin(Time.time * 3f + wanderSeed) * wanderStrength; // 飘忽
        rb.velocity = vel;

        if (PlayerDist() <= aggroRange)
            ToState(CrowState.Chase);
    }

    /// <summary> 追击：朝玩家飞 + 横向摆动（手枪单发难命中的关键），攒够计时就俯冲 </summary>
    private void DoChase()
    {
        if (player == null) { ToState(CrowState.Cruise); return; }

        Vector2 toPlayer = (Vector2)player.position - rb.position;
        Vector2 vel = toPlayer.normalized * chaseSpeed;
        vel += PerpOf(vel) * Mathf.Sin(Time.time * 4f + wanderSeed) * wanderStrength;
        rb.velocity = vel;

        swoopTimer += Time.deltaTime;
        if (swoopTimer >= swoopInterval && toPlayer.magnitude <= aggroRange)
        {
            diveDir = toPlayer.normalized; // 锁定俯冲瞬间方向
            dealtDamage = false;
            ToState(CrowState.Dive);
            return;
        }

        if (PlayerDist() > deaggroRange)
            ToState(CrowState.Cruise);
    }

    /// <summary> 俯冲：直线高速冲，啄到人或冲超时就拉起 </summary>
    private void DoDive()
    {
        rb.velocity = diveDir * swoopSpeed;

        // 啄击判定：一次俯冲只造成一次伤害
        if (!dealtDamage && PlayerDist() <= attackRange)
        {
            if (playerHealth != null) playerHealth.TakeDamage(attackDamage);
            dealtDamage = true;
        }

        if (stateTimer >= swoopMaxTime)
            ToState(CrowState.Recover);
    }

    /// <summary> 拉起：慢慢飘一下，给玩家喘息窗口，然后回追击 </summary>
    private void DoRecover()
    {
        rb.velocity = Vector2.Lerp(rb.velocity, (-diveDir + Vector2.up).normalized * 1.5f, 0.1f);
        if (stateTimer >= recoverTime)
        {
            swoopTimer = 0f;
            ToState(CrowState.Chase);
        }
    }

    /// <summary> 惊群：随机方向高速四散，时间到后看情况回巡航或追击 </summary>
    private void DoScatter()
    {
        rb.velocity = scatterDir * scatterSpeed;
        if (stateTimer >= scatterTime)
            ToState(PlayerDist() <= aggroRange ? CrowState.Chase : CrowState.Cruise);
    }

    // ======== 状态切换 / 事件 ========

    private void ToState(CrowState next)
    {
        state = next;
        stateTimer = 0f;
        PlayStateAnim(next); // 动画口子（现在是空的，留给小泽接动画）
    }

    private void HandleDisturbed()
    {
        if (dead) return;
        scatterDir = Random.insideUnitCircle.normalized; // 每只乱飞方向不同才像惊群
        if (scatterDir == Vector2.zero) scatterDir = Vector2.up;
        ToState(CrowState.Scatter);
    }

    private void OnHurt()
    {
        DisturbFlock(); // 被打中：自己 + 全场乌鸦一起惊
    }

    private void OnCrowKilled()
    {
        dead = true;
        rb.velocity = Vector2.zero;
        DisturbFlock(); // 枪响/击杀 = 惊群信号

        // 死亡哀叫：走项目距离听声（AudibleAudio 在全局临时音源上播，尸体销毁也不打断这声短叫，
        // 且会按距离衰减——修"一触发就全图异响"）
        if (deathCryClip != null)
            AudibleAudio.PlayAt(deathCryClip, transform.position);

        // 通知鸟群：我死了（flock 用它做"全灭停止循环音"的存活计数）
        if (ownerFlock != null)
            ownerFlock.NotifyCrowDied();

        // 小概率掉弹药（克隆 Prefab，参考密码机钥匙套路）
        if (dropPickupPrefab != null && Random.value <= dropChance)
            Instantiate(dropPickupPrefab, rb.position, Quaternion.identity);

        onCrowDeath?.Invoke();

        // ===== 尸体表现（死亡后的生命周期由本脚本接管，HealthSystem 不再自动销毁） =====
        // 1. 刚体转 Kinematic：不再参与物理推挤（尸体不挡路、不被推动）
        rb.isKinematic = true;
        // 2. 碰撞全关：尸体不挡子弹射线、不挡路（HealthSystem.isDead 本身也会让子弹代码跳过尸体）
        foreach (Collider2D col in GetComponents<Collider2D>())
            col.enabled = false;
        // 3. 换死亡图：只动 SpriteRenderer.sprite，其他渲染参数一律不碰（没拖 deathSprite 就保持原图）
        if (sr != null)
        {
            if (deathSprite != null)
                sr.sprite = deathSprite;
            sr.sortingOrder -= 5; // 尸体垫底，避免挡住活物
        }
        // 4. 落地感：小幅下坠模拟"翻转面朝下坠落落地"
        //    （不能用真重力：碰撞已关会掉穿地板，所以用固定距离的缓动下坠，最稳）
        StartCoroutine(CorpseFallRoutine());

        // 5. 停留计时：0 = 永久不消失；大于 0 到点销毁
        if (corpseDuration > 0f)
            Destroy(gameObject, corpseDuration);
    }

    /// <summary> 尸体小幅下坠（约 0.25 格，缓出曲线），模拟从空中坠落落地即停 </summary>
    private IEnumerator CorpseFallRoutine()
    {
        Vector2 startPos = rb.position;
        float dropDistance = 0.25f;
        float t = 0f;
        while (t < 0.3f)
        {
            t += Time.deltaTime;
            float eased = 1f - (1f - Mathf.Clamp01(t / 0.3f)) * (1f - Mathf.Clamp01(t / 0.3f)); // 缓出
            rb.MovePosition(startPos + Vector2.down * (dropDistance * eased));
            yield return null;
        }
    }

    private float PlayerDist()
    {
        if (player == null) return float.MaxValue;
        return Vector2.Distance(rb.position, player.position);
    }

    private static Vector2 PerpOf(Vector2 v)
    {
        v.Normalize();
        return new Vector2(-v.y, v.x);
    }

    // ======== 动画口子（❌ 本脚本不碰 Animator，以下全部是留给小泽的接入口） ========

    /// <summary> 状态切换动画口子：小泽接自己的动画系统时在这里分发 </summary>
    private void PlayStateAnim(CrowState s)
    {
        switch (s)
        {
            case CrowState.Cruise:  PlayGlide();  break;
            case CrowState.Chase:   PlayFlap();   break;
            case CrowState.Dive:    PlayDive();   break;
            case CrowState.Recover: PlayFlap();   break;
            case CrowState.Scatter: PlayFlap();   break;
        }
        // 接入示例（小泽以后自己填）：
        // if (animator != null) animator.SetTrigger("Flap");
    }

    /// <summary> 滑翔动画（空实现，占位阶段用占位扇翅代替） </summary>
    public void PlayGlide() { /* 小泽以后在这里接滑翔动画 */ }

    /// <summary> 扇翅动画（空实现） </summary>
    public void PlayFlap() { /* 小泽以后在这里接扇翅动画 */ }

    /// <summary> 俯冲动画（空实现） </summary>
    public void PlayDive() { /* 小泽以后在这里接俯冲动画 */ }

    /// <summary> 占位扇翅：翻转朝向 + Y 缩放正弦，接了真动画就把 usePlaceholderFlap 取消勾选 </summary>
    private void UpdatePlaceholderAnim()
    {
        if (!usePlaceholderFlap || sr == null) return;

        Vector3 s = baseScale;
        // 朝向：水平速度决定翻转
        if (Mathf.Abs(rb.velocity.x) > 0.1f)
            s.x = rb.velocity.x > 0f ? Mathf.Abs(baseScale.x) : -Mathf.Abs(baseScale.x);
        // Y 缩放正弦 = 模拟扇翅
        s.y = Mathf.Abs(baseScale.y) * (1f + 0.25f * Mathf.Sin(Time.time * flapSpeed + wanderSeed));
        transform.localScale = s;
    }
}
