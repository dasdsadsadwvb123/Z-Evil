using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Events;
using System.Collections;
using System.Collections.Generic;

/// <summary>
/// 电锯哥 Boss AI（game1 教学 Boss）：
/// 巡逻（房内随机点位）→ 发现玩家（alertClip + 循环锯声提速）→ 追击（网格步进，同丧尸套路）
/// → 近身挥砍（前摇红 tint 预警 → attack 动画 → 动画中段判定伤害）→ 回到追击。
/// 动画：按小泽做好的 12 个状态名（全小写）用 CrossFade 播放，状态不存在只警告不崩。
/// 死亡：锯声熄火淡出（音效线）+ 身体序列（B+C 杂交，事件秒广播）。
/// 有 "death" 状态时：CrossFade 播真死亡动画（滑行叠加）→ 播完接挣扎段 → 90° 扑倒；
/// 没有时：退回代码旋转的倒地→挣扎→扑倒序列（向后兼容，不崩）。
/// 尸体：corpseFadeDuration 默认 0 = 永久躺地上（调试时可改 >0 开渐隐）。
/// 血条：RE 式隐藏血条——代码存在但 showHealthBar 默认 false（屏幕上永不渲染），
/// 玩家靠狂暴等阶段变化感知进度；小泽调试时自己勾上才显示。
/// 挂到 Boss 物体上（需要：SpriteRenderer + BoxCollider2D(Trigger) + Rigidbody2D(Kinematic)
/// + HealthSystem(maxHealth=30) + 小泽的 Animator + 本脚本）。
/// </summary>
public class BossSawAI : MonoBehaviour
{
    // ======== 动画状态名常量（小泽的 Animator 里就这些名字，全小写） ========
    private const string IdleDown = "idledown", IdleUp = "idleup", IdleRight = "idleright", IdleLeft = "idleleft";
    private const string WalkDown = "walkdown", WalkUp = "walkup", WalkRight = "walkright", WalkLeft = "walkleft";
    private const string AttackDown = "attackdown", AttackUp = "attackup", AttackRight = "attackright", AttackLeft = "attackleft";

    [Header("移动（网格步进，和丧尸同套路）")]
    [Tooltip("一格多大（和场景格子一致，一般 1）")]
    public float gridSize = 1f;
    [Tooltip("巡逻速度")]
    public float patrolSpeed = 1.5f;
    [Tooltip("追击速度（介于丧尸 4 和玩家之间）")]
    public float chaseSpeed = 3.5f;
    [Tooltip("发现玩家的视野距离")]
    public float aggroRange = 6f;
    [Tooltip("巡逻范围：出生点周围多大的圈里随机巡逻")]
    public float patrolRadius = 4f;
    [Tooltip("墙/障碍所在 Layer（IsWalkable 检测用，Inspector 里选墙的 Layer）")]
    public LayerMask obstacleLayer;

    [Header("攻击")]
    [Tooltip("进入这个距离开始挥砍")]
    public float attackRange = 0.9f;
    [Tooltip("挥砍伤害")]
    public int attackDamage = 2;
    [Tooltip("攻击前摇（秒）：红 tint 预警 + 锯声拉高，给玩家反应窗口")]
    public float windupTime = 0.45f;
    [Tooltip("攻击间隔（秒）：从起手到下次能再起手")]
    public float attackCooldown = 1.8f;

    [Header("残血狂暴（一次性触发）")]
    public bool enableEnrage = true;
    [Tooltip("血量低于这个比例触发狂暴（0.3 = 30%）")]
    [Range(0.05f, 0.9f)]
    public float enrageThreshold = 0.3f;
    [Tooltip("狂暴后移速倍率")]
    public float enrageSpeedMul = 1.15f;
    [Tooltip("狂暴后攻击间隔倍率")]
    public float enrageAttackMul = 0.7f;

    [Header("音效（素材小泽自己拖，不拖 = 静音不报错）")]
    [Tooltip("登场循环锯声：挂机就响，死亡停止")]
    public AudioClip chainsawLoopClip;
    [Tooltip("每次挥砍播一声")]
    public AudioClip attackSoundClip;
    [Tooltip("第一次发现玩家播一声")]
    public AudioClip alertClip;
    [Tooltip("追击时锯声音调（1 = 原速，1.3 = 提速预警感）")]
    public float chasePitch = 1.3f;
    [Tooltip("循环锯声音量")]
    [Range(0f, 1f)]
    public float loopVolume = 0.8f;

    [Header("声音随距离淡出/淡入（跟视野联动，出视野渐弱）")]
    [Tooltip("总开关：关掉 = 声音永远正常大小（不随距离变化）")]
    public bool soundDistanceEnabled = true;
    [Tooltip("音量随视野距离联动：视野内全声，出视野淡出至 视野×倍数 处静音（改视野范围 Aggro Range，无声边界自动跟着动）")]
    public float silentMultiplier = 1.5f;
    [Tooltip("淡入淡出速度：越大变化越快（2 = 大约半秒从有声淡到无声）")]
    public float soundFadeSpeed = 2f;

    [Header("血条（RE 式隐藏血条：默认关闭不渲染，玩家靠狂暴感知阶段；调试时才勾开）")]
    [Tooltip("默认 false = 血条代码在但屏幕上永不显示（生化危机老传统）；调试用才勾上")]
    public bool showHealthBar = false;
    [Tooltip("血条上显示的名字")]
    public string bossDisplayName = "电锯哥";

    [Header("死亡：锯声熄火（贯穿全程的音效线）")]
    [Tooltip("死亡锯声熄火淡出时长（秒）：建议 ≈ 死亡序列总时长，别出现'声音没了身体还在扑腾'")]
    public float chainsawFadeDuration = 4.2f;
    [Tooltip("熄火结束时锯声 pitch（0.4 左右 = 引擎憋到熄火的低沉突突声）")]
    public float deathEndPitch = 0.4f;
    [Tooltip("熄火抖动幅度：pitch 每小段随机 ±这个值，模拟链锯引擎挣扎")]
    public float deathPitchJitter = 0.1f;

    [Header("死亡：身体序列（失控滑行 → 倒地 → 挣扎 → 扑倒 → 渐隐/永久）")]
    [Tooltip("死亡瞬间失控滑行速度（格/秒），沿最后朝向冲")]
    public float deathSlideSpeed = 6f;
    [Tooltip("滑行最大距离（格），撞墙会提前截停")]
    public float deathSlideMaxDist = 3f;
    [Tooltip("滑行停下后倒地时长（秒）：旋转到 ~85° 贴地")]
    public float deathFallTime = 0.3f;
    [Tooltip("倒地后躺多久开始挣扎（秒）")]
    public float deathStruggleDelay = 1f;
    [Tooltip("挣扎半撑起时长（秒）：转回 ~40° + 剧烈抖动")]
    public float deathStruggleTime = 0.6f;
    [Tooltip("挣扎后彻底扑倒时长（秒）：转满 90° 贴地")]
    public float deathCollapseTime = 0.3f;
    [Tooltip("扑倒后尸体渐隐时长（秒）；默认 0 = 尸体永久躺地上不消失（Boss 默认开箱即永久）")]
    public float corpseFadeDuration = 0f;
    [Tooltip("Boss 死亡事件（可接掉落/开门/剧情，死亡瞬间立刻广播）")]
    public UnityEvent onBossDeath;

    // ---- 运行时状态 ----
    private Rigidbody2D rb;
    private SpriteRenderer sr;
    private Animator animator;
    private HealthSystem myHealth;
    private Transform player;
    private HealthSystem playerHealth;
    private AudioSource audioSource;

    private bool dead = false;
    private bool isMoving = false;
    private bool isAttacking = false;
    private bool hasSeenPlayer = false;   // 发现过玩家 = 永久仇恨（Boss 房不脱战）
    private bool enraged = false;
    private Vector2 homePos;              // 出生点 = 巡逻中心
    private Vector2 patrolTarget;         // 当前巡逻目标格
    private bool hasPatrolTarget = false;
    private Vector2 targetGridPos;        // 正在走过去的格
    private Vector2 currentFacing = Vector2.down;
    private float nextAttackTime = 0f;
    private Color baseColor = Color.white; // 当前"基础"tint（白=正常，红=前摇预警）
    private float soundScale = 1f;         // 距离音量系数（1=正常，0=无声），每帧向目标平滑过渡
    private Coroutine flashRoutine;
    private readonly HashSet<string> warnedStates = new HashSet<string>(); // 缺失状态只警告一次

    // ---- 血条 UI 引用（RE 式隐藏血条：默认不渲染，代码仍在） ----
    private GameObject barRoot;
    private RectTransform fillRect;

    private void Start()
    {
        rb = GetComponent<Rigidbody2D>();
        if (rb == null)
        {
            rb = gameObject.AddComponent<Rigidbody2D>();
        }
        rb.bodyType = RigidbodyType2D.Kinematic; // 网格步进，不吃物理推挤
        rb.freezeRotation = true;

        if (GetComponent<Collider2D>() == null)
        {
            BoxCollider2D col = gameObject.AddComponent<BoxCollider2D>();
            col.isTrigger = true;
            col.size = new Vector2(0.9f, 0.9f);
        }

        sr = GetComponent<SpriteRenderer>();
        animator = GetComponent<Animator>();

        GameObject playerObj = GameObject.FindGameObjectWithTag("Player");
        if (playerObj != null)
        {
            player = playerObj.transform;
            playerHealth = playerObj.GetComponent<HealthSystem>();
        }

        myHealth = GetComponent<HealthSystem>();
        if (myHealth != null)
        {
            myHealth.deathTriggerName = "";   // Boss 没有死亡动画状态，死亡表现由本脚本接管
            myHealth.destroyOnDeath = false;
            myHealth.OnDamaged += OnHurt;     // 受击闪白
            myHealth.OnDeath += OnBossDeath;
        }
        else
        {
            Debug.LogWarning("[电锯哥] 身上没有 HealthSystem，打不死也死不了！", gameObject);
        }

        // 循环锯声：登场就响（乌鸦 flock 同款自动补音源）
        audioSource = GetComponent<AudioSource>();
        if (audioSource == null) audioSource = gameObject.AddComponent<AudioSource>();
        audioSource.playOnAwake = false;
        audioSource.loop = true;
        audioSource.clip = chainsawLoopClip;
        audioSource.volume = loopVolume;
        audioSource.pitch = 1f;
        if (chainsawLoopClip != null) audioSource.Play();

        homePos = rb.position;
        targetGridPos = rb.position;
        SnapToGrid();

        // 血条照样生成（默认 showHealthBar=false → 创建后立即隐藏，永不渲染）
        if (showHealthBar) CreateHealthBar();
    }

    private void OnDestroy()
    {
        if (myHealth != null)
        {
            myHealth.OnDamaged -= OnHurt;
            myHealth.OnDeath -= OnBossDeath;
        }
    }

    private void Update()
    {
        if (dead) return;

        // 狂暴检查（一次性）
        if (!enraged && enableEnrage && myHealth != null && myHealth.maxHealth > 0)
        {
            float ratio = (float)myHealth.currentHealth / myHealth.maxHealth;
            if (ratio < enrageThreshold)
            {
                enraged = true;
                chaseSpeed *= enrageSpeedMul;
                patrolSpeed *= enrageSpeedMul;
                attackCooldown *= enrageAttackMul;
                Debug.Log("[电锯哥] 进入狂暴！移速 x" + enrageSpeedMul + "，攻速 x" + (1f / enrageAttackMul), gameObject);
            }
        }

        // 锯声音调：巡逻 = 1，追击/战斗 = 1.3（平滑过渡，"越追越凶"）
        float targetPitch = hasSeenPlayer ? chasePitch : 1f;
        audioSource.pitch = Mathf.Lerp(audioSource.pitch, targetPitch, 3f * Time.deltaTime);

        // 声音随距离淡出/淡入（dead=true 时上面已 return，死亡熄火淡出不受这里干扰）
        UpdateSoundScale();

        // 血条刷新（RE 式：showHealthBar=false 时 barRoot 为 null 或隐藏，这里自然跳过）
        if (barRoot != null && barRoot.activeSelf && myHealth != null && myHealth.maxHealth > 0)
        {
            float ratio = Mathf.Clamp01((float)myHealth.currentHealth / myHealth.maxHealth);
            fillRect.anchorMax = new Vector2(ratio, 1f);
        }

        if (isAttacking) return;

        float dist = PlayerDist();

        // 进入攻击距离 → 挥砍（受攻击间隔限制）
        if (dist <= attackRange && Time.time >= nextAttackTime)
        {
            StartCoroutine(DoAttack());
            return;
        }
        if (isMoving) return;

        if (dist <= aggroRange)
        {
            if (!hasSeenPlayer)
            {
                hasSeenPlayer = true; // 首次发现：alert 一声，之后永久仇恨
                if (alertClip != null) audioSource.PlayOneShot(alertClip, soundScale); // 第二个参数 = 距离系数：远处的发现声也变小
                // 进战显示血条——RE 式：showHealthBar=false（默认）时这里什么都不发生
                if (showHealthBar && barRoot != null) barRoot.SetActive(true);
                hasPatrolTarget = false;
            }
            ChaseStep();
        }
        else
        {
            PatrolStep();
        }
    }

    private void FixedUpdate()
    {
        if (dead || isAttacking) return;
        if (isMoving)
        {
            float speed = hasSeenPlayer ? chaseSpeed : patrolSpeed;
            Vector2 next = Vector2.MoveTowards(rb.position, targetGridPos, speed * Time.fixedDeltaTime);
            rb.position = next;
            if (Vector2.Distance(rb.position, targetGridPos) < 0.001f)
            {
                rb.position = targetGridPos;
                isMoving = false;
                SetIdleAnim(); // 走到格了 → 静止 idle（按当前朝向）
            }
        }
    }

    // ======== 巡逻 / 追击（网格步进，参照 ZombieAI 套路） ========

    private void PatrolStep()
    {
        // 没目标或走到了 → 在出生点圈内随机挑一个可走的格
        if (!hasPatrolTarget || Vector2.Distance(rb.position, patrolTarget) < 0.1f)
        {
            hasPatrolTarget = PickPatrolTarget();
            if (!hasPatrolTarget) return;
        }
        if (!StepToward(patrolTarget))
            hasPatrolTarget = false; // 被堵死 → 换个目标
    }

    private bool PickPatrolTarget()
    {
        for (int i = 0; i < 10; i++) // 最多试 10 次
        {
            Vector2 candidate = homePos + Random.insideUnitCircle * patrolRadius;
            candidate.x = Mathf.Round(candidate.x / gridSize) * gridSize;
            candidate.y = Mathf.Round(candidate.y / gridSize) * gridSize;
            if (IsWalkable(candidate))
            {
                patrolTarget = candidate;
                return true;
            }
        }
        return false;
    }

    /// <summary> 追击：四方向里挑"走完离玩家最近"的可走格（ZombieAI 同款贪心） </summary>
    private void ChaseStep()
    {
        if (player == null) return;
        Vector2 diff = (Vector2)player.position - rb.position;

        Vector2 dir1, dir2;
        if (Mathf.Abs(diff.x) >= Mathf.Abs(diff.y))
        {
            dir1 = new Vector2(Mathf.Sign(diff.x), 0);
            dir2 = new Vector2(0, Mathf.Sign(diff.y));
        }
        else
        {
            dir1 = new Vector2(0, Mathf.Sign(diff.y));
            dir2 = new Vector2(Mathf.Sign(diff.x), 0);
        }

        Vector2[] candidates = new Vector2[] { dir1, dir2, -dir1, -dir2 };
        Vector2 bestDir = Vector2.zero;
        float bestDist = float.MaxValue;
        foreach (Vector2 d in candidates)
        {
            Vector2 next = targetGridPos + d * gridSize;
            if (!IsWalkable(next)) continue;
            float dDist = Vector2.Distance(next, player.position);
            if (dDist < bestDist)
            {
                bestDist = dDist;
                bestDir = d;
            }
        }

        if (bestDir != Vector2.zero)
        {
            targetGridPos += bestDir * gridSize;
            isMoving = true;
            currentFacing = bestDir;
            PlayState("walk" + DirSuffix(bestDir));
        }
        else
        {
            SetIdleAnim(); // 四面被堵：站住瞪着玩家
        }
    }

    /// <summary> 朝目标格走一步（巡逻用：主方向堵了走副方向） </summary>
    private bool StepToward(Vector2 target)
    {
        Vector2 diff = target - rb.position;
        if (diff.magnitude < 0.05f) return false;

        Vector2 dir1, dir2;
        if (Mathf.Abs(diff.x) >= Mathf.Abs(diff.y))
        {
            dir1 = new Vector2(Mathf.Sign(diff.x), 0);
            dir2 = new Vector2(0, Mathf.Sign(diff.y));
        }
        else
        {
            dir1 = new Vector2(0, Mathf.Sign(diff.y));
            dir2 = new Vector2(Mathf.Sign(diff.x), 0);
        }

        foreach (Vector2 d in new Vector2[] { dir1, dir2 })
        {
            Vector2 next = targetGridPos + d * gridSize;
            if (IsWalkable(next))
            {
                targetGridPos = next;
                isMoving = true;
                currentFacing = d;
                PlayState("walk" + DirSuffix(d));
                return true;
            }
        }
        return false; // 两个方向都堵 → 失败
    }

    private bool IsWalkable(Vector2 pos)
    {
        if (Physics2D.OverlapBox(pos, Vector2.one * gridSize * 0.9f, 0f, obstacleLayer) != null)
            return false;
        Vector2 from = targetGridPos;
        Vector2 toDir = (pos - from).normalized;
        float toDist = Vector2.Distance(from, pos);
        if (Physics2D.Raycast(from, toDir, toDist, obstacleLayer).collider != null)
            return false;
        return true;
    }

    // ======== 攻击 ========

    private IEnumerator DoAttack()
    {
        isAttacking = true;
        nextAttackTime = Time.time + attackCooldown; // 狂暴时 attackCooldown 已被乘小，直接用

        // 前摇预警：面向玩家 + 红 tint + 挥砍音 + 锯声拉高
        Vector2 dir = DirectionToPlayer();
        currentFacing = dir;
        baseColor = new Color(1f, 0.35f, 0.3f); // 预警红
        ApplyTint();
        if (attackSoundClip != null) audioSource.PlayOneShot(attackSoundClip, soundScale); // 第二个参数 = 距离系数：远处的挥砍声也变小
        audioSource.pitch = chasePitch;

        yield return new WaitForSeconds(windupTime);

        // 播 attack 状态（按攻击朝向）
        PlayState("attack" + DirSuffix(dir));

        // 动画中段判定一次伤害
        yield return new WaitForSeconds(0.2f);
        if (!dead && PlayerDist() <= attackRange + 0.4f && playerHealth != null && !playerHealth.isDead)
            playerHealth.TakeDamage(attackDamage);

        // 攻击后摇
        yield return new WaitForSeconds(0.25f);

        if (!dead)
        {
            baseColor = Color.white; // 恢复正常 tint
            ApplyTint();
            SetIdleAnim();
        }
        isAttacking = false;
    }

    // ======== 受击 / 死亡 ========

    private void OnHurt()
    {
        if (dead) return;
        if (flashRoutine != null) StopCoroutine(flashRoutine);
        flashRoutine = StartCoroutine(WhiteFlashRoutine());
    }

    /// <summary> 受击闪白 0.1 秒（纯代码 tint，不占动画） </summary>
    private IEnumerator WhiteFlashRoutine()
    {
        sr.color = Color.white;
        yield return new WaitForSeconds(0.1f);
        sr.color = baseColor;
        flashRoutine = null;
    }

    private void OnBossDeath()
    {
        if (dead) return;
        dead = true;

        // 事件立刻广播（时机不变，接掉落/剧情不用等序列播完）
        onBossDeath?.Invoke();

        // 双线并行：锯声熄火（音效线）+ 身体死亡序列（滑行→倒地→挣扎→扑倒→渐隐/永久）
        StartCoroutine(ChainsawDeathFadeRoutine());
        StartCoroutine(DeathSequenceRoutine());
    }

    /// <summary>
    /// 死亡锯声"熄火式淡出"（音效线）：音量渐降到 0，pitch 从当前值（追击时 1.3）滑落到
    /// deathEndPitch，中间带随机抖动——模拟链锯引擎"突突突……噗"的挣扎感。
    /// 只管声音，不管销毁（Boss 物体由 DeathSequenceRoutine 收尾）。
    /// </summary>
    private IEnumerator ChainsawDeathFadeRoutine()
    {
        if (audioSource == null) yield break;

        float startVolume = audioSource.volume;
        float startPitch = audioSource.pitch;
        float jitter = 0f;
        float jitterTimer = 0f;
        float t = 0f;

        while (t < chainsawFadeDuration)
        {
            t += Time.deltaTime;
            float k = Mathf.Clamp01(t / chainsawFadeDuration);

            // 音量线性渐降
            audioSource.volume = Mathf.Lerp(startVolume, 0f, k);
            // pitch 平滑滑落 + 每 0.1 秒换一次随机抖动（不是每帧抖，那样会变成电流噪）
            jitterTimer += Time.deltaTime;
            if (jitterTimer >= 0.1f)
            {
                jitterTimer = 0f;
                jitter = Random.Range(-deathPitchJitter, deathPitchJitter);
            }
            audioSource.pitch = Mathf.Lerp(startPitch, deathEndPitch, k) + jitter;

            yield return null;
        }

        // 彻底熄火
        audioSource.Stop();
    }

    /// <summary>
    /// 死亡身体序列（B+C 杂交 + death 状态接入）：
    /// 0. 有 "death" 动画状态 → 死亡瞬间 CrossFade 播真动画（替代代码旋转的倒地姿态）；
    ///    没有 → 退回代码旋转序列（向后兼容，不崩）
    /// 1. 失控滑行（C 段保留）：物理位移和死亡动画叠加 = "被击飞后边滑边倒下"；
    ///    撞墙/到最大距离截停，身体 ±2° 抖
    /// 2. 有真动画：动画播完定格最后帧 → 接挣扎段（40° 半撑起抖动 → 90° 彻底扑倒）；
    ///    没真动画：倒地 85° → 接同一段挣扎段（两条路共用）
    /// 3. 收尾：corpseFadeDuration 渐隐后销毁；默认 0 = 尸体永久躺地上（Boss 默认永久）
    /// </summary>
    private IEnumerator DeathSequenceRoutine()
    {
        if (sr == null)
        {
            Destroy(gameObject);
            yield break;
        }

        // ---- 0. 检查有没有真死亡动画（有就直接播，绕过 dead 守卫） ----
        bool hasDeathAnim = false;
        if (animator != null)
        {
            int deathHash = Animator.StringToHash("death");
            hasDeathAnim = animator.HasState(0, deathHash);
            if (hasDeathAnim)
                animator.CrossFade(deathHash, 0.05f, 0, 0f); // PlayState 有 dead 守卫拦着，死亡动画这里直接播
            else
                Debug.LogWarning("[电锯哥] Animator 里没有 \"death\" 状态，退回代码旋转倒地序列", gameObject);
        }

        // ---- 1. 失控滑行（C 段保留：边滑边播死亡动画） ----
        Vector2 slideDir = currentFacing == Vector2.zero ? Vector2.down : currentFacing;
        float slid = 0f;
        while (slid < deathSlideMaxDist)
        {
            float step = Mathf.Min(deathSlideSpeed * Time.deltaTime, deathSlideMaxDist - slid);
            Vector2 next = rb.position + slideDir * step;
            // 撞墙物理截停
            if (Physics2D.OverlapBox(next, Vector2.one * gridSize * 0.6f, 0f, obstacleLayer) != null)
                break;
            slid += step;
            rb.MovePosition(next);
            sr.transform.localRotation = Quaternion.Euler(0f, 0f, Random.Range(-2f, 2f)); // ±2° 抖
            yield return null;
        }
        sr.transform.localRotation = Quaternion.identity;

        // 倒地方向：朝左往一边倒，其余往另一边（两条路线共用）
        float fallSign = (currentFacing.x < 0f) ? 1f : -1f;

        // ---- 2. 倒地姿态：真动画 vs 代码旋转（二选一），之后都接挣扎段 ----
        if (hasDeathAnim)
        {
            // 真动画路线：等 death 播完，身体自然定格最后一帧（Animator 不 Loop 就停在那）
            // 最多等 5 秒——如果小泽不小心把 death 状态设了 Loop，超时后照样接挣扎段，不会卡死
            float wait = 0f;
            while (wait < 5f)
            {
                wait += Time.deltaTime;
                AnimatorStateInfo info = animator.GetCurrentAnimatorStateInfo(0);
                if (info.IsName("death") && info.normalizedTime >= 1f) break;
                yield return null;
            }
        }
        else
        {
            // 代码旋转路线（原 B 段）：倒地 85°
            yield return RotateSpriteTo(85f * fallSign, deathFallTime);
        }

        // ---- 2.5 挣扎段（两条路线共用）：躺一会 → 40° 半撑起抖动 → 90° 彻底扑倒 ----
        yield return StruggleAndCollapse(fallSign);

        // ---- 3. 收尾：默认 0 = 永久躺地；调试可把 Corpse Fade Duration 填 >0 开渐隐 ----
        if (corpseFadeDuration <= 0f) yield break; // 永久尸体
        float ft = 0f;
        Color c = sr.color;
        while (ft < corpseFadeDuration)
        {
            ft += Time.deltaTime;
            c.a = Mathf.Clamp01(1f - ft / corpseFadeDuration);
            sr.color = c;
            yield return null;
        }
        Destroy(gameObject);
    }

    /// <summary>
    /// 挣扎段：躺 deathStruggleDelay 秒 → deathStruggleTime 秒内转回 ~40° 半撑起 + 剧烈抖动
    /// → deathCollapseTime 秒彻底扑倒 90° 贴地。真动画路线和回退路线共用。
    /// </summary>
    private IEnumerator StruggleAndCollapse(float fallSign)
    {
        yield return new WaitForSeconds(deathStruggleDelay);
        float struggleT = 0f;
        while (struggleT < deathStruggleTime)
        {
            struggleT += Time.deltaTime;
            float k = Mathf.Clamp01(struggleT / deathStruggleTime);
            float z = Mathf.Lerp(85f * fallSign, 40f * fallSign, k) + Random.Range(-6f, 6f);
            sr.transform.localRotation = Quaternion.Euler(0f, 0f, z);
            yield return null;
        }
        yield return RotateSpriteTo(90f * fallSign, deathCollapseTime);
    }

    /// <summary> 把 Sprite 的 z 旋转从当前角度平滑转到目标角度 </summary>
    private IEnumerator RotateSpriteTo(float targetZ, float duration)
    {
        float startZ = sr.transform.localEulerAngles.z;
        if (startZ > 180f) startZ -= 360f; // 规范化到 -180~180，避免绕远路
        float t = 0f;
        while (t < duration)
        {
            t += Time.deltaTime;
            float z = Mathf.LerpAngle(startZ, targetZ, Mathf.Clamp01(t / duration));
            sr.transform.localRotation = Quaternion.Euler(0f, 0f, z);
            yield return null;
        }
        sr.transform.localRotation = Quaternion.Euler(0f, 0f, targetZ);
    }

    // ======== 动画（按状态名播放，缺失只警告不崩） ========

    private void SetIdleAnim()
    {
        PlayState("idle" + DirSuffix(currentFacing));
    }

    private string DirSuffix(Vector2 dir)
    {
        if (dir == Vector2.up) return "up";
        if (dir == Vector2.left) return "left";
        if (dir == Vector2.right) return "right";
        return "down";
    }

    /// <summary> 按状态名切动画：HasState 检查，不存在警告一次（不崩） </summary>
    private void PlayState(string stateName)
    {
        if (animator == null || dead) return;
        int hash = Animator.StringToHash(stateName);
        if (!animator.HasState(0, hash))
        {
            if (!warnedStates.Contains(stateName))
            {
                warnedStates.Add(stateName);
                Debug.LogWarning("[电锯哥] Animator 里没有状态 \"" + stateName + "\"，跳过播放（不崩）", gameObject);
            }
            return;
        }
        animator.CrossFade(hash, 0.05f, 0, 0f);
    }

    // ======== 工具 ========

    private float PlayerDist()
    {
        if (player == null) return float.MaxValue;
        return Vector2.Distance(rb.position, player.position);
    }

    // ======== 声音随距离淡出/淡入 ========

    /// <summary>
    /// 每帧算一个"距离音量系数"soundScale（1=正常，0=无声）：
    /// 跟小泽设的视野范围 aggroRange 联动——视野内（≤ aggroRange）全声；
    /// 出视野开始淡出，到 aggroRange × silentMultiplier 处完全无声，
    /// 过渡带自动随视野伸缩：只改 aggroRange 一个数，声音范围就跟着动。
    /// 当前系数每帧向目标平滑靠近 → 走远时缓慢变小，走回来时缓慢变大。
    /// 只在活着的 Update 里被调用，死亡熄火淡出（ChainsawDeathFadeRoutine）不受影响。
    /// </summary>
    private void UpdateSoundScale()
    {
        // 1. 算目标系数（无声边界 = 视野范围 × 倍数）
        float target;
        if (!soundDistanceEnabled)
        {
            target = 1f; // 总开关关掉 → 永远正常音量
        }
        else
        {
            float dist = PlayerDist();
            float silentRange = aggroRange * silentMultiplier; // 无声距离随视野自动伸缩
            if (dist <= aggroRange) target = 1f;               // 视野内：全音量
            else if (dist >= silentRange) target = 0f;         // 视野×倍数外：无声
            else target = (silentRange - dist) / (silentRange - aggroRange); // 过渡带：线性插值
        }

        // 2. 当前系数向目标平滑过渡（淡入淡出都靠这一行）
        soundScale = Mathf.Lerp(soundScale, target, soundFadeSpeed * Time.deltaTime);

        // 3. 循环锯声：音量 = 基础音量 × 距离系数
        audioSource.volume = loopVolume * soundScale;

        // 4. 省资源：系数到 0 就 Stop；重新进入范围就从头 Play（"重新出现时声音重新播放"）
        if (soundScale <= 0.01f)
        {
            if (audioSource.isPlaying) audioSource.Stop();
        }
        else if (chainsawLoopClip != null && !audioSource.isPlaying)
        {
            audioSource.Play();
        }
    }

    private Vector2 DirectionToPlayer()
    {
        if (player == null) return currentFacing;
        Vector2 diff = (Vector2)player.position - rb.position;
        if (Mathf.Abs(diff.x) >= Mathf.Abs(diff.y))
            return new Vector2(Mathf.Sign(diff.x), 0);
        return new Vector2(0, Mathf.Sign(diff.y));
    }

    private void ApplyTint()
    {
        if (sr != null) sr.color = baseColor;
    }

    private void SnapToGrid()
    {
        Vector2 pos = rb.position;
        pos.x = Mathf.Round(pos.x / gridSize) * gridSize;
        pos.y = Mathf.Round(pos.y / gridSize) * gridSize;
        rb.position = pos;
    }

    // ======== 血条 UI（RE 式隐藏血条：代码完整保留，showHealthBar 默认 false 不渲染） ========

    private void CreateHealthBar()
    {
        // 最外层 Canvas（排序 310：在背包 200 之上、红闪 320 之下）
        GameObject canvasGO = new GameObject("BossBarCanvas", typeof(RectTransform)); // UI 物体必须带 RectTransform
        Canvas canvas = canvasGO.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 310;
        canvasGO.AddComponent<CanvasScaler>().uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        canvasGO.AddComponent<GraphicRaycaster>();

        barRoot = new GameObject("BossBarRoot", typeof(RectTransform));
        barRoot.transform.SetParent(canvasGO.transform, false);
        RectTransform rootRect = barRoot.GetComponent<RectTransform>();
        rootRect.anchorMin = new Vector2(0.5f, 1f);
        rootRect.anchorMax = new Vector2(0.5f, 1f);
        rootRect.pivot = new Vector2(0.5f, 1f);
        rootRect.anchoredPosition = new Vector2(0f, -24f);
        rootRect.sizeDelta = new Vector2(420f, 34f);

        // 黑底条
        GameObject bg = new GameObject("BG", typeof(RectTransform));
        bg.transform.SetParent(barRoot.transform, false);
        Image bgImg = bg.AddComponent<Image>();
        bgImg.color = new Color(0f, 0f, 0f, 0.7f);
        RectTransform bgRect = bgImg.GetComponent<RectTransform>();
        bgRect.anchorMin = new Vector2(0.5f, 0.5f);
        bgRect.anchorMax = new Vector2(0.5f, 0.5f);
        bgRect.pivot = new Vector2(0.5f, 0.5f);
        bgRect.anchoredPosition = Vector2.zero;
        bgRect.sizeDelta = new Vector2(420f, 26f);

        // 红色血量填充（改 anchorMax.x 控制比例）
        GameObject fill = new GameObject("Fill", typeof(RectTransform));
        fill.transform.SetParent(bg.transform, false);
        Image fillImg = fill.AddComponent<Image>();
        fillImg.color = new Color(0.85f, 0.1f, 0.1f); // 血红
        fillRect = fillImg.GetComponent<RectTransform>();
        fillRect.anchorMin = new Vector2(0f, 0f);
        fillRect.anchorMax = new Vector2(1f, 1f); // 初始满血
        fillRect.offsetMin = new Vector2(3f, 3f);
        fillRect.offsetMax = new Vector2(-3f, -3f);

        // 白字名字（盖在血条上）
        GameObject nameGO = new GameObject("Name", typeof(RectTransform));
        nameGO.transform.SetParent(bg.transform, false);
        Text nameText = nameGO.AddComponent<Text>();
        nameText.text = bossDisplayName;
        nameText.fontSize = 16;
        nameText.color = Color.white;
        nameText.alignment = TextAnchor.MiddleCenter;
        nameText.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        RectTransform nameRect = nameText.GetComponent<RectTransform>();
        nameRect.anchorMin = Vector2.zero;
        nameRect.anchorMax = Vector2.one;
        nameRect.offsetMin = Vector2.zero;
        nameRect.offsetMax = Vector2.zero;

        // 初始隐藏：发现玩家（进战）且 showHealthBar=true 才显示
        barRoot.SetActive(false);
    }
}
