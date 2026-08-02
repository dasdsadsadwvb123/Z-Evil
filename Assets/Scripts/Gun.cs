using UnityEngine;

public class Gun : MonoBehaviour
{
    [Header("射击设置")]
    public KeyCode fireKey = KeyCode.J;
    public LayerMask shootableLayers = -1;
    public Transform firePoint;

    private Inventory inventory;
    private PixelGridMovement playerMovement;
    private float lastFireTime;

    private void Start()
    {
        inventory = GetComponent<Inventory>();
        playerMovement = GetComponent<PixelGridMovement>();
    }

    private void Update()
    {
        if (inventory == null) return;

        GunData gunData = inventory.GetEquippedGun();
        if (gunData == null) return;

        if (Input.GetKeyDown(fireKey) && Time.time >= lastFireTime + gunData.fireRate)
        {
           Fire(gunData);
            if (gunData.range > 1f) playerMovement?.PlayShoot();
           lastFireTime = Time.time;
        }
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
