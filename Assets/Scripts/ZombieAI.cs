using UnityEngine;
using System.Collections;
using System.Collections.Generic;

public class ZombieAI : MonoBehaviour
{
    [Header("移动设置")]
    [SerializeField] private float gridSize = 1f;
    [SerializeField] private float moveSpeed = 4f;
    [SerializeField] private float detectionRange = 5f;
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

   [Header("爆体强化")]
    [SerializeField] private bool canExplode = true;
   [SerializeField] private float damageMultiplier = 2f;
    [SerializeField] private float speedMultiplier = 1.5f;
    [SerializeField] private int teleportsToExplode = 4;

    [Header("动画参数名")]
    [SerializeField] private Animator animator;
    [SerializeField] private string walkUp = "WalkUp";
    [SerializeField] private string walkDown = "WalkDown";
    [SerializeField] private string walkLeft = "WalkLeft";
    [SerializeField] private string walkRight = "WalkRight";
    [SerializeField] private string idleUp = "IdleUp";
    [SerializeField] private string idleDown = "IdleDown";
    [SerializeField] private string idleLeft = "IdleLeft";
    [SerializeField] private string idleRight = "IdleRight";
    [SerializeField] private string attackTrigger = "Attack";
    [SerializeField] private string deathTrigger = "Death";
    [SerializeField] private string explodeTrigger = "Explode";

    [Header("新形态属性")]
    [SerializeField] private int newMaxHealth = 6;
    [SerializeField] private string newWalkUp = "NewWalkUp";
    [SerializeField] private string newWalkDown = "NewWalkDown";
    [SerializeField] private string newWalkLeft = "NewWalkLeft";
    [SerializeField] private string newWalkRight = "NewWalkRight";
    [SerializeField] private string newIdleUp = "NewIdleUp";
    [SerializeField] private string newIdleDown = "NewIdleDown";
    [SerializeField] private string newIdleLeft = "NewIdleLeft";
    [SerializeField] private string newIdleRight = "NewIdleRight";
    [SerializeField] private string newAttackTrigger = "NewAttack";
    [SerializeField] private string newDeathTrigger = "NewDeath";


    private Transform player;
    private HealthSystem playerHealth;
    private HealthSystem myHealth;
    private Rigidbody2D rb;
    private Vector2 targetGridPos;
    private Vector2 homePos;            // 出生点（牵引圈圆心，Start 记录）
    private bool givingUp = false;      // 牵引发动中：先回家，暂时不理玩家
    private bool warnedBlocked = false; // 四面被堵的一次性提示（防日志刷屏）
    private Vector2 currentFacing = Vector2.down;
    private bool isMoving = false;
    private bool isAttacking = false;
    private bool isDead = false;
    private bool hasExploded = false;
    private int deathTeleportCount = 0;
    private int baseDamage;
    private float baseSpeed;
    private float lastAttackTime;
    private HashSet<string> animatorParams = null; // Animator 参数名缓存（懒建：参数缺失防呆用，运行时参数不会变，建一次即可）

    private void Start()
    {
        if (animator == null) animator = GetComponent<Animator>();
        rb = GetComponent<Rigidbody2D>();
        if (rb == null)
        {
            rb = gameObject.AddComponent<Rigidbody2D>();
            rb.bodyType = RigidbodyType2D.Kinematic;
        }
        if (GetComponent<Collider2D>() == null)
        {
            BoxCollider2D col = gameObject.AddComponent<BoxCollider2D>();
            col.isTrigger = true;
           col.size = new Vector2(0.8f, 0.8f);
        }
        Collider2D existingCol = GetComponent<Collider2D>();
        if (existingCol != null) existingCol.isTrigger = true;
       GameObject playerObj = GameObject.FindGameObjectWithTag("Player");
        if (playerObj != null)
        {
            player = playerObj.transform;
            playerHealth = playerObj.GetComponent<HealthSystem>();
        }
        myHealth = GetComponent<HealthSystem>();
       if (myHealth != null) myHealth.OnDeath += OnZombieDeath;
        if (myHealth != null) myHealth.deathTriggerName = "";
       baseDamage = attackDamage;
        baseSpeed = moveSpeed;
        SnapToGrid();
        targetGridPos = rb.position;
        homePos = rb.position; // 记录出生点：牵引圈圆心
    }

   private void Update()
   {
       if (isDead && !hasExploded) { CheckExplode(); return; }
        if (isDead) return;
       if (isAttacking || player == null) return;

        // ===== 追击牵引：跑出 leash 圈 → 放弃玩家，先走回出生点（防跑出地图/围栏） =====
        float homeDist = Vector2.Distance(transform.position, homePos);
        if (!givingUp && homeDist > leashRange) givingUp = true;

        if (givingUp)
        {
            if (homeDist < 0.5f) givingUp = false;          // 到家 → 恢复正常
            else if (!isMoving)
            {
                if (!TryStepToward(homePos)) SetIdleTrigger(currentFacing); // 走回出生点（被堵就原地待命）
            }
            return;
        }

        float dist = Vector2.Distance(transform.position, player.position);

        if (dist <= attackRange)
        {
            if (Time.time >= lastAttackTime + attackCooldown)
                StartCoroutine(DoAttack());
            return;
        }
        if (isMoving) return;

        if (dist <= detectionRange)
        {
            // 四方向里挑"走完离玩家最近"的可走格（贪心）
            if (!TryStepToward(player.position))
            {
                SetIdleTrigger(currentFacing);
                if (!warnedBlocked)
                {
                    warnedBlocked = true;
                    Debug.LogWarning("[丧尸] " + name + " 在寻敌范围内却四面走不动：请检查 Obstacle Layer 是否勾了墙/地形的 Layer（漏勾 → 走不动或穿墙）", gameObject);
                }
            }
        }
        else
        {
            if (!isMoving) SetIdleTrigger(currentFacing);
        }
    }

    /// <summary> 朝目标点走一步：四方向里挑"走完离目标最近"的可走格。返回是否迈出了一步 </summary>
    private bool TryStepToward(Vector2 target)
    {
        Vector2 diff = target - (Vector2)transform.position;
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

        Vector2[] allDirs = new Vector2[] { dir1, dir2, -dir1, -dir2 };
        Vector2 bestDir = Vector2.zero;
        float bestDist = float.MaxValue;
        foreach (Vector2 d in allDirs)
        {
            if (d == Vector2.zero) continue;
            Vector2 testPos = targetGridPos + d * gridSize;
            if (!IsWalkable(testPos)) continue;
            float dDist = Vector2.Distance(testPos, target);
            if (dDist < bestDist) { bestDist = dDist; bestDir = d; }
        }
        if (bestDir == Vector2.zero) return false;

        targetGridPos = targetGridPos + bestDir * gridSize;
        isMoving = true;
        SetWalkTrigger(bestDir);
        return true;
    }

    private void FixedUpdate()
    {
        if (isDead || isAttacking) return;
        if (isMoving)
        {
            Vector2 cur = rb.position;
            Vector2 nxt = Vector2.MoveTowards(cur, targetGridPos, moveSpeed * Time.fixedDeltaTime);
            rb.position = nxt;
            if (Vector2.Distance(rb.position, targetGridPos) < 0.001f)
            {
                rb.position = targetGridPos;
                isMoving = false;
            }
        }
    }

    private Vector2 GetDirToPlayer()
    {
        Vector2 diff = (Vector2)player.position - (Vector2)transform.position;
        if (Mathf.Abs(diff.x) >= Mathf.Abs(diff.y))
            return new Vector2(Mathf.Sign(diff.x), 0);
        return new Vector2(0, Mathf.Sign(diff.y));
    }

    private bool IsWalkable(Vector2 pos)
    {
        // 吸附整数格：保证边界判定和网格一致
        pos.x = Mathf.Round(pos.x / gridSize) * gridSize;
        pos.y = Mathf.Round(pos.y / gridSize) * gridSize;

        // ① 地图边界（可选安全网）：走出矩形 = 不可走
        if (useBounds && (pos.x < boundsMin.x || pos.x > boundsMax.x || pos.y < boundsMin.y || pos.y > boundsMax.y))
            return false;

        // ② 终点格：0.9 格盒是否压到障碍（排除自身/子物体碰撞体）
        if (ObstacleQuery.BlockedAt(pos, Vector2.one * gridSize * 0.9f, transform, obstacleLayer))
            return false;

        // ③ 起点→终点中心连线：薄墙也不放过（射线同 obstacleLayer）
        Vector2 from = targetGridPos;
        Vector2 toDir = (pos - from).normalized;
        float toDist = Vector2.Distance(from, pos);
        if (ObstacleQuery.RaycastBlocked(from, toDir, toDist, transform, obstacleLayer))
            return false;

        // ④ 半格中点再补一盒：防"薄墙夹缝漏检"（同暴君 BlockedAt 套路）
        Vector2 mid = (from + pos) * 0.5f;
        if (ObstacleQuery.BlockedAt(mid, Vector2.one * gridSize * 0.6f, transform, obstacleLayer))
            return false;

        return true;
    }

   private void SetWalkTrigger(Vector2 dir)
   {
       ResetTriggers();
       currentFacing = dir;
        if (dir == Vector2.up) SafeSetTrigger(walkUp);
        else if (dir == Vector2.down) SafeSetTrigger(walkDown);
        else if (dir == Vector2.left) SafeSetTrigger(walkLeft);
        else if (dir == Vector2.right) SafeSetTrigger(walkRight);
   }

   private void SetIdleTrigger(Vector2 dir)
   {
       ResetTriggers();
        if (dir == Vector2.up) SafeSetTrigger(idleUp);
        else if (dir == Vector2.down) SafeSetTrigger(idleDown);
        else if (dir == Vector2.left) SafeSetTrigger(idleLeft);
        else if (dir == Vector2.right) SafeSetTrigger(idleRight);
   }

    private void ResetTriggers()
    {
        // 全部走安全通道：参数不存在就静默跳过（Animator 缺参数绝不再让协程崩）
        SafeResetTrigger(walkUp);
        SafeResetTrigger(walkDown);
        SafeResetTrigger(walkLeft);
        SafeResetTrigger(walkRight);
        SafeResetTrigger(idleUp);
        SafeResetTrigger(idleDown);
        SafeResetTrigger(idleLeft);
        SafeResetTrigger(idleRight);
        SafeResetTrigger(attackTrigger);
    }

    // ======== Animator 参数防呆（参数缺失不再抛异常 / 中断协程） ========

    /// <summary> 建/取 Animator 参数名缓存；animator 为空返回 false（后续安全调用一律静默跳过） </summary>
    private bool EnsureAnimatorParams()
    {
        if (animator == null) return false;
        if (animatorParams == null)
        {
            animatorParams = new HashSet<string>();
            foreach (AnimatorControllerParameter p in animator.parameters)
                animatorParams.Add(p.name);
        }
        return true;
    }

    /// <summary> 这个 Animator 有没有名为 name 的参数 </summary>
    private bool HasParam(string name)
    {
        if (string.IsNullOrEmpty(name)) return false;
        if (!EnsureAnimatorParams()) return false;
        return animatorParams.Contains(name);
    }

    /// <summary> 安全 SetTrigger：参数不存在就静默跳过，绝不抛异常 </summary>
    private void SafeSetTrigger(string name)
    {
        if (string.IsNullOrEmpty(name)) return;
        if (!EnsureAnimatorParams()) return;
        if (animatorParams.Contains(name)) animator.SetTrigger(name);
    }

    /// <summary> 安全 ResetTrigger：参数不存在就静默跳过，绝不抛异常 </summary>
    private void SafeResetTrigger(string name)
    {
        if (string.IsNullOrEmpty(name)) return;
        if (!EnsureAnimatorParams()) return;
        if (animatorParams.Contains(name)) animator.ResetTrigger(name);
    }

    /// <summary>
    /// 换名到 New* 形态用：candidate 存在才切换；不存在则保留旧名并返回缺失标签（供汇总警告）。
    /// 保留旧名后，后续 SafeSetTrigger/SafeResetTrigger 对缺失名会静默跳过 → 复活流程照常走完不卡死。
    /// </summary>
    private string TrySwitchParam(ref string current, string candidate, string label)
    {
        if (HasParam(candidate))
        {
            current = candidate;
            return "";
        }
        return label + " ";
    }

    private IEnumerator DoAttack()
    {
        isAttacking = true;
        lastAttackTime = Time.time;
        Vector2 dir = GetDirToPlayer();
        SetWalkTrigger(dir);
        SafeSetTrigger(attackTrigger);
        yield return new WaitForSeconds(0.3f);
        float dist = Vector2.Distance(transform.position, player.position);
        if (dist <= attackRange + 0.5f && playerHealth != null)
            playerHealth.TakeDamage(attackDamage);
        yield return new WaitForSeconds(0.2f);
        isAttacking = false;
    }

    private void OnZombieDeath()
    {
        isDead = true;
        isMoving = false;
        isAttacking = false;
        deathTeleportCount = TeleportTracker.teleportCount;
        ResetTriggers();
        SafeSetTrigger(deathTrigger);
        Collider2D col = GetComponent<Collider2D>();
        if (col != null) col.enabled = false;
    }

   private void CheckExplode()
   {
       if (hasExploded) return;
        if (!canExplode) return;
       Debug.Log(string.Format("死亡传送: {0}, 当前传送: {1}, 需要: {2}", deathTeleportCount, TeleportTracker.teleportCount, teleportsToExplode));
       if (TeleportTracker.teleportCount >= deathTeleportCount + teleportsToExplode)
            StartCoroutine(ExplodeAndRevive());
    }

    private IEnumerator ExplodeAndRevive()
    {
        hasExploded = true;
        ResetTriggers();
        SafeSetTrigger(explodeTrigger);
        yield return new WaitForSeconds(3.0f);

        // ---- 换名到 New* 形态：逐个确认参数在 Animator 里存在，缺失的保留旧名并汇总警告 ----
        //     关键：绝不因 Animator 缺参数而中断协程（否则僵尸会卡在半复活状态，AI 接不上）
        string missing = "";
        missing += TrySwitchParam(ref deathTrigger, newDeathTrigger, "NewDeath");
        missing += TrySwitchParam(ref walkUp, newWalkUp, "NewWalkUp");
        missing += TrySwitchParam(ref walkDown, newWalkDown, "NewWalkDown");
        missing += TrySwitchParam(ref walkLeft, newWalkLeft, "NewWalkLeft");
        missing += TrySwitchParam(ref walkRight, newWalkRight, "NewWalkRight");
        missing += TrySwitchParam(ref idleUp, newIdleUp, "NewIdleUp");
        missing += TrySwitchParam(ref idleDown, newIdleDown, "NewIdleDown");
        missing += TrySwitchParam(ref idleLeft, newIdleLeft, "NewIdleLeft");
        missing += TrySwitchParam(ref idleRight, newIdleRight, "NewIdleRight");
        missing += TrySwitchParam(ref attackTrigger, newAttackTrigger, "NewAttack");
        if (!string.IsNullOrEmpty(missing))
        {
            Debug.LogWarning("[僵尸] " + name + " 的 Animator 缺少这些二形态参数：" + missing.Trim()
                + " → 这些动作沿用旧参数名继续（复活流程不受影响、不会卡死）。"
                + " 想用二形态动画，请把上面列出的参数补进这个 Animator。", gameObject);
        }

        attackDamage = Mathf.RoundToInt(baseDamage * damageMultiplier);
        moveSpeed = baseSpeed * speedMultiplier;
        if (myHealth != null)
        {
            myHealth.maxHealth = newMaxHealth;
            myHealth.currentHealth = newMaxHealth;
            myHealth.isDead = false;
        }
        Collider2D col = GetComponent<Collider2D>();
        if (col != null) col.enabled = true;
        isDead = false;
        givingUp = false; // 复活后牵引状态复位
        SnapToGrid();
        targetGridPos = rb.position;
        ResetTriggers();
        SafeSetTrigger(idleDown);
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
