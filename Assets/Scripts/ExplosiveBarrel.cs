using UnityEngine;
using UnityEngine.Events;
using System.Collections;

/// <summary>
/// 可爆炸罐子（决战地图机关，挂在罐子物体上）：
/// 公开 Explode()——播放爆闪演出 + 音效（距离听声惯例）+ 半径内暴君受击 + 僵直 + 半径内玩家受伤（可选开关）；
/// 爆后换残骸图（不拖 = 直接消失）、碰撞体改 Trigger 不挡路。
/// 加分项：勾 shootable 时自动补 HealthSystem（没挂才补，1 血）——枪/刀打死罐子 = 引爆（走同一 Explode 入口）。
/// 引爆源：爆破器（ExplosionDetonator.TriggerExplosion，每罐随机延迟陆续炸）、枪打、或小泽自己的 UnityEvent 接 Explode/ExplodeDelayed。
/// </summary>
public class ExplosiveBarrel : MonoBehaviour
{
    [Header("爆炸参数（小泽自己调数值）")]
    [Tooltip("爆炸半径（世界单位，1 = 1 格）")]
    public float explosionRadius = 2.5f;
    [Tooltip("对暴君造成的伤害")]
    public int tyrantDamage = 3;
    [Tooltip("对暴君造成的僵直时长（秒）：炸到 = 暴君停止一切行动这么久")]
    public float tyrantStunDuration = 2f;
    [Tooltip("爆炸是否也伤玩家（离得太近自己也被波及；决战房建议开，逼玩家卡距离）")]
    public bool hurtPlayer = true;
    [Tooltip("对玩家造成的伤害")]
    public int playerDamage = 2;

    [Header("演出")]
    [Tooltip("爆炸音效（距离听声惯例；不拖 = 静音）")]
    public AudioClip explosionClip;
    [Tooltip("爆后残骸图（不拖 = 罐子直接消失）")]
    public Sprite debrisSprite;
    [Tooltip("爆闪圆圈扩散时长（秒）")]
    public float blastFlashDuration = 0.3f;
    [Tooltip("引信闪烁：点燃后橙红↔原色交替，提示这罐要炸了（ExplodeDelayed 引信用）")]
    public bool fuseBlink = true;

    [Header("枪打引爆（加分项）")]
    [Tooltip("勾上 = 自动补 HealthSystem（没挂才补，1 血）——枪/刀打死罐子就引爆；不勾 = 只能靠爆破器/事件")]
    public bool shootable = true;

    /// <summary> 是否已经炸过（防二次引爆） </summary>
    public bool Exploded { get; private set; } = false;

    [Tooltip("爆炸瞬间广播（可接开门/剧情/成就）")]
    public UnityEvent onExplode;

    private bool fuseLit = false; // 引信已点燃（防重复排队引信）

    private void Start()
    {
        // 罐子必须是实体碰撞体（挡路 + 让子弹能打中）；没有就自动补
        Collider2D col = GetComponent<Collider2D>();
        if (col == null)
        {
            BoxCollider2D box = gameObject.AddComponent<BoxCollider2D>();
            box.size = new Vector2(0.9f, 0.9f);
        }

        // 枪打引爆：自动补 HealthSystem（只在小泽没手动挂时补，1 血 = 一枪爆）
        if (shootable && GetComponent<HealthSystem>() == null)
        {
            HealthSystem hs = gameObject.AddComponent<HealthSystem>();
            hs.maxHealth = 1;
            hs.currentHealth = 1;
            hs.destroyOnDeath = false; // 死亡由本脚本接管（Explode 换残骸）
            hs.deathTriggerName = "";
            hs.OnDeath += OnShotDead;
        }
    }

    private void OnDestroy()
    {
        HealthSystem hs = GetComponent<HealthSystem>();
        if (hs != null) hs.OnDeath -= OnShotDead;
    }

    /// <summary> 被枪打死后引爆（HealthSystem OnDeath 回调） </summary>
    private void OnShotDead()
    {
        Explode();
    }

    /// <summary>
    /// 延迟引爆：等 delay 秒再炸（引信期橙红闪烁警示，给玩家逃跑窗口）。
    /// 爆破器按每罐随机浮动延迟调这个；delay ≤ 0 = 立即炸。
    /// </summary>
    public void ExplodeDelayed(float delay)
    {
        if (Exploded || fuseLit) return; // 已炸/引信已点 → 忽略
        if (delay <= 0f)
        {
            Explode();
            return;
        }
        fuseLit = true;
        StartCoroutine(FuseRoutine(delay));
    }

    /// <summary> 引信：橙红↔原色交替闪烁警示 → 到点引爆 </summary>
    private IEnumerator FuseRoutine(float delay)
    {
        SpriteRenderer sr = GetComponent<SpriteRenderer>();
        Color origColor = sr != null ? sr.color : Color.white;

        float t = 0f;
        float blinkTimer = 0f;
        bool blinkOn = false;
        while (t < delay)
        {
            t += Time.deltaTime;
            blinkTimer += Time.deltaTime;
            if (fuseBlink && sr != null && blinkTimer >= 0.12f)
            {
                blinkTimer = 0f;
                blinkOn = !blinkOn;
                sr.color = blinkOn ? new Color(1f, 0.45f, 0.2f) : origColor; // 橙红警告闪
            }
            yield return null;
        }

        if (sr != null) sr.color = origColor; // 还原再炸（爆闪/残骸接管视觉）
        Explode();
    }

    /// <summary>
    /// 引爆！播放演出 + 半径内暴君受击僵直 + 半径内玩家受伤（可选）。
    /// 爆破器/枪打/UnityEvent 都走这一个入口；炸过就忽略（防二次引爆）。
    /// </summary>
    public void Explode()
    {
        if (Exploded) return;
        Exploded = true;

        // 音效（距离听声惯例：远处听不见爆炸声）+ 事件
        AudibleAudio.PlayAt(explosionClip, transform.position);
        onExplode?.Invoke();

        // 半径内暴君：受击 + 僵直
        foreach (TyrantAI tyrant in FindObjectsOfType<TyrantAI>())
        {
            if (Vector2.Distance(tyrant.transform.position, transform.position) <= explosionRadius)
            {
                tyrant.TakeDamage(tyrantDamage);
                tyrant.Stun(tyrantStunDuration);
            }
        }

        // 半径内玩家：受伤（可选开关）
        if (hurtPlayer)
        {
            GameObject playerObj = GameObject.FindGameObjectWithTag("Player");
            if (playerObj != null && Vector2.Distance(playerObj.transform.position, transform.position) <= explosionRadius)
            {
                HealthSystem playerHealth = playerObj.GetComponent<HealthSystem>();
                if (playerHealth != null && !playerHealth.isDead)
                    playerHealth.TakeDamage(playerDamage);
            }
        }

        // 演出：爆闪圆圈扩散渐隐
        StartCoroutine(BlastFlashRoutine());

        // 残骸 or 消失：换残骸图 + 碰撞体改 Trigger（碎罐不挡路，同碎箱惯例）
        SpriteRenderer sr = GetComponent<SpriteRenderer>();
        if (debrisSprite != null && sr != null)
        {
            sr.sprite = debrisSprite;
            Collider2D col = GetComponent<Collider2D>();
            if (col != null) col.isTrigger = true;
        }
        else
        {
            Destroy(gameObject); // 没配残骸图 → 整个消失
        }

        Debug.Log("[爆炸罐] " + name + " 引爆！半径 " + explosionRadius, gameObject);
    }

    /// <summary> 爆闪：代码生成圆形贴图，从罐子中心扩散 + 渐隐，播完自毁 </summary>
    private IEnumerator BlastFlashRoutine()
    {
        // 生成白色圆形贴图（逐像素画圆，除颤仪电极同款套路）
        int size = 64;
        Texture2D tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float nx = x / (float)(size - 1) * 2f - 1f;
                float ny = y / (float)(size - 1) * 2f - 1f;
                float dist = Mathf.Sqrt(nx * nx + ny * ny);
                float alpha = Mathf.Clamp01((1f - dist) / 0.15f);
                tex.SetPixel(x, y, new Color(1f, 1f, 1f, alpha));
            }
        }
        tex.Apply();
        Sprite circle = Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f));

        GameObject flash = new GameObject("BlastFlash");
        flash.transform.position = transform.position;
        SpriteRenderer flashSr = flash.AddComponent<SpriteRenderer>();
        flashSr.sprite = circle;
        flashSr.color = new Color(1f, 0.7f, 0.3f, 0.9f); // 橙黄爆闪
        flashSr.sortingOrder = 50; // 压在场景之上

        float t = 0f;
        while (t < blastFlashDuration)
        {
            t += Time.deltaTime;
            float k = Mathf.Clamp01(t / blastFlashDuration);
            float diameter = Mathf.Lerp(0.5f, explosionRadius * 2f, k); // 从小圆扩到爆炸半径
            flashSr.transform.localScale = Vector3.one * diameter;
            Color c = flashSr.color;
            c.a = 0.9f * (1f - k); // 渐隐
            flashSr.color = c;
            yield return null;
        }
        Destroy(flash);
        Destroy(tex); // 释放临时贴图
    }
}
