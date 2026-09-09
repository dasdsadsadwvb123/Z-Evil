using UnityEngine;
using System.Collections;
using System.Collections.Generic;

/// <summary>
/// 舔食者 Licker AI（game2 守要道型敌人，基于 BossSawAI 框架）：
/// 失明 → 听声辨位（玩家【移动中】才算发出声音，站定不动就"隐身"）
/// → 发现 → 尖叫 → 地面追击（带记忆时间：发现后追 N 秒，期间玩家站定也追）→ 舌头长距突刺攻击。
/// 追出守卫半径 / 记忆时间到 → 放弃 → 走回出生点继续地面巡逻。
/// 死亡：普通敌人待遇——HealthSystem 勾 destroyOnDeath 自动销毁（本脚本不做死亡演出）。
/// 挂到舔食者物体上（需要：SpriteRenderer + BoxCollider2D(Trigger) + Rigidbody2D(Kinematic)
/// + HealthSystem(maxHealth=8, destroyOnDeath 勾上) + 小泽的 Animator + 本脚本）。
/// 动画：电锯哥同款 12 个小写状态名（idle/walk/attack × 上下左右），缺失只警告不崩。
/// 玩家移动检测用"逐帧位移"判断，不需要改小泽的 PixelGridMovement（isMoving 是私有的）。
/// </summary>
public class LickerAI : MonoBehaviour
{
    // ======== 动画状态名常量（电锯哥同款：全小写，缺状态只警告） ========
    private const string IdleDown = "idledown", IdleUp = "idleup", IdleRight = "idleright", IdleLeft = "idleleft";
    private const string WalkDown = "walkdown", WalkUp = "walkup", WalkRight = "walkright", WalkLeft = "walkleft";
    private const string AttackDown = "attackdown", AttackUp = "attackup", AttackRight = "attackright", AttackLeft = "attackleft";

    /// <summary> 舔食者的三种状态：地面巡逻 → 地面追击 → 回位 </summary>
    private enum State { Patrol, Chasing, ReturningHome }

    [Header("移动（网格步进，和丧尸/电锯哥同套路）")]
    [Tooltip("一格多大（和场景格子一致，一般 1）")]
    public float gridSize = 1f;
    [Tooltip("巡逻速度（地面爬行）")]
    public float patrolSpeed = 1.2f;
    [Tooltip("追击速度（地面冲刺）")]
    public float chaseSpeed = 4f;
    [Tooltip("听声范围：玩家在这个距离内【移动】才会被发现（失明，看不见站着不动的玩家）")]
    public float aggroRange = 6f;
    [Tooltip("巡逻范围：出生点周围多大的圈里爬行巡逻")]
    public float patrolRadius = 4f;
    [Tooltip("墙/障碍所在 Layer（IsWalkable 检测用，Inspector 里选墙的 Layer）")]
    public LayerMask obstacleLayer;

    [Header("听声辨位（招牌机制）")]
    [Tooltip("追击记忆时间（秒）：发现后追这么久，期间玩家站定不动也追；超时还够不着才放弃")]
    public float chaseMemoryTime = 3f;

    [Header("守要道")]
    [Tooltip("守卫半径：追击时跑不出出生点周围这个圈，出圈/玩家出圈就放弃回位（不跨场景追）")]
    public float guardRadius = 5f;

    [Header("舌头突刺攻击")]
    [Tooltip("进入这个距离开始舌头突刺（比电锯哥 0.9 远）")]
    public float attackRange = 1.6f;
    [Tooltip("舌头伤害")]
    public int attackDamage = 2;
    [Tooltip("攻击前摇（秒）：闪红 + 音调拉高预警，给玩家反应窗口")]
    public float windupTime = 0.4f;
    [Tooltip("突刺距离（格）：舌头弹出去多远")]
    public float lungeDistance = 1.2f;
    [Tooltip("突刺速度（格/秒）：舌头弹出去多快")]
    public float lungeSpeed = 8f;
    [Tooltip("攻击间隔（秒）：从起手到下次能再起手")]
    public float attackCooldown = 1.5f;

    [Header("音效（素材小泽自己拖，不拖 = 静音不报错）")]
    [Tooltip("环境循环声：涎液滴答/低吼，挂着就一直响")]
    public AudioClip lickerLoopClip;
    [Tooltip("发现玩家的尖叫（发现那一刻播）")]
    public AudioClip alertClip;
    [Tooltip("舌头抽击音效（突刺弹出去那一刻播）")]
    public AudioClip attackSoundClip;
    [Tooltip("追击时音调（1 = 原速，1.3 = 提速压迫感）")]
    public float chasePitch = 1.3f;
    [Tooltip("循环环境声音量")]
    [Range(0f, 1f)]
    public float loopVolume = 0.6f;

    // ---- 运行时状态 ----
    private Rigidbody2D rb;
    private SpriteRenderer sr;
    private Animator animator;
    private HealthSystem myHealth;
    private Transform player;
    private HealthSystem playerHealth;
    private AudioSource audioSource;

    private State state = State.Patrol;
    private bool isAttacking = false;
    private bool isMoving = false;
    private Vector2 homePos;              // 出生点：巡逻中心 + 守卫半径圆心
    private Vector2 groundPos;            // 逻辑位置（地面坐标，和渲染位置一致）
    private Vector2 patrolTarget;         // 当前巡逻目标格
    private bool hasPatrolTarget = false;
    private Vector2 targetGridPos;        // 正在走过去的格（地面坐标）
    private Vector2 currentFacing = Vector2.down;
    private float nextAttackTime = 0f;
    private float chaseMemoryTimer = 0f;  // 追击记忆：>0 期间死追，减到 0 放弃
    private Vector2 lastPlayerPos;        // 上一帧玩家位置（算位移 = 听脚步）
    private bool hasSeenPlayerLastFrame = false; // 上一帧是否"听得见"玩家（边界触发尖叫用）
    private Color baseColor = Color.white;
    private Coroutine flashRoutine;
    private readonly HashSet<string> warnedStates = new HashSet<string>();

    private void Start()
    {
        rb = GetComponent<Rigidbody2D>();
        if (rb == null)
        {
            rb = gameObject.AddComponent<Rigidbody2D>();
        }
        rb.bodyType = RigidbodyType2D.Kinematic; // 网格步进，不吃物理推挤
        rb.freezeRotation = true;                // 网格步进不吃物理旋转

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
            lastPlayerPos = player.position;
        }

        myHealth = GetComponent<HealthSystem>();
        if (myHealth != null)
        {
            myHealth.deathTriggerName = "";  // 普通敌人没有死亡动画状态，死了直接靠 destroyOnDeath 销毁
            myHealth.destroyOnDeath = true;  // 血量归零 → HealthSystem 自己 Destroy（可挂掉落用 OnDeath 事件）
            myHealth.OnDamaged += OnHurt;    // 受击闪白
        }
        else
        {
            Debug.LogWarning("[舔食者] 身上没有 HealthSystem，打不死也死不了！", gameObject);
        }

        // 环境循环声：登场就响（涎液/低吼，电锯哥同款自动补音源）
        audioSource = GetComponent<AudioSource>();
        if (audioSource == null) audioSource = gameObject.AddComponent<AudioSource>();
        audioSource.playOnAwake = false;
        audioSource.loop = true;
        audioSource.clip = lickerLoopClip;
        audioSource.volume = loopVolume;
        audioSource.pitch = 1f;
        if (lickerLoopClip != null) audioSource.Play();

        homePos = rb.position;
        groundPos = rb.position;
        targetGridPos = rb.position;
        SnapToGrid();

        SetIdleAnim();
    }

    private void OnDestroy()
    {
        if (myHealth != null)
        {
            myHealth.OnDamaged -= OnHurt;
        }
    }

    private void Update()
    {
        if (myHealth != null && myHealth.isDead) return;

        // 环境声音调：巡逻 = 1，追击 = 1.3（平滑过渡，"追起来了"的压迫感）
        float targetPitch = state == State.Chasing ? chasePitch : 1f;
        audioSource.pitch = Mathf.Lerp(audioSource.pitch, targetPitch, 3f * Time.deltaTime);

        if (isAttacking) return;

        // ===== 听声辨位（每帧判断玩家有没有在动） =====
        UpdateHearing();

        switch (state)
        {
            case State.Patrol:
                if (isMoving) break; // 还在走向当前格
                PatrolStep();
                break;

            case State.Chasing:
                // 记忆时间倒数：到 0 = 玩家站太久了/听不见了 → 放弃回位
                chaseMemoryTimer -= Time.deltaTime;
                if (chaseMemoryTimer <= 0f || player == null)
                {
                    GiveUpChase();
                    break;
                }
                // 进攻击距离 → 舌头突刺（受攻击间隔限制）
                if (PlayerDist() <= attackRange && Time.time >= nextAttackTime)
                {
                    StartCoroutine(DoTongueAttack());
                    break;
                }
                if (isMoving) break;
                ChaseStep();
                break;

            case State.ReturningHome:
                if (isMoving) break;
                // 走回家了 → 恢复地面巡逻
                if (Vector2.Distance(groundPos, homePos) < 0.1f)
                {
                    BackToPatrol();
                }
                else
                {
                    StepToward(homePos, patrolSpeed);
                }
                break;
        }
    }

    private void FixedUpdate()
    {
        if (myHealth != null && myHealth.isDead) return;
        if (isAttacking) return;
        if (!isMoving) return;

        // 网格步进（地面爬行，和丧尸/电锯哥同款）：groundPos 就是渲染位置
        float speed = (state == State.Chasing) ? chaseSpeed : patrolSpeed;
        groundPos = Vector2.MoveTowards(groundPos, targetGridPos, speed * Time.fixedDeltaTime);
        rb.position = groundPos;

        if (Vector2.Distance(groundPos, targetGridPos) < 0.001f)
        {
            groundPos = targetGridPos;
            rb.position = groundPos;
            isMoving = false;
            SetIdleAnim(); // 走到格了 → 静止 idle（按当前朝向）
        }
    }

    // ======== 听声辨位 ========

    /// <summary>
    /// 每帧检查玩家是否在移动（逐帧位移判断，不动小泽的 PixelGridMovement）：
    /// 玩家移动中 + 在听声范围内 + 在守卫半径内 → 发现！
    /// 射击/挥刀锁定期间玩家 frozen 不动 → 天然听不见，站定隐身成立。
    /// </summary>
    private void UpdateHearing()
    {
        if (player == null) return;

        bool canHear = IsPlayerMoving()
            && PlayerDist() <= aggroRange
            && Vector2.Distance((Vector2)player.position, homePos) <= guardRadius; // 玩家走出守卫圈就不归我管（守要道）

        if (canHear && !hasSeenPlayerLastFrame)
        {
            Alert(); // 边界触发：从"听不见"变"听得见"的那一帧尖叫 + 直接追
        }
        hasSeenPlayerLastFrame = canHear;

        // 已经在追：只要玩家还在动且听得见，就刷新记忆时间（一直动就一直追）
        if (canHear && state == State.Chasing)
        {
            chaseMemoryTimer = chaseMemoryTime;
        }
    }

    /// <summary> 玩家这帧有没有挪动（含传送过滤：瞬移不算脚步声） </summary>
    private bool IsPlayerMoving()
    {
        Vector2 delta = (Vector2)player.position - lastPlayerPos;
        lastPlayerPos = player.position;
        if (delta.sqrMagnitude > 16f) return false; // 一帧挪超过 4 格 = 传送门瞬移，不算走路
        return delta.sqrMagnitude > 0.0001f;
    }

    /// <summary> 发现玩家：尖叫一声 → 直接进入追击（地面爬行，无需过渡） </summary>
    private void Alert()
    {
        if (state == State.Chasing) return; // 已经在追就不重复尖叫
        if (alertClip != null) audioSource.PlayOneShot(alertClip);
        Debug.Log("[舔食者] 听到动静了！");
        StartChase();
    }

    private void StartChase()
    {
        state = State.Chasing;
        chaseMemoryTimer = chaseMemoryTime; // 发现后至少追满记忆时间
        hasPatrolTarget = false;
    }

    /// <summary> 放弃追击：走回出生点（路上不再听声，到家恢复巡逻） </summary>
    private void GiveUpChase()
    {
        state = State.ReturningHome;
        hasPatrolTarget = false;
        baseColor = Color.white;
        ApplyTint();
        Debug.Log("[舔食者] 没声音了…… 回去巡逻。");
    }

    /// <summary> 到家了：恢复地面巡逻 </summary>
    private void BackToPatrol()
    {
        state = State.Patrol;
        SetIdleAnim();
    }

    // ======== 巡逻 / 追击（网格步进，参照 BossSawAI 套路） ========

    private void PatrolStep()
    {
        // 没目标或走到了 → 在出生点圈内随机挑一个可走的格
        if (!hasPatrolTarget || Vector2.Distance(groundPos, patrolTarget) < 0.1f)
        {
            hasPatrolTarget = PickPatrolTarget();
            if (!hasPatrolTarget) return;
        }
        if (!StepToward(patrolTarget, patrolSpeed))
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

    /// <summary> 追击：四方向里挑"走完离玩家最近"的可走格（电锯哥同款贪心），但不出守卫圈 </summary>
    private void ChaseStep()
    {
        if (player == null) return;
        Vector2 diff = (Vector2)player.position - groundPos;

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
            if (Vector2.Distance(next, homePos) > guardRadius) continue; // 守要道：不追出圈
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
            SetIdleAnim(); // 四面被堵或到圈边：站住
        }
    }

    /// <summary> 朝目标格走一步（巡逻/回位用：主方向堵了走副方向） </summary>
    private bool StepToward(Vector2 target, float speed)
    {
        Vector2 diff = target - groundPos;
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

    // ======== 舌头突刺攻击 ========

    /// <summary>
    /// 舌头长距突刺：前摇闪红+音调拉高（预警）→ 舌头（整个身体）直线弹向玩家 →
    /// 途中/终点判定伤害 → 收回原位。撞墙提前截停（舌头不穿墙）。
    /// </summary>
    private IEnumerator DoTongueAttack()
    {
        isAttacking = true;
        nextAttackTime = Time.time + attackCooldown;

        // ---- 前摇预警：面向玩家 + 红 tint + 音调拉高（电锯哥同款反应窗口） ----
        Vector2 dir = DirectionToPlayer();
        currentFacing = dir;
        baseColor = new Color(1f, 0.35f, 0.3f); // 预警红
        ApplyTint();
        PlayState("attack" + DirSuffix(dir));
        audioSource.pitch = chasePitch;

        yield return new WaitForSeconds(windupTime);

        // ---- 突刺：直线弹出去，途中第一次贴到玩家就结算伤害 ----
        if (attackSoundClip != null) audioSource.PlayOneShot(attackSoundClip);

        Vector2 startPos = groundPos;
        Vector2 lungeEnd = startPos + dir * lungeDistance;
        bool hasHit = false;
        float hitRadius = attackRange * 0.6f; // 舌尖判定圈

        while (groundPos != (Vector2)lungeEnd)
        {
            Vector2 next = Vector2.MoveTowards(groundPos, lungeEnd, lungeSpeed * Time.deltaTime);
            // 撞墙截停：舌头不穿墙
            if (Physics2D.OverlapBox(next, Vector2.one * gridSize * 0.6f, 0f, obstacleLayer) != null)
                break;
            groundPos = next;
            rb.position = groundPos; // 突刺在地面进行

            if (!hasHit && playerHealth != null && !playerHealth.isDead && PlayerDist() <= hitRadius)
            {
                hasHit = true;
                playerHealth.TakeDamage(attackDamage);
            }
            yield return null;
        }

        yield return new WaitForSeconds(0.1f); // 舌头停在最大伸长处顿一下

        // ---- 收回：弹回起手位置 ----
        while (groundPos != (Vector2)startPos)
        {
            groundPos = Vector2.MoveTowards(groundPos, startPos, lungeSpeed * 0.6f * Time.deltaTime);
            rb.position = groundPos;
            yield return null;
        }

        baseColor = Color.white;
        ApplyTint();
        SetIdleAnim();
        isAttacking = false;
    }

    // ======== 受击 ========

    private void OnHurt()
    {
        if (myHealth != null && myHealth.isDead) return;
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
        if (animator == null) return;
        int hash = Animator.StringToHash(stateName);
        if (!animator.HasState(0, hash))
        {
            if (!warnedStates.Contains(stateName))
            {
                warnedStates.Add(stateName);
                Debug.LogWarning("[舔食者] Animator 里没有状态 \"" + stateName + "\"，跳过播放（不崩）——请核对此敌人的动画状态名是否和电锯哥同一套", gameObject);
            }
            return;
        }
        animator.CrossFade(hash, 0.05f, 0, 0f);
    }

    // ======== 工具 ========

    /// <summary> 玩家距离（按地面位置算） </summary>
    private float PlayerDist()
    {
        if (player == null) return float.MaxValue;
        return Vector2.Distance(groundPos, player.position);
    }

    private Vector2 DirectionToPlayer()
    {
        if (player == null) return currentFacing;
        Vector2 diff = (Vector2)player.position - groundPos;
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
        Vector2 pos = groundPos;
        pos.x = Mathf.Round(pos.x / gridSize) * gridSize;
        pos.y = Mathf.Round(pos.y / gridSize) * gridSize;
        groundPos = pos;
        rb.position = pos;
    }

    // ======== 调试可视化（选中物体时 Scene 窗口里画三个圈，方便调参数） ========

    private void OnDrawGizmosSelected()
    {
        Vector2 center = Application.isPlaying ? homePos : (Vector2)transform.position;
        Gizmos.color = Color.green;
        Gizmos.DrawWireSphere(center, patrolRadius);   // 绿 = 巡逻圈
        Gizmos.color = Color.yellow;
        Gizmos.DrawWireSphere(center, aggroRange);     // 黄 = 听声范围
        Gizmos.color = Color.red;
        Gizmos.DrawWireSphere(center, guardRadius);    // 红 = 守卫半径（不追出圈）
    }
}
