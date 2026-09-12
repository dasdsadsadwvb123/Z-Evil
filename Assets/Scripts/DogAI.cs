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
    [Tooltip("牵引半径：追击中离出生点超过这个距离 → 放弃玩家、先走回出生点（防地图外游荡）")]
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
    }

    private void Update()
    {
        if (isDead || isAttacking || player == null) return;

        // ===== 追击牵引：跑出 leash 圈 → 放弃玩家，先走回出生点（防跑出地图/围栏） =====
        float homeDist = Vector2.Distance(rb.position, homePos);
        if (!givingUp && homeDist > leashRange) givingUp = true;

        if (givingUp)
        {
            if (homeDist < 0.5f) { givingUp = false; if (!isMoving) ResetWalkTriggers(); }
            else if (!isMoving) { if (!StepToward(homePos)) ResetWalkTriggers(); } // 回出生点（被堵就原地待命）
            return;
        }

        float dist = Vector2.Distance(transform.position, player.position);

        if (dist <= attackRange)
        {
            if (Time.time >= lastAttackTime + attackCooldown)
                StartCoroutine(DoAttack());
            return;
        }

        if (dist <= detectionRange)
        {
            if (!StepToward(player.position))
            {
                if (!isMoving) ResetWalkTriggers();
                if (!warnedBlocked)
                {
                    warnedBlocked = true;
                    Debug.LogWarning("[丧尸狗] " + name + " 在寻敌范围内却走不动：请检查 Obstacle Layer 是否勾了墙/地形的 Layer（漏勾 → 走不动或穿墙）", gameObject);
                }
            }
        }
        else
        {
            if (!isMoving)
                ResetWalkTriggers();
        }
    }

    /// <summary> 朝目标点走一格（单一主方向，和原追击逻辑一致）；走不了返回 false </summary>
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

    /// <summary> 从当前位置指向目标的主方向（单轴） </summary>
    private Vector2 GetDirectionTo(Vector2 target)
    {
        Vector2 diff = target - (Vector2)transform.position;
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
                rb.position = targetGridPosition;
                isMoving = false;
                ResetWalkTriggers();
            }
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
