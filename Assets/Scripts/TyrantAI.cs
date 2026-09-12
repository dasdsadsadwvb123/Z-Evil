using UnityEngine;
using UnityEngine.Events;
using System.Collections;
using System.Collections.Generic;

/// <summary>
/// 暴君 Boss AI（game3 压轴，架构照搬 BossSawAI）：
/// 巡逻（房内随机点位）→ 发现玩家（alertClip）→ 追击（网格步进贪心，同丧尸/电锯哥）
/// → 近身攻击（前摇红 tint → attack 动画 → 动画中段判定伤害）→ 回到追击。
/// 【招牌机制·快速冲刺】追击中按概率发动：前摇蓄力闪烁（原地不动警示）→ 直线高速冲向玩家
/// （可轻微追踪）→ 冲到身前立刻接攻击动画；有冷却和最大冲刺距离；【撞墙硬直】冲刺撞墙 =
/// 原地懵住 wallStunDuration 秒（复用 Stun），玩家反制窗口。
/// 【防撞墙死循环】冲刺前预检起点方向（贴墙/隔墙不冲）+ 撞墙后禁冲期 wallHitDashBan
/// （叠加在冷却与硬直之上）+ 连续撞墙递增（step 递增、max 封顶），顺畅通完一次冲刺计数清零。
/// 【残血狂化二阶段】血量 ≤ rageThreshold 一次性切换：提速 + 冲刺概率翻倍（封顶必冲）+
/// 音效 pitch 上调 + 体色暗红 + 场景灯光压暗（经 DarknessController.globalDimFactor）。
/// 【脚步震屏】移动时按步频：脚步声（距离听声）+ CameraShaker.Shake，强度随距离衰减。
/// 【罐子爆破联动】ExplosiveBarrel 调 TakeDamage(伤害) + Stun(僵直秒数)。
/// 【假死暴起终局】血量归零不直接死：倒下定格（假死）→ 玩家靠近 fakeDeathTriggerDistance
/// → 暴起扑击（二阶段倍率）→ 接一次攻击 → 才真死（onTyrantDeath 此时才广播）；
/// 假死中被打 → 跳过扑击直接真死。状态机：Alive → FakeDeath → Lunging → Dead。
/// 动画：只播小泽做好的 12 个状态（HasState 防呆，缺失警告不崩），不创建/修改 Animator。
/// ⚠️ 暴君的状态名带空格（如 "attack move down"）——三个前缀字段 Inspector 可改，
///    实际状态名 = 前缀 + 方向（up/down/left/right），Console 警告会打出拼出来的完整名。
/// 挂到暴君物体上（需要：SpriteRenderer + Rigidbody2D(Kinematic) + HealthSystem + 小泽的 Animator + 本脚本）。
/// </summary>
public class TyrantAI : MonoBehaviour
{
    [Header("动画状态名前缀（实际状态名 = 前缀 + 方向；暴君状态名带空格，默认就是 'idle down' 这类）")]
    [Tooltip("待机状态前缀：'idle ' → idle down / idle up / idle left / idle right")]
    public string idlePrefix = "idle"; // 小泽动画命名：idledown/idleleft/idleright/idleup
    [Tooltip("移动状态前缀：'move ' → move down / move up / move left / move right")]
    public string movePrefix = "walk"; // 小泽动画命名：walkdown/walkleft/walkright/walkup
    [Tooltip("攻击状态前缀：'attack move ' → attack move down / attack move up / attack move left / attack move right")]
    public string attackPrefix = "attack"; // 小泽动画命名：attackdown/attackleft/attackright/attackup

    [Header("移动（网格步进，和丧尸/电锯哥同套路）")]
    [Tooltip("一格多大（和场景格子一致，一般 1）")]
    public float gridSize = 1f;
    [Tooltip("巡逻速度")]
    public float patrolSpeed = 1.5f;
    [Tooltip("追击速度")]
    public float chaseSpeed = 3.2f;
    [Tooltip("发现玩家的视野距离")]
    public float aggroRange = 7f;
    [Tooltip("巡逻范围：出生点周围多大的圈里随机巡逻")]
    public float patrolRadius = 4f;
    [Tooltip("墙/障碍所在 Layer（IsWalkable 检测用，Inspector 里选墙的 Layer）")]
    public LayerMask obstacleLayer;

    [Header("攻击")]
    [Tooltip("进入这个距离开始攻击")]
    public float attackRange = 1f;
    [Tooltip("攻击伤害")]
    public int attackDamage = 3;
    [Tooltip("攻击前摇（秒）：红 tint 预警，给玩家反应窗口")]
    public float windupTime = 0.5f;
    [Tooltip("攻击间隔（秒）：从起手到下次能再起手")]
    public float attackCooldown = 2f;

    [Header("快速冲刺（招牌机制）")]
    [Tooltip("冲刺检定发动概率（0~1；追击中每隔检定间隔掷一次）")]
    [Range(0f, 1f)]
    public float dashChance = 0.5f;
    [Tooltip("冲刺检定间隔（秒）：条件满足时每隔这么久掷一次概率，防每帧刷骰子")]
    public float dashRollInterval = 0.4f;
    [Tooltip("前摇蓄力时长（秒）：原地闪烁警示，不动")]
    public float dashTelegraph = 0.6f;
    [Tooltip("冲刺速度倍率（相对追击速度；3 = 三倍速冲脸）")]
    public float dashSpeedMultiplier = 3f;
    [Tooltip("冲刺冷却（秒）")]
    public float dashCooldown = 5f;
    [Tooltip("冲刺触发距离上限：玩家比这还远就不考虑冲（贴脸也不冲）")]
    public float dashMaxTriggerRange = 6f;
    [Tooltip("单次冲刺最大距离（格），冲满就停")]
    public float dashMaxDistance = 5f;
    [Tooltip("冲刺中轻微追踪的转向力度（0 = 完全直线，0.3 = 明显拐弯追人）")]
    [Range(0f, 1f)]
    public float dashTurnFactor = 0.15f;
    [Tooltip("冲刺收尾攻击延迟（秒）：冲到身前等这么久【必出手】（默认 0.1，太慢会打不到人）")]
    public float dashFollowUpDelay = 0.1f;
    [Tooltip("冲刺收尾的攻击距离余量（格）：PlayerDist ≤ 攻击距离 + 这个值 就算冲到了 → 出手")]
    public float dashFollowUpExtraRange = 0.3f;
    [Tooltip("冲刺收尾那刀（扑击）的前摇（秒）：默认 0.05，短到玩家几乎躲不掉（区别于普通攻击的长前摇）")]
    public float dashFollowUpWindup = 0.05f;
    [Tooltip("冲刺收尾那刀（扑击）的命中距离余量（格）：越大越难躲（默认 0.9，动画刚播即判定）")]
    public float dashFollowUpHitRange = 0.9f;

    [Header("音效（素材小泽自己拖，不拖 = 静音不报错）")]
    [Tooltip("登场循环低吼/脚步声：挂机就响，死亡停止")]
    public AudioClip loopClip;
    [Tooltip("第一次发现玩家播一声")]
    public AudioClip alertClip;
    [Tooltip("每次攻击播一声")]
    public AudioClip attackClip;
    [Tooltip("冲刺发动播一声（前摇时）")]
    public AudioClip dashClip;
    [Tooltip("循环声音量")]
    [Range(0f, 1f)]
    public float loopVolume = 0.8f;

    [Header("声音随距离淡出/淡入（跟视野联动，出视野渐弱）")]
    [Tooltip("总开关：关掉 = 声音永远正常大小（不随距离变化）")]
    public bool soundDistanceEnabled = true;
    [Tooltip("音量随视野距离联动：视野内全声，出视野淡出至 视野×倍数 处静音")]
    public float silentMultiplier = 1.5f;
    [Tooltip("淡入淡出速度：越大变化越快")]
    public float soundFadeSpeed = 2f;

    [Header("死亡")]
    [Tooltip("尸体渐隐时长（秒）；默认 0 = 尸体永久躺地上")]
    public float corpseFadeDuration = 0f;
    [Tooltip("暴君死亡事件（可接通关演出/开门/剧情；注意：走假死流程时在【真正死亡】那一刻才广播）")]
    public UnityEvent onTyrantDeath;

    [Header("撞墙硬直（冲刺反制窗口）")]
    [Tooltip("冲刺撞墙截停后的硬直时长（秒）：这段时间暴君不动，玩家可以安全输出")]
    public float wallStunDuration = 2.5f;
    [Tooltip("撞墙硬直播一声低吼（不拖 = 静音）")]
    public AudioClip wallStunClip;
    [Tooltip("撞墙后禁冲期（秒）：硬直结束再禁这么久不许冲刺——掐断\"冲→撞→恢复→再冲\"死循环")]
    public float wallHitDashBan = 3f;
    [Tooltip("连续撞墙每多撞一次，禁冲期再加这么多秒（0 = 不递增）")]
    public float wallHitDashBanStep = 3f;
    [Tooltip("禁冲期上限（秒）：连续撞墙递增到这里封顶")]
    public float wallHitDashBanMax = 10f;
    [Tooltip("冲刺前预检距离（格）：起点方向这段距离内有墙就放弃本次冲刺（贴墙/隔墙不无脑冲）")]
    public float dashPrecheckDistance = 1.2f;

    [Header("残血狂化二阶段（一次性切换）")]
    [Tooltip("狂化开关")]
    public bool enableRage = true;
    [Tooltip("血量低于这个比例触发狂化（0.3 = 30%）")]
    [Range(0.05f, 0.9f)]
    public float rageThreshold = 0.3f;
    [Tooltip("狂化后追击速度倍率")]
    public float rageSpeedMultiplier = 1.3f;
    [Tooltip("狂化后冲刺概率倍率（结果封顶 1 = 必冲）")]
    [Range(1f, 5f)]
    public float rageDashMultiplier = 2f;
    [Tooltip("狂化后循环音效 pitch 上调档位（1.15 = 提速压迫感，不加新音频文件）")]
    public float ragePitchBoost = 1.15f;
    [Tooltip("狂化时场景灯光压暗系数（经 DarknessController 全局系数，0.7 = 暗一档）")]
    [Range(0.3f, 1f)]
    public float rageDimFactor = 0.7f;

    [Header("低血视觉异变（狂化同时生效，纯代码）")]
    [Tooltip("狂化后的暴君体色 tint（暗红）")]
    public Color rageTint = new Color(0.8f, 0.3f, 0.3f);

    [Header("脚步震屏（移动时按步频，强度随距离衰减）")]
    [Tooltip("脚步声音效（不拖 = 只有震屏没声）")]
    public AudioClip footstepClip;
    [Tooltip("步频：每隔多少秒一步")]
    public float footstepInterval = 0.45f;
    [Tooltip("贴脸时震屏强度（越远越弱，超过最大距离不震）")]
    public float footstepShakeStrength = 0.25f;
    [Tooltip("震屏最大有效距离（格），超过就不震")]
    public float footstepShakeMaxDist = 12f;

    [Header("假死暴起（终局演出）")]
    [Tooltip("假死开关：血量归零不直接死，等玩家靠近再暴起扑击")]
    public bool fakeDeathEnabled = true;
    [Tooltip("假死状态下玩家靠近这个距离 → 暴起扑击")]
    public float fakeDeathTriggerDistance = 2.5f;
    [Tooltip("暴起瞬间的惊吓音效（独立空位，恒定音量不走距离衰减；不拖 = 静音）——正常冲刺仍播 Dash Clip")]
    public AudioClip lungeStingClip;
    [Tooltip("暴起惊吓音音量")]
    [Range(0f, 1f)]
    public float lungeStingVolume = 1f;
    [Tooltip("暴起瞬间广播（可接演出/音乐；TyrantIntro 自动订阅它来重启 Boss 音乐）")]
    public UnityEvent onLungeStart;
    [Tooltip("进入假死瞬间广播（第一次被打空血倒下那一刻；TyrantIntro 订阅它做 Boss 音乐渐停）")]
    public UnityEvent onFakeDeath;

    // ---- 运行时状态 ----
    private Rigidbody2D rb;
    private SpriteRenderer sr;
    private Animator animator;
    private HealthSystem myHealth;
    private Transform player;
    private HealthSystem playerHealth;
    private AudioSource audioSource;
    private AudioSource stingSource;      // 惊吓音专用（独立 2D 音源，恒定音量不受距离淡出/循环音量影响）

    /// <summary> 死亡四相状态机：存活 → 假死（等玩家靠近）→ 扑击 → 真死 </summary>
    private enum DeathPhase { Alive, FakeDeath, Lunging, Dead }

    private bool dead = false;            // 完全死亡（FinalizeDeath 才置 true；假死期不算）
    private DeathPhase deathPhase = DeathPhase.Alive;
    private bool isMoving = false;
    private bool isAttacking = false;
    private bool isDashing = false;
    private bool hasSeenPlayer = false;   // 发现过玩家 = 永久仇恨（Boss 房不脱战）
    private bool raged = false;           // 狂化二阶段（只切一次）
    private float stunUntil = 0f;         // 僵直截止时间（Stun() 设置）
    private Color normalTint = Color.white; // 当前"正常"体色（狂化后变暗红；攻击前摇红/闪白都恢复到它）
    private float footstepTimer = 0f;     // 脚步步频计时
    private Vector2 homePos;              // 出生点 = 巡逻中心
    private Vector2 patrolTarget;         // 当前巡逻目标格
    private bool hasPatrolTarget = false;
    private Vector2 targetGridPos;        // 正在走过去的格
    private Vector2 currentFacing = Vector2.down;
    private float nextAttackTime = 0f;
    private float nextDashTime = 0f;      // 冲刺冷却
    private float nextDashRoll = 0f;      // 下次冲刺概率检定时间
    private float noDashUntil = 0f;       // 禁冲截止时间（撞墙后设置，掐断撞墙死循环）
    private int consecutiveWallHits = 0;  // 连续撞墙次数（越多禁冲期越长；顺畅通完一次清零）
    private Vector2 lastMoveCheckPos;     // 上一帧位置（卡住诊断用）
    private float stuckMoveTimer = 0f;    // 连续没挪动的时长（卡住诊断用）
    private bool stuckWarned = false;     // 卡住警告是否已打过（一次性，动过后复位）
    private const float StuckWarnSeconds = 4f; // 连续这么久没挪动就打一次卡住警告
    private Color baseColor = Color.white;
    private float soundScale = 1f;        // 距离音量系数（1=正常，0=无声）
    private Coroutine flashRoutine;
    private Coroutine attackRoutine;
    private Coroutine dashRoutine;
    private readonly HashSet<string> warnedStates = new HashSet<string>(); // 缺失状态只警告一次
    private string deathKey;              // 世界进度表钥匙（1=假死 2=真死；读档还原用）
    private bool deathRestoreChecked = false; // 读档自查只跑一次（放 Update 首帧，等 HealthSystem.Start 跑完）

    private void Start()
    {
        CacheComponents();

        homePos = rb.position;
        targetGridPos = rb.position;
        SnapToGrid();
        lastMoveCheckPos = rb.position; // 卡住诊断基准位置
        deathKey = WorldState.KeyFor("Dead", this);
    }

    /// <summary> 缓存/补齐全部组件引用（幂等，可重复调用） </summary>
    private void CacheComponents()
    {
        if (rb == null)
        {
            rb = GetComponent<Rigidbody2D>();
            if (rb == null) rb = gameObject.AddComponent<Rigidbody2D>();
            rb.bodyType = RigidbodyType2D.Kinematic; // 网格步进，不吃物理推挤
            rb.freezeRotation = true;
        }

        if (GetComponent<Collider2D>() == null)
        {
            BoxCollider2D col = gameObject.AddComponent<BoxCollider2D>();
            col.isTrigger = true;
            col.size = new Vector2(0.9f, 0.9f);
        }

        if (sr == null) sr = GetComponent<SpriteRenderer>();
        if (animator == null) animator = GetComponent<Animator>();

        if (player == null)
        {
            GameObject playerObj = GameObject.FindGameObjectWithTag("Player");
            if (playerObj != null)
            {
                player = playerObj.transform;
                playerHealth = playerObj.GetComponent<HealthSystem>();
            }
        }

        if (myHealth == null)
        {
            myHealth = GetComponent<HealthSystem>();
            if (myHealth != null)
            {
                myHealth.deathTriggerName = "";   // 死亡表现由本脚本接管
                myHealth.destroyOnDeath = false;
                myHealth.OnDamaged += OnHurt;     // 受击闪白
                myHealth.OnDeath += OnTyrantDeath;
            }
            else
            {
                Debug.LogWarning("[暴君] 身上没有 HealthSystem，打不死也死不了！", gameObject);
            }
        }

        if (audioSource == null)
        {
            audioSource = GetComponent<AudioSource>();
            if (audioSource == null) audioSource = gameObject.AddComponent<AudioSource>();
            audioSource.playOnAwake = false;
            audioSource.loop = true;
            audioSource.clip = loopClip;
            audioSource.volume = loopVolume;
            audioSource.pitch = 1f;
            if (loopClip != null) audioSource.Play();
        }
    }

    private void OnDestroy()
    {
        if (myHealth != null)
        {
            myHealth.OnDamaged -= OnHurt;
            myHealth.OnDeath -= OnTyrantDeath;
        }
    }

    private void Update()
    {
        // 读档自查（放 Update 首帧：等 HealthSystem.Start 把血量重置完再覆盖，避免被冲掉）
        if (!deathRestoreChecked)
        {
            deathRestoreChecked = true;
            int v = WorldState.Get(deathKey, 0);
            if (v >= 1) ApplyRestoredDeath(v);
        }

        if (dead) return;

        // ---- 死亡四相分支：假死/扑击阶段普通 AI 全停（扑击由协程驱动） ----
        if (deathPhase == DeathPhase.FakeDeath)
        {
            // 假死：等玩家靠近 → 暴起
            if (PlayerDist() <= fakeDeathTriggerDistance) StartLunge();
            return;
        }
        if (deathPhase == DeathPhase.Lunging) return;

        // ---- 残血狂化检查（一次性切换二阶段） ----
        CheckRage();

        // 声音随距离淡出（僵直中也照常，暴君趴着也有呼吸声）
        UpdateSoundScale();

        // 脚步震屏（移动时按步频，强度随距离衰减）
        UpdateFootsteps();

        // 僵直中：停止一切 AI 行为（不追不攻不冲刺），动画停在待机
        if (Time.time < stunUntil) return;

        if (isAttacking || isDashing) return;

        float dist = PlayerDist();

        // 进入攻击距离 → 攻击（受攻击间隔限制）
        if (dist <= attackRange && Time.time >= nextAttackTime)
        {
            attackRoutine = StartCoroutine(DoAttack());
            return;
        }
        if (isMoving) return;

        if (dist <= aggroRange)
        {
            if (!hasSeenPlayer)
            {
                hasSeenPlayer = true; // 首次发现：alert 一声，之后永久仇恨
                if (alertClip != null) audioSource.PlayOneShot(alertClip, soundScale);
                hasPatrolTarget = false;
            }

            // 冲刺概率检定（追击中）
            TryRollDash(dist);

            ChaseStep();
        }
        else
        {
            PatrolStep();
        }

        // ---- 卡住诊断（加分）：该动却连续没挪动 → 打一次警告，帮小泽一眼分辨是配置还是代码 ----
        CheckStuck();
    }

    /// <summary>
    /// 卡住诊断：活着（非僵直/攻击/冲刺/假死阶段）却连续 StuckWarnSeconds 秒没挪动 → 打一次警告。
    /// 提示小泽检查 obstacleLayer 是否把暴君自身/实验箱也算进去了（那样会"自己挡自己"永远站桩）。
    /// 打一次后不刷屏；动过之后自动复位，允许下次再警告。
    /// </summary>
    private void CheckStuck()
    {
        if (Vector2.Distance(rb.position, lastMoveCheckPos) > 0.01f)
        {
            lastMoveCheckPos = rb.position;
            stuckMoveTimer = 0f;
            stuckWarned = false; // 动过 → 复位
            return;
        }
        lastMoveCheckPos = rb.position;
        stuckMoveTimer += Time.deltaTime;
        if (stuckMoveTimer >= StuckWarnSeconds && !stuckWarned)
        {
            stuckWarned = true;
            Debug.LogWarning("[暴君] 已连续 " + StuckWarnSeconds + " 秒没挪动（位置 " + rb.position
                + "）。若它该走却没走：检查 Obstacle Layer（当前 mask=" + obstacleLayer.value
                + "）是否把暴君自己/实验箱的层也勾了进去——本脚本的移动检测已过滤自身碰撞体，"
                + "但实验箱等『别的物体』若也在该层，仍会被当成墙挡住。", gameObject);
        }
    }

    private void FixedUpdate()
    {
        if (dead || deathPhase != DeathPhase.Alive) return; // 假死/扑击/死亡 → 网格移动全停
        if (Time.time < stunUntil) return;
        if (isAttacking || isDashing) return;
        if (isMoving)
        {
            float speed = hasSeenPlayer ? chaseSpeed : patrolSpeed;
            Vector2 next = Vector2.MoveTowards(rb.position, targetGridPos, speed * Time.fixedDeltaTime);
            rb.position = next;
            if (Vector2.Distance(rb.position, targetGridPos) < 0.001f)
            {
                rb.position = targetGridPos;
                isMoving = false;
                SetIdleAnim(); // 走到格了 → 静止待机（按当前朝向）
            }
        }
    }

    // ======== 冲刺（招牌机制） ========

    /// <summary> 追击中每隔检定间隔掷一次概率：中了 → 发动冲刺 </summary>
    private void TryRollDash(float dist)
    {
        if (dashChance <= 0f) return;
        if (Time.time < nextDashTime) return;                    // 冷却中
        if (Time.time < noDashUntil) return;                     // 撞墙禁冲期（掐断"冲→撞→恢复→再冲"死循环）
        if (dist > dashMaxTriggerRange) return;                  // 太远不冲
        if (dist <= attackRange + 0.2f) return;                  // 贴脸不用冲
        if (Time.time < nextDashRoll) return;                    // 检定间隔没到

        nextDashRoll = Time.time + dashRollInterval;

        // 冲刺前预检：起点方向立即被墙挡 → 放弃本次冲刺（贴墙/隔墙时不再无脑冲）
        if (IsDashBlocked(DirectionToPlayer())) return;

        if (Random.value <= dashChance)
        {
            dashRoutine = StartCoroutine(DashRoutine());
        }
    }

    /// <summary>
    /// 冲刺前预检：沿 dir 方向探 dashPrecheckDistance 格，起点方向立即被墙挡 → 返回 true（本次别冲）。
    /// 用与撞墙截停同一套障碍盒检测（半格步进防漏检薄墙），不碰网格逻辑。
    /// </summary>
    private bool IsDashBlocked(Vector2 dir)
    {
        if (dir == Vector2.zero) return false;
        Vector2 d = dir.normalized;
        float step = Mathf.Max(0.25f, gridSize * 0.5f); // 半格步进
        for (float probe = step; probe <= dashPrecheckDistance + 0.0001f; probe += step)
        {
            Vector2 p = rb.position + d * probe;
            if (BlockedAt(p, gridSize * 0.8f)) return true; // 排除自身的障碍检测
        }
        return false;
    }

    /// <summary> 冲刺：前摇蓄力闪烁（原地不动）→ 直线高速冲向玩家（轻微追踪）→ 贴身立刻接攻击 </summary>
    private IEnumerator DashRoutine()
    {
        isDashing = true;
        isMoving = false;
        hasPatrolTarget = false;

        // ---- 前摇：蓄力闪烁警示（橙红 ↔ 白交替），原地不动 ----
        Vector2 dashDir = DirectionToPlayer();
        if (dashClip != null) audioSource.PlayOneShot(dashClip, soundScale);
        float telegraphT = 0f;
        float blinkTimer = 0f;
        bool blinkOn = false;
        while (telegraphT < dashTelegraph)
        {
            telegraphT += Time.deltaTime;
            blinkTimer += Time.deltaTime;
            if (blinkTimer >= 0.12f)
            {
                blinkTimer = 0f;
                blinkOn = !blinkOn;
                sr.color = blinkOn ? new Color(1f, 0.5f, 0.2f) : baseColor;
            }
            yield return null;
        }
        sr.color = baseColor;
        if (dead) { isDashing = false; yield break; }

        // ---- 冲刺：高速直线 + 轻微追踪（每帧往玩家方向偏转一点点） ----
        Vector2 dir = dashDir;
        string lastSuffix = DirSuffix(dir);
        PlayState(movePrefix + lastSuffix); // 冲刺用 move 动画
        float travelled = 0f;
        bool hitWall = false; // 撞墙截停标记（区分三种停止原因）
        while (travelled < dashMaxDistance)
        {
            // 轻微追踪：当前冲向每帧向"玩家实际方向"靠拢一点
            Vector2 desired = DirectionToPlayer();
            dir = Vector2.Lerp(dir, desired, dashTurnFactor).normalized;
            Vector2 facing = SnapDir(dir);
            string suffix = DirSuffix(facing);
            if (suffix != lastSuffix)
            {
                lastSuffix = suffix;
                currentFacing = facing;
                PlayState(movePrefix + suffix); // 冲刺中转向了 → 换方向 move 动画
            }

            float step = chaseSpeed * dashSpeedMultiplier * Time.deltaTime;
            Vector2 next = rb.position + dir * step;

            // 撞墙截停（冲刺是自由移动，用简单障碍盒查，不走网格射线；排除自身防误判）
            if (BlockedAt(next, gridSize * 0.8f))
            {
                hitWall = true;
                break;
            }

            travelled += step;
            rb.position = next;

            // 冲到玩家身前 → 停
            if (PlayerDist() <= attackRange) break;
            yield return null;
        }

        isDashing = false;
        dashRoutine = null;
        nextDashTime = Time.time + dashCooldown; // 进入冷却

        // 顺畅完一次冲刺（没撞墙）→ 撞墙计数清零（开阔地一次不撞就不受影响）
        if (!hitWall) consecutiveWallHits = 0;

        // ---- 撞墙硬直：暴君把自己撞懵了，硬直期玩家可安全输出（复用僵直系统） ----
        if (hitWall && !dead && deathPhase == DeathPhase.Alive)
        {
            if (wallStunClip != null) audioSource.PlayOneShot(wallStunClip, soundScale);

            // 连续撞墙递增禁冲期（wallHitDashBan / +step / 封顶 max）：硬直结束之后再禁冲 ban 秒，
            // 叠加在冲刺冷却之上 → 彻底掐断"冲→撞→硬直→恢复→再冲→再撞"的站桩死循环
            consecutiveWallHits++;
            float ban = Mathf.Min(wallHitDashBan + (consecutiveWallHits - 1) * wallHitDashBanStep, wallHitDashBanMax);
            noDashUntil = Time.time + wallStunDuration + ban;

            Debug.Log("[暴君] 冲刺撞墙，硬直 " + wallStunDuration + " 秒 + 禁冲 " + ban
                + " 秒（连续撞墙第 " + consecutiveWallHits + " 次，反制窗口！）", gameObject);
            SyncGridTarget(); // 撞墙也要同步网格目标，防硬直恢复后"走回"旧格
            Stun(wallStunDuration);
            yield break; // Stun 接管后续恢复
        }

        // ---- 冲刺收尾：先用【吸附前的真实距离】决定怎么收尾，之后才做网格吸附 ----
        //      （网格吸附最多把暴君推远半格；若先吸附再量距离，判定会失败 → 站面前发呆不打）
        float endDist = PlayerDist();
        bool handled = false;

        // ① 够近 → dashFollowUpDelay 秒内【必出手】：难躲的扑击刀（DoAttack(true) = 短前摇 + 大命中范围）
        if (!dead && deathPhase == DeathPhase.Alive && endDist <= attackRange + dashFollowUpExtraRange)
        {
            yield return new WaitForSeconds(dashFollowUpDelay);
            if (!dead && deathPhase == DeathPhase.Alive && Time.time >= stunUntil)
                attackRoutine = StartCoroutine(DoAttack(true)); // 忽略攻击冷却强制出手；内部照常重置 nextAttackTime
            else if (!dead) SetIdleAnim();
            handled = true;
        }
        // ② 距离只差一点点（玩家挪了半步）→ 先补走一格贴近再出手，不站面前发呆
        else if (!dead && deathPhase == DeathPhase.Alive && player != null
                 && endDist <= attackRange + dashFollowUpExtraRange + 1.5f)
        {
            SyncGridTarget(); // 先归位网格，StepToward 才有正确基准格
            if (StepToward((Vector2)player.position))
            {
                float t = 0f;
                while (isMoving && t < 1.5f) { t += Time.deltaTime; yield return null; } // 等这一格走完
                if (!dead && deathPhase == DeathPhase.Alive && Time.time >= stunUntil
                    && PlayerDist() <= attackRange + dashFollowUpHitRange)
                    attackRoutine = StartCoroutine(DoAttack(true));
                else if (!dead) SetIdleAnim();
            }
            else
            {
                SetIdleAnim();
            }
            handled = true;
        }

        // ③ 确实被甩开了 → 回 idle（继续网格追击）
        if (!handled && !dead && deathPhase == DeathPhase.Alive) SetIdleAnim();
        if (!dead) SyncGridTarget(); // 最后统一吸附回网格（幂等；防下次追击"走回"旧格）
    }

    /// <summary>
    /// 把暴君位置吸附回最近整数格并同步网格目标（自由移动/冲刺结束用）。
    /// 否则结束回网格追击会先"走回"旧目标格，视觉上冲过去又退回来。
    /// </summary>
    private void SyncGridTarget()
    {
        Vector2 snap = rb.position;
        snap.x = Mathf.Round(snap.x / gridSize) * gridSize;
        snap.y = Mathf.Round(snap.y / gridSize) * gridSize;
        if (!BlockedAt(snap, gridSize * 0.9f)) rb.position = snap; // 吸附最近格；被堵就保持原位不硬塞
        targetGridPos = rb.position;
        isMoving = false;
    }

    // ======== 巡逻 / 追击（网格步进，照搬电锯哥套路） ========

    private void PatrolStep()
    {
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
        for (int i = 0; i < 10; i++)
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
            PlayState(movePrefix + DirSuffix(bestDir));
        }
        else
        {
            SetIdleAnim(); // 四面被堵：站住瞪着玩家
        }
    }

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
                PlayState(movePrefix + DirSuffix(d));
                return true;
            }
        }
        return false;
    }

    private bool IsWalkable(Vector2 pos)
    {
        // 目标格有没有墙/障碍（关键：排除暴君自己的碰撞体——否则大碰撞体会把自己判成"四面被堵"而站桩）
        if (BlockedAt(pos, gridSize * 0.9f)) return false;

        Vector2 from = targetGridPos;
        Vector2 toDir = (pos - from).normalized;
        float toDist = Vector2.Distance(from, pos);
        if (RaycastBlocked(from, toDir, toDist)) return false;
        return true;
    }

    // ======== 物理查询（统一排除自身，防"自己挡自己"导致整天站桩） ========

    /// <summary> 这个碰撞体是不是暴君自己（本体或子物体）——物理查询要把它过滤掉 </summary>
    private bool IsSelfCollider(Collider2D c)
    {
        if (c == null) return true;
        return c.transform == transform || c.transform.IsChildOf(transform);
    }

    /// <summary>
    /// pos 处 size 大小的盒子里有没有"非自身"的墙/障碍（obstacleLayer）。
    /// 用 OverlapBoxAll + 过滤自身：暴君碰撞体比一格大 / 本身在 obstacleLayer 里也不会误判断"被堵"。
    /// </summary>
    private bool BlockedAt(Vector2 pos, float size)
    {
        Collider2D[] hits = Physics2D.OverlapBoxAll(pos, Vector2.one * size, 0f, obstacleLayer);
        foreach (Collider2D c in hits)
        {
            if (!IsSelfCollider(c)) return true; // 有真障碍（墙/别的物体）
        }
        return false;
    }

    /// <summary> 从 from 沿 dir 走 dist，路上有没有"非自身"的障碍（同上过滤自身） </summary>
    private bool RaycastBlocked(Vector2 from, Vector2 dir, float dist)
    {
        if (dir == Vector2.zero || dist <= 0f) return false;
        RaycastHit2D[] hits = Physics2D.RaycastAll(from, dir.normalized, dist, obstacleLayer);
        foreach (RaycastHit2D h in hits)
        {
            if (h.collider == null) continue;
            if (!IsSelfCollider(h.collider)) return true;
        }
        return false;
    }

    // ======== 攻击 ========

    /// <summary>
    /// 攻击。dashLunge = false：普通攻击（长前摇、判定晚、可躲）；
    /// dashLunge = true：冲刺收尾的"扑击刀"——短前摇（dashFollowUpWindup）+ 大命中余量（dashFollowUpHitRange）
    /// + 动画刚播即判定，几乎躲不掉。
    /// </summary>
    private IEnumerator DoAttack(bool dashLunge = false)
    {
        isAttacking = true;
        nextAttackTime = Time.time + attackCooldown;

        // 前摇预警：面向玩家 + 红 tint + 攻击音
        Vector2 dir = DirectionToPlayer();
        currentFacing = dir;
        baseColor = new Color(1f, 0.35f, 0.3f); // 预警红
        ApplyTint();
        if (attackClip != null) audioSource.PlayOneShot(attackClip, soundScale);

        yield return new WaitForSeconds(dashLunge ? dashFollowUpWindup : windupTime);

        // 播 attack 状态（完整状态名带空格：如 "attack move down"）
        PlayState(attackPrefix + DirSuffix(dir));

        if (dashLunge)
        {
            // 扑击刀：动画刚播即判定（不再等 0.2 秒）+ 大命中余量 → 玩家走一步也躲不掉
            if (!dead && PlayerDist() <= attackRange + dashFollowUpHitRange && playerHealth != null && !playerHealth.isDead)
                playerHealth.TakeDamage(attackDamage);
        }
        else
        {
            // 普通攻击：动画中段判定一次伤害（照旧，可躲）
            yield return new WaitForSeconds(0.2f);
            if (!dead && PlayerDist() <= attackRange + 0.4f && playerHealth != null && !playerHealth.isDead)
                playerHealth.TakeDamage(attackDamage);
        }

        // 攻击后摇
        yield return new WaitForSeconds(0.25f);

        if (!dead)
        {
            baseColor = normalTint; // 恢复正常体色（狂化后是暗红）
            ApplyTint();
            SetIdleAnim();
        }
        isAttacking = false;
    }

    // ======== 罐子爆破联动接口（ExplosiveBarrel 调用） ========

    /// <summary>
    /// 受击接口：伤害走 HealthSystem（闪白/死亡序列统一）。
    /// 罐子爆破、脚本外任何系统都调这个。
    /// </summary>
    public void TakeDamage(int dmg)
    {
        if (dead || dmg <= 0) return;
        if (myHealth != null)
        {
            myHealth.TakeDamage(dmg);
        }
        else
        {
            Debug.LogWarning("[暴君] 收到伤害 " + dmg + " 但身上没有 HealthSystem，伤害被忽略", gameObject);
        }
    }

    /// <summary>
    /// 僵直：停止一切 AI 行为（不追不攻不冲刺）持续 duration 秒 + 播待机动画，时间到自动恢复。
    /// 罐子爆破炸到暴君 / 冲刺撞墙硬直 都走这里。
    /// </summary>
    public void Stun(float duration)
    {
        if (dead || duration <= 0f || deathPhase != DeathPhase.Alive) return;
        stunUntil = Time.time + duration;

        // 掐断进行中的攻击/冲刺/移动（注意：撞墙硬直调进来时 dashRoutine 已被清引用，不会停到自己）
        if (attackRoutine != null) { StopCoroutine(attackRoutine); attackRoutine = null; }
        if (dashRoutine != null) { StopCoroutine(dashRoutine); dashRoutine = null; }
        isAttacking = false;
        isDashing = false;
        isMoving = false;
        hasPatrolTarget = false;
        sr.color = baseColor;

        SetIdleAnim(); // 僵直 = 原地待机
        Debug.Log("[暴君] 僵直 " + duration + " 秒", gameObject);
    }

    // ======== 受击 / 死亡 ========

    private void OnHurt()
    {
        if (dead) return;

        // 假死中被补刀 → 跳过暴起扑击，直接真死（防拖时间）
        if (deathPhase == DeathPhase.FakeDeath)
        {
            Debug.Log("[暴君] 假死中被补了一刀——装不下去了，直接死亡");
            FinalizeDeath();
            return;
        }

        if (flashRoutine != null) StopCoroutine(flashRoutine);
        flashRoutine = StartCoroutine(WhiteFlashRoutine());
    }

    private IEnumerator WhiteFlashRoutine()
    {
        sr.color = Color.white;
        yield return new WaitForSeconds(0.1f);
        sr.color = baseColor;
        flashRoutine = null;
    }

    /// <summary>
    /// HealthSystem OnDeath 回调（血量归零时触发）：
    /// 第一次归零（且开了假死）→ 进入假死等玩家靠近；假死期被补刀再次归零 → 直接真死。
    /// 没开假死 → 直接真死。
    /// </summary>
    private void OnTyrantDeath()
    {
        if (deathPhase == DeathPhase.Dead) return;

        if (fakeDeathEnabled && deathPhase == DeathPhase.Alive)
        {
            EnterFakeDeath();
        }
        else
        {
            FinalizeDeath();
        }
    }

    /// <summary> 残血狂化检查（Update 每帧调；只切一次） </summary>
    private void CheckRage()
    {
        if (raged || !enableRage) return;
        if (myHealth == null || myHealth.maxHealth <= 0) return;

        float ratio = (float)myHealth.currentHealth / myHealth.maxHealth;
        if (ratio <= rageThreshold) EnterRage();
    }

    /// <summary> 进入狂化二阶段：提速 + 冲刺翻倍 + 音调上调 + 体色暗红 + 场景灯光压暗 </summary>
    private void EnterRage()
    {
        raged = true;

        chaseSpeed *= rageSpeedMultiplier;
        dashChance = Mathf.Min(1f, dashChance * rageDashMultiplier); // 封顶必冲
        if (audioSource != null) audioSource.pitch = ragePitchBoost; // 音效整体上调一档

        normalTint = rageTint;   // 低血视觉异变：体色变暗红（攻击前摇/闪白都恢复到它）
        baseColor = rageTint;    // ApplyTint 读 baseColor，两处同步
        ApplyTint();

        // 场景灯光压暗一档（经 DarknessController 全局系数，不会被它的每帧 Lerp 拉回）
        DarknessController.globalDimFactor = rageDimFactor;

        Debug.Log("[暴君] 进入狂化二阶段！速度x" + rageSpeedMultiplier + " 冲刺概率x" + rageDashMultiplier + " 灯光压暗", gameObject);
    }

    /// <summary> 掐断一切进行中的行为（假死/真死共用收尾第一步） </summary>
    private void KillAllActions()
    {
        if (attackRoutine != null) { StopCoroutine(attackRoutine); attackRoutine = null; }
        if (dashRoutine != null) { StopCoroutine(dashRoutine); dashRoutine = null; }
        isAttacking = false;
        isDashing = false;
        isMoving = false;
        hasPatrolTarget = false;
        baseColor = normalTint; // 清掉攻击前摇可能残留的预警红
        if (sr != null) sr.color = normalTint;
    }

    /// <summary> 播死亡姿态：有 "death" 状态播真动画定格；没有就停在当前帧（不崩） </summary>
    private void PlayDeathPose()
    {
        if (animator == null) return;
        int deathHash = Animator.StringToHash("death");
        if (animator.HasState(0, deathHash))
            animator.CrossFade(deathHash, 0.05f, 0, 0f);
        else
            Debug.LogWarning("[暴君] Animator 里没有 \"death\" 状态，死亡定格当前帧", gameObject);
    }

    /// <summary> 恢复死亡阶段（读档用）：v>=2 真死 / v==1 假死；不重播事件（避免重复触发通关演出） </summary>
    private void ApplyRestoredDeath(int v)
    {
        if (v >= 2)
        {
            deathPhase = DeathPhase.Dead;
            dead = true;
            KillAllActions();
            if (audioSource != null) audioSource.Stop();
            PlayDeathPose();
            Collider2D col = GetComponent<Collider2D>();
            if (col != null) col.enabled = false; // 尸体不挡路
            Debug.Log("[暴君] 读档还原：已真死", gameObject);
        }
        else if (v == 1)
        {
            // 假死还原：倒着不动，血量重置为 1 让玩家还能补刀
            deathPhase = DeathPhase.FakeDeath;
            KillAllActions();
            if (audioSource != null) audioSource.Stop();
            PlayDeathPose();
            if (myHealth != null)
            {
                myHealth.isDead = false;
                myHealth.currentHealth = 1;
            }
            Debug.Log("[暴君] 读档还原：处于假死状态", gameObject);
        }
    }

    /// <summary> 进入假死：倒下定格 + 音源熄火，等玩家靠近再暴起（血量重置让玩家还能补刀） </summary>
    private void EnterFakeDeath()
    {
        deathPhase = DeathPhase.FakeDeath;
        WorldState.Set(deathKey, 1); // 世界进度表登记"假死"
        KillAllActions();
        if (audioSource != null) audioSource.Stop();

        onFakeDeath?.Invoke(); // 第一次被打空血倒下 → 广播（TyrantIntro 订阅它做 Boss 音乐渐停）

        PlayDeathPose();

        // 重置血量可被打：假死中再被打（OnDamaged）→ OnHurt 里检测 → 跳过扑击直接真死
        if (myHealth != null)
        {
            myHealth.isDead = false;
            myHealth.currentHealth = 1;
        }

        Debug.Log("[暴君] 倒下了……一动不动（假死，靠近 " + fakeDeathTriggerDistance + " 格内会暴起）", gameObject);
    }

    /// <summary> 暴起！玩家靠太近 → 立即扑击（复用冲刺移动，二阶段倍率） </summary>
    private void StartLunge()
    {
        deathPhase = DeathPhase.Lunging;
        Debug.Log("[暴君] 暴起！！扑击！", gameObject);
        onLungeStart?.Invoke(); // 暴起广播（TyrantIntro 订阅它重启 Boss 音乐）
        dashRoutine = StartCoroutine(LungeRoutine());
    }

    /// <summary> 扑击：无前摇直接高速冲向玩家 → 接一次攻击 → 真死 </summary>
    private IEnumerator LungeRoutine()
    {
        // 暴起惊吓音：独立空位恒定音量（Dash Clip 只用于正常冲刺，不再兼任暴起音）
        PlaySting(lungeStingClip, lungeStingVolume);
        CameraShaker.Shake(footstepShakeStrength * 2f, 0.25f); // 暴起瞬间狠狠震一下屏

        // 高速扑向玩家（轻微追踪，同冲刺移动逻辑）
        Vector2 dir = DirectionToPlayer();
        string lastSuffix = DirSuffix(dir);
        PlayState(movePrefix + lastSuffix);
        float travelled = 0f;
        while (travelled < dashMaxDistance)
        {
            Vector2 desired = DirectionToPlayer();
            dir = Vector2.Lerp(dir, desired, dashTurnFactor).normalized;
            Vector2 facing = SnapDir(dir);
            string suffix = DirSuffix(facing);
            if (suffix != lastSuffix)
            {
                lastSuffix = suffix;
                currentFacing = facing;
                PlayState(movePrefix + suffix);
            }

            float step = chaseSpeed * dashSpeedMultiplier * Time.deltaTime;
            Vector2 next = rb.position + dir * step;
            if (BlockedAt(next, gridSize * 0.8f))
                break; // 撞墙截停（扑空也照常接攻击；排除自身防误判）

            travelled += step;
            rb.position = next;
            if (PlayerDist() <= attackRange) break;
            yield return null;
        }

        // 扑击落地接一次攻击（扑空也挥一下，演出完整）
        currentFacing = DirectionToPlayer();
        yield return StartCoroutine(DoAttack());

        // 攻击完 → 真死（先清掉自己这条协程引用，防 FinalizeDeath 的 KillAllActions 停掉自己、收尾中断）
        dashRoutine = null;
        FinalizeDeath();
    }

    /// <summary> 真死：事件广播（接通关演出）+ 循环声熄火 + 死亡姿态 + 尸体收尾 </summary>
    private void FinalizeDeath()
    {
        if (deathPhase == DeathPhase.Dead) return;
        deathPhase = DeathPhase.Dead;
        dead = true;
        WorldState.Set(deathKey, 2); // 世界进度表登记"真死"

        KillAllActions();

        // 事件立刻广播（接通关演出/开门不用等收尾）
        onTyrantDeath?.Invoke();

        // 狂化的场景压暗恢复
        DarknessController.globalDimFactor = 1f;

        if (audioSource != null) audioSource.Stop();
        PlayDeathPose();

        if (corpseFadeDuration > 0f) StartCoroutine(CorpseFadeRoutine());
    }

    private IEnumerator CorpseFadeRoutine()
    {
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

    // ======== 动画（按状态名播放，缺失只警告不崩） ========

    private void SetIdleAnim()
    {
        PlayState(idlePrefix + DirSuffix(currentFacing));
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
                Debug.LogWarning("[暴君] Animator 里没有状态 \"" + stateName + "\"（检查 Inspector 里三个状态名前缀），跳过播放（不崩）", gameObject);
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

    /// <summary>
    /// 脚步震屏（Update 每帧调）：移动中按步频触发——脚步声（距离听声）+ 相机微震，
    /// 震动强度随与玩家距离衰减（贴脸震最狠，超过最大距离不震）。
    /// </summary>
    private void UpdateFootsteps()
    {
        bool moving = isMoving || isDashing;
        if (!moving)
        {
            footstepTimer = 0f; // 停下重置步频（起步第一步立刻响）
            return;
        }

        footstepTimer -= Time.deltaTime;
        if (footstepTimer > 0f) return;
        footstepTimer = footstepInterval;

        float dist = PlayerDist();
        if (dist >= footstepShakeMaxDist) return; // 太远：听不见也震不到

        // 强度随距离线性衰减：贴脸 = 满强度，最大距离 = 0
        float strength = footstepShakeStrength * (1f - dist / footstepShakeMaxDist);

        if (footstepClip != null) AudibleAudio.PlayAt(footstepClip, transform.position);
        CameraShaker.Shake(strength, 0.15f);
    }

    private Vector2 DirectionToPlayer()
    {
        if (player == null) return currentFacing;
        return SnapDir((Vector2)player.position - rb.position);
    }

    /// <summary> 任意方向吸附到四方向（返回的是原始方向的符号，不取玩家相对方向） </summary>
    private Vector2 SnapDir(Vector2 v)
    {
        if (Mathf.Abs(v.x) >= Mathf.Abs(v.y))
            return new Vector2(Mathf.Sign(v.x), 0);
        return new Vector2(0, Mathf.Sign(v.y));
    }

    /// <summary> 声音随距离淡出/淡入（跟视野联动，照搬电锯哥逻辑） </summary>
    private void UpdateSoundScale()
    {
        float target;
        if (!soundDistanceEnabled)
        {
            target = 1f;
        }
        else
        {
            float dist = PlayerDist();
            float silentRange = aggroRange * silentMultiplier;
            if (dist <= aggroRange) target = 1f;
            else if (dist >= silentRange) target = 0f;
            else target = (silentRange - dist) / (silentRange - aggroRange);
        }

        soundScale = Mathf.Lerp(soundScale, target, soundFadeSpeed * Time.deltaTime);
        audioSource.volume = loopVolume * soundScale;

        // 省资源：系数到 0 就 Stop；重新进入范围就从头 Play
        if (soundScale <= 0.01f)
        {
            if (audioSource.isPlaying) audioSource.Stop();
        }
        else if (loopClip != null && !audioSource.isPlaying)
        {
            audioSource.Play();
        }
    }

    private void ApplyTint()
    {
        if (sr != null) sr.color = baseColor;
    }

    /// <summary>
    /// 播惊吓音：独立 2D 音源（spatialBlend=0）恒定音量播放——不走距离衰减，
    /// 也不受本尊循环声 UpdateSoundScale 每帧改 audioSource.volume 的影响（惊吓演出要的就是"响"）。
    /// </summary>
    private void PlaySting(AudioClip clip, float volume)
    {
        if (clip == null) return;
        if (stingSource == null)
        {
            stingSource = gameObject.AddComponent<AudioSource>();
            stingSource.playOnAwake = false;
            stingSource.loop = false;
            stingSource.spatialBlend = 0f; // 纯 2D：恒定音量
        }
        stingSource.PlayOneShot(clip, volume);
    }

    private void SnapToGrid()
    {
        Vector2 pos = rb.position;
        pos.x = Mathf.Round(pos.x / gridSize) * gridSize;
        pos.y = Mathf.Round(pos.y / gridSize) * gridSize;
        rb.position = pos;
    }
}
