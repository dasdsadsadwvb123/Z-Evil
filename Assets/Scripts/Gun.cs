using UnityEngine;
using System.Collections;
using System.Collections.Generic;

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
    private AudioSource audioSource;   // 开火音效用（没有就自动补一个）

    /// <summary> 开火/挥刀瞬间广播（每次扣扳机一次；远程和近战都触发）。GunVisual 订阅它做亮枪/亮刀 </summary>
    public event System.Action OnFired;

    /// <summary> 当前是否在换弹中（换弹期间禁止开枪，防止触发开枪动画） </summary>
    public bool IsReloading { get { return isReloading; } }
    private bool isReloading = false;

    private void Start()
    {
        inventory = GetComponent<Inventory>();
        playerMovement = GetComponent<PixelGridMovement>();
        audioSource = GetComponent<AudioSource>();
        if (audioSource == null)
            audioSource = gameObject.AddComponent<AudioSource>(); // 玩家身上没有就自动补一个
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
                DryFire(gunData);
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

    /// <summary>
    /// 弹尽时触发：空仓提示 + 打空"咔嗒"音效（dryFireClip 没配就只有日志，静音不报错）。
    /// 正常音量播：空仓是玩家贴脸按键的行为，玩家侧枪声一贯全音量（距离缩放是 Boss 电锯哥那边的机制）。
    /// </summary>
    private void DryFire(GunData gunData)
    {
        if (gunData.dryFireClip != null && audioSource != null)
            audioSource.PlayOneShot(gunData.dryFireClip); // 每把枪各自的"咔嗒"（GunData 里拖素材）
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
            OnFired?.Invoke(); // 广播"挥刀了"（GunVisual 亮刀用；近战不亮枪口闪光，由 GunVisual 按 range 区分）
            return;
        }

        Vector2 dir = playerMovement != null
            ? playerMovement.GetFacingDirection()
            : Vector2.down;

        Vector2 origin = firePoint != null
            ? (Vector2)firePoint.position
            : (Vector2)transform.position + dir * 0.5f;

        // 开火音效：每次扣扳机播一声（不是每发弹丸一声），fireClip 没拖就静音
        // 近战小刀在上面 range<=1 分支已经 return 了，走不到这里，天然没有音效
        if (gunData.fireClip != null && audioSource != null)
            audioSource.PlayOneShot(gunData.fireClip);

        // ===== 散射改造（散弹枪用）：pelletCount=1 时行为和改造前完全一样 =====
        int pellets = Mathf.Max(1, gunData.pelletCount);
        if (pellets == 1)
        {
            // 手枪/沙鹰：单发直射（原逻辑，一行没变）
            FireOnePellet(origin, dir, gunData);
            OnFired?.Invoke(); // 广播开火（GunVisual 枪口闪光用）
            return;
        }

        // 散弹枪：多发弹丸以朝向为中心，均匀铺满 spreadAngle 总角度
        float spread = Mathf.Max(0f, gunData.spreadAngle);
        float step = spread > 0f ? spread / (pellets - 1) : 0f;
        float startAngle = -spread * 0.5f;
        for (int i = 0; i < pellets; i++)
        {
            Vector2 pelletDir = RotateVector(dir, startAngle + step * i);
            FireOnePellet(origin, pelletDir, gunData);
        }

        OnFired?.Invoke(); // 广播开火（GunVisual 枪口闪光用）
    }

    /// <summary> 把方向向量旋转指定角度（散射用），单位：度 </summary>
    private static Vector2 RotateVector(Vector2 v, float angleDeg)
    {
        float rad = angleDeg * Mathf.Deg2Rad;
        float cos = Mathf.Cos(rad);
        float sin = Mathf.Sin(rad);
        return new Vector2(v.x * cos - v.y * sin, v.x * sin + v.y * cos);
    }

    /// <summary>
    /// 单发弹丸的命中判定（射线 + 扇形兜底，穿透可配）。
    /// 穿透规则（GunData 可调）：penetrate 总开关 + penetrateLimit（额外穿透数，-1 = 无限）
    /// + penetrateDamageFalloff（每穿一个伤害乘一次，1 = 不衰减）。
    /// 手枪（penetrate=false）= 永远只打 1 个目标，和最初版本行为一致。
    /// </summary>
    private void FireOnePellet(Vector2 origin, Vector2 dir, GunData gunData)
    {
        Debug.DrawRay(origin, dir * gunData.range, Color.red, 0.3f);

        // 有效穿透上限：总开关没勾 = 0（只打第 1 个目标）；-1 = 无限
        int limit = gunData.penetrate ? gunData.penetrateLimit : 0;
        List<HealthSystem> targets = CollectTargetsAlongRay(origin, dir, gunData, limit);
        if (targets.Count == 0) return; // 谁都没打中，和原来一样

        // 逐个结算：第 1 个全额伤害，之后每穿一个乘一次 falloff（向上取整、最低 1，防 0 伤）
        for (int i = 0; i < targets.Count; i++)
        {
            int dmg = Mathf.Max(1, Mathf.CeilToInt(gunData.damage * Mathf.Pow(gunData.penetrateDamageFalloff, i)));
            targets[i].TakeDamage(dmg);
            if (i > 0)
                Debug.Log("[射击] 穿透命中: " + targets[i].name + "（第 " + (i + 1) + " 目标，伤害 " + dmg + "）");
        }
    }

    /// <summary>
    /// 沿一条弹丸的射线收集目标（按距离从近到远，数量上限由穿透配置决定）：
    /// 1. 射线精确路径（RaycastAll）：命中活体 HealthSystem 就收集；命中墙（无血量碰撞体）
    ///    立即截停——穿透不能穿墙；死人不挡弹不收集；无限穿透 = 一直收到射程尽头或撞墙为止；
    /// 2. 射线一个都没打中 → 走"扇形兜底"（点积 > 0.85 的扇形内目标），同样按距离排序、
    ///    受同一个上限约束——穿透目标必然也在扇形范围内。
    /// </summary>
    private List<HealthSystem> CollectTargetsAlongRay(Vector2 origin, Vector2 dir, GunData gunData, int limit)
    {
        // 要收集的目标总数上限：limit 是"额外穿透数"，+1 才是含第一个目标的总数；-1（无限）用 int.MaxValue
        int maxTargets = limit < 0 ? int.MaxValue : limit + 1;
        List<HealthSystem> result = new List<HealthSystem>();

        // 1. 射线路径（墙挡穿透）
        RaycastHit2D[] hits = Physics2D.RaycastAll(origin, dir, gunData.range, shootableLayers);
        System.Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));
        foreach (RaycastHit2D h in hits)
        {
            HealthSystem hs = h.collider.GetComponent<HealthSystem>();
            if (hs != null)
            {
                if (!hs.isDead)
                {
                    result.Add(hs);
                    if (result.Count >= maxTargets) return result; // 穿透上限（无限时永远到不了）
                }
                // 死人：不挡弹不收集，弹丸继续飞
            }
            else
            {
                break; // 墙/障碍：弹丸到此为止，穿透被挡
            }
        }
        if (result.Count > 0) return result;

        // 2. 兜底：扇形内目标按距离排序（规则和最初版兜底一致，数量受同一个上限约束）
        List<HealthSystem> fan = new List<HealthSystem>();
        foreach (HealthSystem hs in FindObjectsOfType<HealthSystem>())
        {
            if (hs.isDead || hs.gameObject == gameObject) continue;
            Vector2 toTarget = (Vector2)hs.transform.position - origin;
            float dist = toTarget.magnitude;
            if (dist > gunData.range) continue;
            if (Vector2.Dot(dir, toTarget.normalized) > 0.85f)
                fan.Add(hs);
        }
        fan.Sort((a, b) =>
            ((Vector2)a.transform.position - origin).sqrMagnitude
            .CompareTo(((Vector2)b.transform.position - origin).sqrMagnitude));
        for (int i = 0; i < fan.Count && result.Count < maxTargets; i++)
            result.Add(fan[i]);
        return result;
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
