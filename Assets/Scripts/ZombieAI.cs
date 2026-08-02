using UnityEngine;
using System.Collections;

public class ZombieAI : MonoBehaviour
{
    [Header("移动设置")]
    [SerializeField] private float gridSize = 1f;
    [SerializeField] private float moveSpeed = 4f;
    [SerializeField] private float detectionRange = 5f;
    [SerializeField] private LayerMask obstacleLayer;

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
    private Vector2 currentFacing = Vector2.down;
    private bool isMoving = false;
    private bool isAttacking = false;
    private bool isDead = false;
    private bool hasExploded = false;
    private int deathTeleportCount = 0;
    private int baseDamage;
    private float baseSpeed;
    private float lastAttackTime;

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
    }

   private void Update()
   {
       if (isDead && !hasExploded) { CheckExplode(); return; }
        if (isDead) return;
       if (isAttacking || player == null) return;

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
            Vector2 diff = (Vector2)player.position - (Vector2)transform.position;
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
                if (IsWalkable(testPos))
                {
                    float dDist = Vector2.Distance(testPos, player.position);
                    if (dDist < bestDist)
                    {
                        bestDist = dDist;
                        bestDir = d;
                    }
                }
            }
            if (bestDir != Vector2.zero)
            {
                targetGridPos = targetGridPos + bestDir * gridSize;
                isMoving = true;
                SetWalkTrigger(bestDir);
            }
            else
            {
                SetIdleTrigger(currentFacing);
            }
       }
       else
        {
            if (!isMoving) SetIdleTrigger(currentFacing);
        }
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
        if (Physics2D.OverlapBox(pos, Vector2.one * gridSize * 0.9f, 0f, obstacleLayer) != null)
            return false;

        Vector2 from = targetGridPos;
        Vector2 toDir = (pos - from).normalized;
        float toDist = Vector2.Distance(from, pos);
        if (Physics2D.Raycast(from, toDir, toDist, obstacleLayer).collider != null)
            return false;

        return true;
    }

   private void SetWalkTrigger(Vector2 dir)
   {
       ResetTriggers();
       currentFacing = dir;
        if (dir == Vector2.up) animator.SetTrigger(walkUp);
        else if (dir == Vector2.down) animator.SetTrigger(walkDown);
        else if (dir == Vector2.left) animator.SetTrigger(walkLeft);
        else if (dir == Vector2.right) animator.SetTrigger(walkRight);
   }

   private void SetIdleTrigger(Vector2 dir)
   {
       ResetTriggers();
        if (dir == Vector2.up) animator.SetTrigger(idleUp);
        else if (dir == Vector2.down) animator.SetTrigger(idleDown);
        else if (dir == Vector2.left) animator.SetTrigger(idleLeft);
        else if (dir == Vector2.right) animator.SetTrigger(idleRight);
   }

    private void ResetTriggers()
    {
        animator.ResetTrigger(walkUp);
        animator.ResetTrigger(walkDown);
        animator.ResetTrigger(walkLeft);
        animator.ResetTrigger(walkRight);
        animator.ResetTrigger(idleUp);
        animator.ResetTrigger(idleDown);
        animator.ResetTrigger(idleLeft);
        animator.ResetTrigger(idleRight);
        animator.ResetTrigger(attackTrigger);
    }

    private IEnumerator DoAttack()
    {
        isAttacking = true;
        lastAttackTime = Time.time;
        Vector2 dir = GetDirToPlayer();
        SetWalkTrigger(dir);
        animator.SetTrigger(attackTrigger);
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
        animator.SetTrigger(deathTrigger);
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
        animator.SetTrigger(explodeTrigger);
        yield return new WaitForSeconds(3.0f);
        deathTrigger = newDeathTrigger;
        walkUp = newWalkUp;
        walkDown = newWalkDown;
        walkLeft = newWalkLeft;
        walkRight = newWalkRight;
        idleUp = newIdleUp;
        idleDown = newIdleDown;
        idleLeft = newIdleLeft;
        idleRight = newIdleRight;
        attackTrigger = newAttackTrigger;
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
        SnapToGrid();
        targetGridPos = rb.position;
        ResetTriggers();
        animator.SetTrigger(idleDown);
    }

    private void SnapToGrid()
    {
        Vector2 pos = rb.position;
        pos.x = Mathf.Round(pos.x / gridSize) * gridSize;
        pos.y = Mathf.Round(pos.y / gridSize) * gridSize;
        rb.position = pos;
    }
}
