using UnityEngine;
using System.Collections;

public class DogAI : MonoBehaviour
{
    [Header("移动设置")]
    [SerializeField] private float gridSize = 1f;
    [SerializeField] private float moveSpeed = 4f;
    [SerializeField] private float detectionRange = 6f;
    [SerializeField] private LayerMask obstacleLayer;

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
    }

    private void Update()
    {
        if (isDead || isAttacking || player == null) return;

        float dist = Vector2.Distance(transform.position, player.position);

        if (dist <= attackRange)
        {
            if (Time.time >= lastAttackTime + attackCooldown)
                StartCoroutine(DoAttack());
            return;
        }

        if (dist <= detectionRange)
        {
            Vector2 dir = GetDirectionToPlayer();
            Vector2 nextPos = targetGridPosition + dir * gridSize;

            if (IsWalkable(nextPos))
            {
                targetGridPosition = nextPos;
                isMoving = true;
                SetWalkTrigger(dir);
            }
        }
        else
        {
            if (!isMoving)
                ResetWalkTriggers();
        }
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
        Collider2D hit = Physics2D.OverlapCircle(pos, gridSize * 0.4f, obstacleLayer);
        return hit == null;
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
}
