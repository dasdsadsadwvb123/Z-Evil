using UnityEngine;
using System.Collections;

public class Gun : MonoBehaviour
{
    [Header("射击设置")]
    public KeyCode fireKey = KeyCode.J;
    public LayerMask shootableLayers = -1;
    public Transform firePoint;

    [Header("换弹设置")]
    [Tooltip("按这个键换弹")]
    public KeyCode reloadKey = KeyCode.R;
    [Tooltip("换弹需要的时间（秒），期间不能开枪")]
    public float reloadDuration = 1.5f;
    [Tooltip("换弹音效（在 Inspector 里拖进来，不配就不播）")]
    public AudioClip reloadClip;

    private Inventory inventory;
    private PixelGridMovement playerMovement;
    private float lastFireTime;

    /// <summary> 当前是否在换弹中（换弹期间禁止开枪，防止触发开枪动画） </summary>
    public bool IsReloading { get { return isReloading; } }
    private bool isReloading = false;

    private void Start()
    {
        inventory = GetComponent<Inventory>();
        playerMovement = GetComponent<PixelGridMovement>();
    }

    private void Update()
    {
        if (inventory == null) return;

        // 换弹中：忽略所有开枪输入，自然就不会触发开枪动画
        if (isReloading) return;

        GunData gunData = inventory.GetEquippedGun();
        if (gunData == null) return;

        // 按 R 换弹（只对远程枪有效）
        if (Input.GetKeyDown(reloadKey) && gunData.range > 1f)
        {
            TryReload();
            return;
        }

        if (Input.GetKeyDown(fireKey) && Time.time >= lastFireTime + gunData.fireRate)
        {
            // 远程武器开枪前必须先消耗一发子弹（近战小刀不需要弹药）
            if (gunData.range > 1f && !inventory.ConsumeAmmo())
            {
                // 弹药为 0：空仓咔嗒，禁止开枪
                DryFire();
                return;
            }

            // 近战武器攻击前消耗 1 点耐久（耐久耗尽就不能再砍了）
            if (gunData.range <= 1f && !inventory.ConsumeDurability())
            {
                // 耐久为 0：刀钝了/坏了，攻击失败
                DryBlade();
                return;
            }

            Fire(gunData);
            if (gunData.range > 1f) playerMovement?.PlayShoot();
            lastFireTime = Time.time;
        }
    }

    /// <summary> 弹尽时触发：空仓提示（以后可以在这里播放"咔嗒"音效） </summary>
    private void DryFire()
    {
        Debug.Log("[弹药] 咔嗒…… 没子弹了！");
    }

    /// <summary> 近战耐久耗尽时触发：武器报废提示 </summary>
    private void DryBlade()
    {
        Debug.Log("[耐久] 武器磨损报废了…… 得找把新的了！");
    }

    /// <summary> 尝试换弹：弹夹没满 + 有备用弹药 才允许开始 </summary>
    private void TryReload()
    {
        if (!inventory.CanReload()) return;
        StartCoroutine(ReloadRoutine());
    }

    /// <summary> 换弹流程：锁开枪 → 播音效 → 等时间 → 补弹夹 → 解锁 </summary>
    private IEnumerator ReloadRoutine()
    {
        isReloading = true;

        // 播放换弹音效（自己配的素材，从 Inspector 拖到 reloadClip 上）
        if (reloadClip != null)
            AudioSource.PlayClipAtPoint(reloadClip, transform.position);

        // 等待换弹时间（期间 Update 会忽略开枪输入，不会触发动画）
        yield return new WaitForSeconds(reloadDuration);

        // 从备用弹药池补满弹夹
        inventory.CompleteReload();

        isReloading = false;
    }

    private void Fire(GunData gunData)
    {

        // 近战武器（小刀等）
        if (gunData.range <= 1f)
        {
            MeleeAttack();
            return;
        }

        Vector2 dir = playerMovement != null
            ? playerMovement.GetFacingDirection()
            : Vector2.down;

        Vector2 origin = firePoint != null
            ? (Vector2)firePoint.position
            : (Vector2)transform.position + dir * 0.5f;

        Debug.DrawRay(origin, dir * gunData.range, Color.red, 0.3f);

        RaycastHit2D hit = Physics2D.Raycast(origin, dir, gunData.range, shootableLayers);
        if (hit.collider != null)
        {
            HealthSystem health = hit.collider.GetComponent<HealthSystem>();
            if (health != null && !health.isDead)
            {
                health.TakeDamage(gunData.damage);
                return;
            }
        }

        HealthSystem closest = null;
        float minDist = gunData.range;
        foreach (HealthSystem hs in FindObjectsOfType<HealthSystem>())
        {
            if (hs.isDead || hs.gameObject == gameObject) continue;
            Vector2 toTarget = (Vector2)hs.transform.position - origin;
            float dist = toTarget.magnitude;
            if (dist > gunData.range) continue;
            if (Vector2.Dot(dir, toTarget.normalized) > 0.85f && dist < minDist)
            {
                minDist = dist;
                closest = hs;
            }
        }
        if (closest != null)
        {
            closest.TakeDamage(gunData.damage);
            Debug.Log("[射击] 命中: " + closest.name);
        }
    }

    private void MeleeAttack()
    {
        if (playerMovement != null)
            playerMovement.PlaySlash();

        Vector2 dir = playerMovement != null
            ? playerMovement.GetFacingDirection()
            : Vector2.down;

        Vector2 origin = (Vector2)transform.position + dir * 0.8f;
        Collider2D[] hits = Physics2D.OverlapBoxAll(origin, new Vector2(1.2f, 1.2f), 0f);

        foreach (Collider2D hit in hits)
        {
            if (hit.gameObject == gameObject) continue;
            DestructibleObstacle obstacle = hit.GetComponentInParent<DestructibleObstacle>();
            if (obstacle != null)
            {
                obstacle.TakeDamage(1);
                return;
            }
            HealthSystem health = hit.GetComponentInParent<HealthSystem>();
            if (health != null)
            {
                health.TakeDamage(1);
                return;
            }
        }
    }
}
