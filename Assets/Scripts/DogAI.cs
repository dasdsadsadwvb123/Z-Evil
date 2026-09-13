using UnityEngine;
using System.Collections;

public class DogAI : MonoBehaviour
{
    [Header("移动设置")]
    [SerializeField] private float gridSize = 1f;
    [SerializeField] private float moveSpeed = 4f;
    [SerializeField] private float detectionRange = 6f;
    [SerializeField] private LayerMask obstacleLayer;

    [Header("追击牵引 & 边界（防跑出地图外）")]
    [Tooltip("牵引半径：只有在【追丢玩家】(超出 Detection Range) 且【离出生点超过这个距离】时，才放弃追击、先走回出生点（防地图外游荡）。追击途中不会再主动放弃")]
    [SerializeField] private float leashRange = 14f;
    [Tooltip("勾上 = 用下面这个矩形硬约束可行走范围（地图没有边界墙时的安全网）")]
    [SerializeField] private bool useBounds = false;
    [SerializeField] private Vector2 boundsMin = new Vector2(0f, 0f);
    [SerializeField] private Vector2 boundsMax = new Vector2(0f, 0f);

    [Header("攻击设置")]
    [SerializeField] private int attackDamage = 1;
    [SerializeField] private float attackRange = 1.2f;
    [SerializeField] private float attackCooldown = 1.5f;

    [Header("动画")]
    [SerializeField] private Animator animator;
    [SerializeField] private string walkUpTrigger = "WalkUp";
    [SerializeField] private string walkDownTrigger = "WalkDown";
    [SerializeField] private string walkLeftTrigger = "WalkLeft";
    [SerializeField] private string walkRightTrigger = "WalkRight";
    [SerializeField] private string attackTrigger = "Attack";

    private Transform player;
    private HealthSystem playerHealth;
    private HealthSystem myHealth;
    private Rigidbody2D rb;
    private Vector2 targetGridPosition;
    private Vector2 homePos;            // 出生点（牵引圈圆心，Start 记录）
    private bool givingUp = false;      // 牵引发动中：先回家，暂时不理玩家
    private bool warnedBlocked = false; // 走不动的一次性提示（防日志刷屏）
    private Vector2 currentFacing = Vector2.down;
    private bool isMoving = false;
    private bool isAttacking = false;
    private bool isDead = false;
    private float lastAttackTime;

    // ===== 卡住诊断（照搬 TyrantAI 思路，适配狗：静止不动是正常的，只在"本该移动"时计时） =====
    private float stuckMoveTimer = 0f;         // 连续没挪动的累计时长（秒）
    private Vector2 lastMoveCheckPos;          // 上次检查时的位置
    private bool stuckWarned = false;          // 本次卡住是否已警告（防刷屏）
    private const float StuckWarnSeconds = 4f; // 连续这么久没挪动就打一次警告

    private void Start()
    {
        rb = GetComponent<Rigidbody2D>();
        if (rb == null)
        {
            rb = gameObject.AddComponent<Rigidbody2D>();
            rb.bodyType = RigidbodyType2D.Kinematic;
        }

        GameObject playerObj = GameObject.FindGameObjectWithTag("Player");
        if (playerObj != null)
        {
            player = playerObj.transform;
            playerHealth = playerObj.GetComponent<HealthSystem>();
        }

        myHealth = GetComponent<HealthSystem>();
        if (myHealth != null)
            myHealth.OnDeath += OnDogDeath;

        SnapToGrid();
        targetGridPosition = rb.position;
        homePos = rb.position; // 记录出生点：牵引圈圆心
        lastMoveCheckPos = rb.position; // 卡住诊断的位置基准
    }

    private void Update()
    {
        if (isDead || isAttacking || player == null) return;

        // 每帧先把两个距离算好（牵引判定要用 dist，必须提前算，别在下面重复声明）
        float dist = Vector2.Distance(transform.position, player.position);
        float homeDist = Vector2.Distance(rb.position, homePos);

        // ===== 追击牵引：只有"追丢(dist > detectionRange) + 离家过远(homeDist > leashRange)"两条件同时满足才放弃 =====
        // （修"玩家把它引得稍远、狗就自己走掉"：追击途中绝不主动放弃，只有追丢了才谈回家）
        if (!givingUp && dist > detectionRange && homeDist > leashRange) givingUp = true;

        if (givingUp)
        {
            if (homeDist < 0.5f)
            {
                SyncGridTarget();   // 到家：吸附回整数格
                givingUp = false;
                ResetWalkTriggers();
                CheckStuck(false);
            }
            else if (!isMoving)
            {
                if (!StepToward(homePos)) { ResetWalkTriggers(); SyncGridTarget(); } // 回家被堵：吸附 + 原地待命
                CheckStuck(true); // 回家途中：允许卡住诊断
            }
            return;
        }

        if (dist <= attackRange)
        {
            if (Time.time >= lastAttackTime + attackCooldown)
                StartCoroutine(DoAttack());
            CheckStuck(false); // 攻击/待机不算"该移动"
            return;
        }

        // ★ 核心守卫：一格没走完，不许再规划下一格（修"目标点每帧飙远 → 乱飘 / 穿墙"）
        if (isMoving) return;

        if (dist <= detectionRange)
        {
            // 四方向贪心追一格（可沿墙绕障）；四面被堵就原地待机
            if (!StepChase())
            {
                ResetWalkTriggers();
                if (!warnedBlocked)
                {
                    warnedBlocked = true;
                    Debug.LogWarning("[丧尸狗] " + name + " 在寻敌范围内四面被堵：请检查 Obstacle Layer 是否勾了墙/地形的 Layer（漏勾 → 走不动或穿墙）", gameObject);
                }
            }
            CheckStuck(true); // 追击中：允许卡住诊断
        }
        else
        {
            ResetWalkTriggers();
            CheckStuck(false); // 没在追人 = 静止是正常的，清零计时
        }
    }

    /// <summary> 朝目标点走一格（单一主方向，回出生点用）；走不了返回 false。基准统一用 targetGridPosition </summary>
    private bool StepToward(Vector2 target)
    {
        Vector2 dir = GetDirectionTo(target);
        Vector2 nextPos = targetGridPosition + dir * gridSize;
        if (!IsWalkable(nextPos)) return false;
        targetGridPosition = nextPos;
        isMoving = true;
        SetWalkTrigger(dir);
        return true;
    }

    /// <summary>
    /// 四方向贪心追一格（照搬 TyrantAI.ChaseStep 的做法）：从 targetGridPosition 出发，
    /// 试上/下/左/右四个相邻格，挑"可走且离玩家最近"的那格迈进；四面全堵 → 返回 false（原地待机）。
    /// 用【严格小于】比较：等距时取候选里的第一个，避免来回摆。
    /// </summary>
    private bool StepChase()
    {
        if (player == null) return false;

        Vector2 diff = (Vector2)player.position - targetGridPosition;
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
            Vector2 next = targetGridPosition + d * gridSize;
            if (!IsWalkable(next)) continue;
            float dDist = Vector2.Distance(next, player.position);
            if (dDist < bestDist) // 严格小于：等距取先到的，不来回摆
            {
                bestDist = dDist;
                bestDir = d;
            }
        }

        if (bestDir == Vector2.zero) return false; // 四面被堵

        targetGridPosition += bestDir * gridSize;
        isMoving = true;
        SetWalkTrigger(bestDir); // 保留狗的动画触发方式：每走一步触发一次
        return true;
    }

    /// <summary> 从基准格（targetGridPosition）指向目标的主方向（单轴）；统一基准，避免用 transform.position 造成方向横跳 </summary>
    private Vector2 GetDirectionTo(Vector2 target)
    {
        Vector2 diff = target - targetGridPosition;
        if (Mathf.Abs(diff.x) >= Mathf.Abs(diff.y))
            return new Vector2(Mathf.Sign(diff.x), 0);
        return new Vector2(0, Mathf.Sign(diff.y));
    }

    private void FixedUpdate()
    {
        if (isDead || isAttacking) return;

        if (isMoving)
        {
            Vector2 current = rb.position;
            Vector2 next = Vector2.MoveTowards(current, targetGridPosition, moveSpeed * Time.fixedDeltaTime);
            rb.MovePosition(next);

            if (Vector2.Distance(rb.position, targetGridPosition) < 0.001f)
            {
                SyncGridTarget(); // 到达：吸附回整数格 + 复位 isMoving
                ResetWalkTriggers();
            }
        }
    }

    /// <summary> 把位置吸附回最近整数格并同步网格目标（到达 / 被堵 / 放弃回家结束后调用） </summary>
    private void SyncGridTarget()
    {
        Vector2 snap = rb.position;
        snap.x = Mathf.Round(snap.x / gridSize) * gridSize;
        snap.y = Mathf.Round(snap.y / gridSize) * gridSize;
        if (!ObstacleQuery.BlockedCircle(snap, gridSize * 0.4f, transform, obstacleLayer)) rb.position = snap; // 被堵就不硬塞
        targetGridPosition = rb.position;
        isMoving = false;
    }

    /// <summary>
    /// 卡住诊断：只在"本该移动"的路径（追击中 / 回家中）累加计时；其它情况清零。
    /// 连续 StuckWarnSeconds 秒没挪动 → 打一次警告（提示检查 Obstacle Layer / gridSize），不刷屏；动过之后复位允许再报。
    /// </summary>
    private void CheckStuck(bool shouldBeMoving)
    {
        if (!shouldBeMoving)
        {
            stuckMoveTimer = 0f;
            stuckWarned = false;
            lastMoveCheckPos = rb.position;
            return;
        }

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
            Debug.LogWarning("[丧尸狗] " + name + " 已连续 " + StuckWarnSeconds + " 秒没挪动：请检查 Obstacle Layer 是否把墙都勾上了、以及 gridSize 是否和瓦片一致。", gameObject);
        }
    }

    private Vector2 GetDirectionToPlayer()
    {
        Vector2 diff = (Vector2)player.position - (Vector2)transform.position;

        if (Mathf.Abs(diff.x) >= Mathf.Abs(diff.y))
            return new Vector2(Mathf.Sign(diff.x), 0);
        else
            return new Vector2(0, Mathf.Sign(diff.y));
    }

    private bool IsWalkable(Vector2 pos)
    {
        // 吸附整数格
        pos.x = Mathf.Round(pos.x / gridSize) * gridSize;
        pos.y = Mathf.Round(pos.y / gridSize) * gridSize;

        // ① 地图边界（可选安全网）：走出矩形 = 不可走
        if (useBounds && (pos.x < boundsMin.x || pos.x > boundsMax.x || pos.y < boundsMin.y || pos.y > boundsMax.y))
            return false;

        // ② 终点格圆形检测（排除自身/子物体碰撞体）
        if (ObstacleQuery.BlockedCircle(pos, gridSize * 0.4f, transform, obstacleLayer))
            return false;

        // ③ 起点→终点中心连线射线：薄墙不放过（原来只查圆 → 薄墙会漏，补上）
        Vector2 from = targetGridPosition;
        Vector2 rayDir = (pos - from).normalized;
        float rayDist = Vector2.Distance(from, pos);
        if (ObstacleQuery.RaycastBlocked(from, rayDir, rayDist, transform, obstacleLayer))
            return false;

        // ④ 半格中点再补一盒（防薄墙夹缝漏检）
        Vector2 mid = (from + pos) * 0.5f;
        if (ObstacleQuery.BlockedAt(mid, Vector2.one * gridSize * 0.6f, transform, obstacleLayer))
            return false;

        return true;
    }

    private void SetWalkTrigger(Vector2 dir)
    {
        ResetWalkTriggers();
        currentFacing = dir;

        if (dir == Vector2.up) animator.SetTrigger(walkUpTrigger);
        else if (dir == Vector2.down) animator.SetTrigger(walkDownTrigger);
        else if (dir == Vector2.left) animator.SetTrigger(walkLeftTrigger);
        else if (dir == Vector2.right) animator.SetTrigger(walkRightTrigger);
    }

    private void ResetWalkTriggers()
    {
        animator.ResetTrigger(walkUpTrigger);
        animator.ResetTrigger(walkDownTrigger);
        animator.ResetTrigger(walkLeftTrigger);
        animator.ResetTrigger(walkRightTrigger);
    }

    private IEnumerator DoAttack()
    {
        isAttacking = true;
        lastAttackTime = Time.time;

        // 面朝玩家方向
        Vector2 dir = GetDirectionToPlayer();
        SetWalkTrigger(dir);

        // 播放攻击动画
        animator.SetTrigger(attackTrigger);

        // 等待攻击动画时间
        yield return new WaitForSeconds(0.3f);

        // 对玩家造成伤害
        float dist = Vector2.Distance(transform.position, player.position);
        if (dist <= attackRange + 0.5f && playerHealth != null)
            playerHealth.TakeDamage(attackDamage);

        yield return new WaitForSeconds(0.2f);
        isAttacking = false;
    }

    private void OnDogDeath()
    {
        isDead = true;
        isMoving = false;
        ResetWalkTriggers();
    }

    private void SnapToGrid()
    {
        Vector2 pos = rb.position;
        pos.x = Mathf.Round(pos.x / gridSize) * gridSize;
        pos.y = Mathf.Round(pos.y / gridSize) * gridSize;
        rb.position = pos;
    }

    /// <summary> 选中时可视化：黄圈 = 牵引半径（离出生点最远追击距离）；青框 = 地图边界矩形（若启用） </summary>
    private void OnDrawGizmosSelected()
    {
        Vector2 center = Application.isPlaying ? homePos : (Vector2)transform.position;
        Gizmos.color = Color.yellow;
        Gizmos.DrawWireSphere(center, leashRange); // 黄 = 牵引圈
        if (useBounds)
        {
            Gizmos.color = Color.cyan;
            Vector3 c = new Vector3((boundsMin.x + boundsMax.x) * 0.5f, (boundsMin.y + boundsMax.y) * 0.5f, 0f);
            Vector3 s = new Vector3(boundsMax.x - boundsMin.x, boundsMax.y - boundsMin.y, 0f);
            Gizmos.DrawWireCube(c, s); // 青 = 可行走矩形
        }
    }
}
