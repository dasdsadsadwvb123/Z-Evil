using UnityEngine;
using System.Collections;

public class PixelGridMovement : MonoBehaviour
{
    [Header("网格设置")]
    [Tooltip("一个方格的世界坐标大小（对应16x16像素）")]
    [SerializeField] private float tileSize = 1f;

    [Tooltip("每个方格的移动距离，调小则步长更短，转向更灵活")]
    [SerializeField] private float stepSize = 0.5f;

    [Tooltip("移动速度（世界单位/秒）")]
    [SerializeField] private float moveSpeed = 5f;

    [Tooltip("输入缓冲时间（秒），移动过程中提前按下方向键的有效保持时间")]
    [SerializeField] private float inputBufferDuration = 0.15f;

   [Header("碰撞检测")]
   [SerializeField] private LayerMask obstacleLayer;
    [SerializeField] private BoxCollider2D playerCollider;

   [Header("组件引用")]
   [SerializeField] private Animator animator;

   [Header("攻击锁定")]
   [Tooltip("开枪/划刀时锁住移动的时长(秒)，应与动画时长匹配")]
   [SerializeField] private float attackLockDuration = 0.5f;

    [HideInInspector] public bool frozen = false;

    private Rigidbody2D rb;
    private Vector2 targetGridPosition;
    private bool isMoving = false;
    private Vector2 facingDirection = Vector2.down;
    private Vector2 lastWalkDirection = Vector2.zero;
    private Vector2 bufferedInput = Vector2.zero;
    private float bufferedInputTime = -1f;
    private Vector2 playerBoxSize;
    private Coroutine attackLockRoutine;
    private float attackInputCooldownEnd = 0f;

    void Start()
    {
        rb = GetComponent<Rigidbody2D>();
        if (rb == null)
        {
            rb = gameObject.AddComponent<Rigidbody2D>();
            rb.bodyType = RigidbodyType2D.Kinematic;
        }

        if (animator == null)
            animator = GetComponent<Animator>();

        if (playerCollider == null) playerCollider = GetComponent<BoxCollider2D>();
        playerBoxSize = playerCollider != null ? playerCollider.size * 0.9f : new Vector2(tileSize * 1.8f, tileSize * 1.8f);

        if (TeleportManager.Instance != null && TeleportManager.Instance.TryGetSpawnPosition(out Vector2 spawnPos))
        {
            TeleportTo(spawnPos);
        }
        else
        {
            SnapToGrid(rb.position);
            targetGridPosition = rb.position;
            TriggerIdleAnimation(facingDirection);
        }
    }

    void Update()
    {
        // 在移动处理前锁定，防止射击/挥刀时先行一步
        if (!frozen && Input.GetKeyDown(KeyCode.J))
        {
            LockMovement();
            return;
        }

        if (frozen)
        {
            // 冻结时（对话/剧情中）强制切回 idle 站立姿态，不再卡在跑动动画
            isMoving = false;
            TriggerIdleAnimation(facingDirection);
            return;
        }

        Vector2 rawInput = new Vector2(Input.GetAxisRaw("Horizontal"), Input.GetAxisRaw("Vertical"));

        if (rawInput != Vector2.zero)
        {
            Vector2 snappedInput;
            if (Mathf.Abs(rawInput.x) >= Mathf.Abs(rawInput.y))
                snappedInput = new Vector2(Mathf.Sign(rawInput.x), 0);
            else
                snappedInput = new Vector2(0, Mathf.Sign(rawInput.y));

            facingDirection = snappedInput;
            bufferedInput = snappedInput;
            bufferedInputTime = Time.time;
        }

        if (!isMoving && bufferedInput != Vector2.zero)
        {
            TryMove(bufferedInput);
            bufferedInput = Vector2.zero;
            bufferedInputTime = -1f;
        }

        if (!isMoving && rawInput == Vector2.zero)
            TriggerIdleAnimation(facingDirection);
    }

    void FixedUpdate()
    {
        if (frozen || !isMoving) return;

        Vector2 currentPos = rb.position;
        Vector2 newPos = Vector2.MoveTowards(currentPos, targetGridPosition, moveSpeed * Time.fixedDeltaTime);
        rb.MovePosition(newPos);

        if (Vector2.Distance(rb.position, targetGridPosition) < 0.001f)
        {
            rb.position = targetGridPosition;
           isMoving = false;

            if (bufferedInput != Vector2.zero && Time.time - bufferedInputTime <= inputBufferDuration)
            {
                TryMove(bufferedInput);
                bufferedInput = Vector2.zero;
                bufferedInputTime = -1f;
            }
            else
            {
                TriggerIdleAnimation(facingDirection);
            }
        }
    }

    private void TryMove(Vector2 direction)
    {
        Vector2 targetPos = targetGridPosition + direction * stepSize;

        if (CanMoveTo(targetPos))
        {
            targetGridPosition = targetPos;
            isMoving = true;
            if (direction != lastWalkDirection)
            {
                lastWalkDirection = direction;
                TriggerWalkAnimation(direction);
            }
        }
    }

    private bool CanMoveTo(Vector2 position)
    {
        Collider2D hit = Physics2D.OverlapBox(position, playerBoxSize, 0f, obstacleLayer);
        return hit == null;
    }

    private void SnapToGrid(Vector2 position)
    {
        float snappedX = Mathf.Round(position.x / tileSize) * tileSize;
        float snappedY = Mathf.Round(position.y / tileSize) * tileSize;
        rb.position = new Vector2(snappedX, snappedY);
    }

    // ======== 动画控制 ========

    private void ResetAllTriggers()
    {
        if (animator == null) return;
        animator.ResetTrigger("IdleUp");
        animator.ResetTrigger("IdleDown");
        animator.ResetTrigger("IdleLeft");
        animator.ResetTrigger("IdleRight");
        animator.ResetTrigger("WalkUp");
        animator.ResetTrigger("WalkDown");
        animator.ResetTrigger("WalkLeft");
        animator.ResetTrigger("WalkRight");
        animator.ResetTrigger("FireUp");
        animator.ResetTrigger("FireDown");
        animator.ResetTrigger("FireLeft");
        animator.ResetTrigger("FireRight");
        animator.ResetTrigger("Death");
        animator.ResetTrigger("FlashUp");
        animator.ResetTrigger("FlashDown");
        animator.ResetTrigger("FlashLeft");
        animator.ResetTrigger("FlashRight");
        animator.ResetTrigger("Slash");
    }

    private void TriggerWalkAnimation(Vector2 direction)
    {
        if (animator == null) return;
        ResetAllTriggers();
        animator.SetTrigger(GetWalkTriggerName(direction));
    }

    private void TriggerIdleAnimation(Vector2 direction)
    {
        if (animator == null) return;
        ResetAllTriggers();
        animator.SetTrigger(GetIdleTriggerName(direction));
        lastWalkDirection = Vector2.zero;
    }

    private string GetWalkTriggerName(Vector2 dir)
    {
        if (dir == Vector2.up)    return "WalkUp";
        if (dir == Vector2.down)  return "WalkDown";
        if (dir == Vector2.left)  return "WalkLeft";
        if (dir == Vector2.right) return "WalkRight";
        return "WalkDown";
    }

    private string GetIdleTriggerName(Vector2 dir)
    {
        if (dir == Vector2.up)    return "IdleUp";
        if (dir == Vector2.down)  return "IdleDown";
        if (dir == Vector2.left)  return "IdleLeft";
        if (dir == Vector2.right) return "IdleRight";
        return "IdleDown";
    }

    // ======== 射击动画触发 ========

    private string GetShootTriggerName(Vector2 dir)
    {
        if (dir == Vector2.up)    return "FireUp";
        if (dir == Vector2.down)  return "FireDown";
        if (dir == Vector2.left)  return "FireLeft";
        if (dir == Vector2.right) return "FireRight";
        return "FireDown";
    }

    /// <summary> 由 Gun.cs 调用，触发当前朝向的射击动画 </summary>
    public void PlayShoot()
    {
        if (animator == null) return;
        LockMovement();
        ResetAllTriggers();
        animator.SetTrigger(GetShootTriggerName(facingDirection));
    }

    // ======== 闪光动画触发（F交互成功时） ========

    private string GetFlashTriggerName(Vector2 dir)
    {
        if (dir == Vector2.up)    return "FlashUp";
        if (dir == Vector2.down)  return "FlashDown";
        if (dir == Vector2.left)  return "FlashLeft";

        if (dir == Vector2.right) return "FlashRight";
        return "FlashDown";
    }

    /// <summary> 由 PlayerPickup.cs 调用，触发当前朝向的闪光动画 </summary>
    public void PlayFlash()
    {
        if (animator == null) return;
        ResetAllTriggers();
        animator.SetTrigger(GetFlashTriggerName(facingDirection));
    }

    // ======== 公开接口 ========

    public Vector2 GetFacingDirection() => facingDirection;
    public Vector2 GetCurrentGridPosition() => targetGridPosition;

    public void TeleportTo(Vector2 newPosition)
    {
        if (rb == null)
        {
            rb = GetComponent<Rigidbody2D>();
            if (rb == null)
            {
                rb = gameObject.AddComponent<Rigidbody2D>();
                rb.bodyType = RigidbodyType2D.Kinematic;
            }
        }
        isMoving = false;
        SnapToGrid(newPosition);
        targetGridPosition = rb.position;
        TriggerIdleAnimation(facingDirection);
    }

    // ======== 编辑器辅助 ========

    void OnDrawGizmosSelected()
    {
       Vector2 pos = Application.isPlaying ? targetGridPosition : (Vector2)transform.position;
        Vector2 boxSize = Application.isPlaying ? playerBoxSize : new Vector2(tileSize * 1.8f, tileSize * 1.8f);
       Vector2 fullSize = new Vector2(tileSize * 2, tileSize * 2);

        if (!Application.isPlaying)
        {
            float x = Mathf.Round(pos.x / tileSize) * tileSize;
            float y = Mathf.Round(pos.y / tileSize) * tileSize;
            pos = new Vector2(x, y);
        }

        Gizmos.color = Color.cyan;
        Gizmos.DrawWireCube(pos, fullSize);

        Gizmos.color = new Color(1f, 0.3f, 0.3f, 0.6f);
        Gizmos.DrawWireCube(pos, boxSize);

        if (!Application.isPlaying)
        {
            Gizmos.color = new Color(0, 1, 1, 0.12f);
            Gizmos.DrawWireCube(pos, Vector2.one * tileSize);
        }
    }
    public void PlaySlash()
    {
        if (animator == null) return;
        LockMovement();
        ResetAllTriggers();
        animator.SetTrigger("Slash");
    }

    // ======== 攻击锁定 ========

    private void LockMovement()
    {
        frozen = true;
        isMoving = false;
        bufferedInput = Vector2.zero;
        if (attackLockRoutine != null)
            StopCoroutine(attackLockRoutine);
        attackLockRoutine = StartCoroutine(AttackLockRoutine());
    }

    private IEnumerator AttackLockRoutine()
    {
        yield return new WaitForSeconds(attackLockDuration);
        frozen = false;
       // 解锁后强制退出动画状态
        TriggerIdleAnimation(facingDirection);
        attackLockRoutine = null;
    }

}
