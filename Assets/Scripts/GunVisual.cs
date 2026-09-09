using UnityEngine;
using System.Collections;

/// <summary>
/// 分层合成射击/挥刀表现：自动生成一个武器贴图子物体，**默认隐藏**——角色平时是纯跑动/站立动画；
/// 开火/挥刀瞬间才亮出（按朝向摆位），远程枪额外加枪口闪光，gunShowDuration 秒后藏回去，连发不断重新计时。
/// 装备哪把枪/刀显示哪张图（GunData 资产配对；近战小刀也亮刀，但不亮枪口闪光）；没配条目的武器照旧完全不显示。
/// 挂到玩家（Player）物体上（和 Gun / Inventory 同物体）。
/// 三张枪图/刀图都要"枪头/刀尖朝右"画；朝左自动 flipX；朝上时压到角色身后（经典背持效果）。
/// 不碰 Animator：本脚本只动子物体的位置/翻转/排序，角色的动画完全不受影响。
/// </summary>

/// <summary> 枪图配置条目（struct：按项目规范不用嵌套类） </summary>
[System.Serializable]
public struct GunSpriteEntry
{
    [Tooltip("枪的 GunData 资产（装备这把枪时显示对应贴图）")]
    public GunData gunData;
    [Tooltip("这把枪/刀的贴图（默认枪头/刀尖朝右画）")]
    public Sprite gunSprite;
    [Tooltip("基准偏转角（度）：图不朝右时用——朝上画的填 -90（不对就试 90），朝右画的填 0")]
    public float rotationOffset; // struct 字段默认就是 0（= 不偏转），不用写初始化
    [Tooltip("这张图单独缩放：1 = 不变；图太小填 1.5/2，太大填 0.7")]
    public float scale; // struct 字段默认就是 1（= 不变），不用写初始化；最终缩放 = 全局 Gun Scale × 这个值
}

public class GunVisual : MonoBehaviour
{
    [Header("武器图配置（装备哪把枪/刀显示哪张图；没配条目的武器 → 不显示）")]
    public GunSpriteEntry[] entries;

    [Header("4 方向摆位（Inspector 可调；默认值适合 32px 左右的像素枪/刀图）")]
    [Tooltip("朝右：枪在角色手侧")]
    public Vector2 offsetRight = new Vector2(0.35f, 0.1f);
    [Tooltip("朝左：自动水平翻转")]
    public Vector2 offsetLeft = new Vector2(-0.35f, 0.1f);
    [Tooltip("朝上：枪举过头，压到角色身后")]
    public Vector2 offsetUp = new Vector2(0.1f, 0.45f);
    [Tooltip("朝下：枪口斜向下")]
    public Vector2 offsetDown = new Vector2(0.1f, 0.05f);
    [Tooltip("枪贴图整体缩放（图太大就调小）")]
    public float gunScale = 1f;

    [Header("排序（相对角色 SpriteRenderer 的偏移）")]
    [Tooltip("右/左/下 = 角色前面（+1 盖在角色上）")]
    public int frontOrderOffset = 1;
    [Tooltip("朝上 = 角色身后（-1 垫在角色下，背枪效果）")]
    public int backOrderOffset = -1;

    [Header("枪口闪光")]
    [Tooltip("闪光贴图（不拖就用代码生成的黄色菱形，零素材也能跑）")]
    public Sprite flashSprite;
    [Tooltip("闪光淡出时长（秒）")]
    public float flashDuration = 0.07f;
    [Tooltip("闪光大小（世界单位）")]
    public float flashSize = 0.5f;
    [Tooltip("闪光颜色")]
    public Color flashColor = new Color(1f, 0.9f, 0.3f);

    [Header("枪显示时长")]
    [Tooltip("开火后枪贴图显示多久（秒）然后藏起来；连发会不断重新计时")]
    public float gunShowDuration = 0.2f;

    private Gun gun;                    // 玩家身上的 Gun（拿 firePoint 和 OnFired 事件）
    private Inventory inventory;
    private PixelGridMovement movement;
    private SpriteRenderer playerSr;

    private GameObject gunGO;           // 枪贴图子物体
    private SpriteRenderer gunSr;
    private GameObject flashGO;         // 枪口闪光物体
    private SpriteRenderer flashSr;
    private Coroutine flashRoutine;
    private Coroutine hideRoutine;      // 亮枪计时（到点藏枪）
    private bool gunVisible = false;    // 枪当前是否亮着
    private float currentRotationOffset = 0f; // 当前武器条目的基准偏转角（FindGunSprite 里更新）
    private float currentScale = 1f;          // 当前武器条目的独立缩放（FindGunSprite 里更新，未命中归 1）

    private void Start()
    {
        gun = GetComponent<Gun>();
        inventory = GetComponent<Inventory>();
        movement = GetComponent<PixelGridMovement>();
        playerSr = GetComponent<SpriteRenderer>();

        if (gun == null)
            Debug.LogWarning("[枪表现] 玩家身上没有 Gun 组件，枪口闪光不会工作", gameObject);

        if (gun != null)
            gun.OnFired += HandleFired; // 订阅开火广播（禁止轮询按键）：亮枪 + 枪口闪光

        CreateGunSprite();
        CreateFlashSprite();
    }

    private void OnDestroy()
    {
        // 退订事件 + 清理自己生成的物体
        if (gun != null) gun.OnFired -= HandleFired;
        if (gunGO != null) Destroy(gunGO);
        if (flashGO != null) Destroy(flashGO);
    }

    private void Update()
    {
        // 枪默认隐藏；只在"亮枪窗口"内每帧重摆位 → 开枪瞬间转向也跟手
        if (!gunVisible) return;
        ApplyGunPose(gunSr.sprite);
    }

    // ======== 枪贴图：开火亮枪 / 计时隐藏 / 摆位 ========

    /// <summary> 开火/挥刀广播回调：亮枪/亮刀 + 重新计时；枪口闪光只有远程才出 </summary>
    private void HandleFired()
    {
        // 没配条目的武器：照旧完全不显示
        GunData equipped = inventory != null ? inventory.GetEquippedGun() : null;
        Sprite sprite = FindGunSprite(equipped);
        if (sprite == null || equipped == null)
        {
            HideGun();
            return;
        }

        // 亮枪/亮刀 + 按当前朝向摆位（近战刀图也要刀尖朝右画，朝左自动翻转）
        gunVisible = true;
        ApplyGunPose(sprite);

        // 连发/连砍：每次开火重新计时（贴图持续可见，符合连打节奏）
        if (hideRoutine != null) StopCoroutine(hideRoutine);
        hideRoutine = StartCoroutine(HideGunRoutine());

        // 枪口闪光只有远程枪（range>1）才出——刀是自己挥的，给刀加火光很怪
        if (equipped.range > 1f) ShowMuzzleFlash();
    }

    /// <summary> 到点藏枪：回到"纯人物动画"状态 </summary>
    private IEnumerator HideGunRoutine()
    {
        yield return new WaitForSeconds(gunShowDuration);
        HideGun();
    }

    private void HideGun()
    {
        gunVisible = false;
        if (hideRoutine != null)
        {
            StopCoroutine(hideRoutine);
            hideRoutine = null;
        }
        if (gunGO != null) gunGO.SetActive(false);
    }

    /// <summary> 按当前朝向设置枪贴图的位置/翻转/旋转/排序（4 方向摆位规则不变） </summary>
    private void ApplyGunPose(Sprite sprite)
    {
        if (movement == null || gunGO == null) return;

        // ⚠️ 关键修复：亮枪必须真的激活物体！
        // 上一版重构时漏了这行——子物体创建时是 SetActive(false)，HideGun 又会关掉它，
        // 导致全链路逻辑跑通但枪从未显示过（这就是"开枪看不见枪"的根因）
        gunGO.SetActive(true);

        gunSr.sprite = sprite;
        gunGO.transform.localScale = new Vector3(gunScale * currentScale, gunScale * currentScale, 1f); // 全局缩放 × 条目独立缩放

        // 按朝向摆位
        Vector2 face = movement.GetFacingDirection();
        Vector3 basePos = Vector3.zero; // localPosition（相对玩家）
        int order;
        int playerOrder = playerSr != null ? playerSr.sortingOrder : 0;

        if (face == Vector2.right)
        {
            basePos = offsetRight;
            gunSr.flipX = false;            // 图本来就是朝右画的
            gunGO.transform.localRotation = Quaternion.Euler(0f, 0f, currentRotationOffset); // 原角度 0 + 条目偏转
            order = playerOrder + frontOrderOffset;   // 角色前面
        }
        else if (face == Vector2.left)
        {
            basePos = offsetLeft;
            gunSr.flipX = true;             // 朝左 = 水平翻转
            // ⚠️ 翻转侧 offset 必须取反：水平镜像会抵消一个旋转符号，不取反时刀会插到自己身上。
            // 数学验证（刀图朝上、offset=-90）：朝左 = 0 + (90) = 刀尖朝左 ✓
            // （朝右不翻转：0 + (-90) = 刀尖朝右 ✓；朝上 90 + (-90) = 0 ✓；朝下 -90 + (-90) = -180 ✓，四方向全对）
            gunGO.transform.localRotation = Quaternion.Euler(0f, 0f, -currentRotationOffset); // 原角度 0 + 取反后的条目偏转
            order = playerOrder + frontOrderOffset;   // 角色前面
        }
        else if (face == Vector2.up)
        {
            basePos = offsetUp;
            gunSr.flipX = false;
            gunGO.transform.localRotation = Quaternion.Euler(0f, 0f, 90f + currentRotationOffset); // 枪口朝上 + 条目偏转
            order = playerOrder + backOrderOffset;    // 角色身后（背枪效果）
        }
        else // 朝下
        {
            basePos = offsetDown;
            gunSr.flipX = false;
            gunGO.transform.localRotation = Quaternion.Euler(0f, 0f, -90f + currentRotationOffset); // 枪口朝下 + 条目偏转
            order = playerOrder + frontOrderOffset;   // 角色前面
        }

        gunGO.transform.localPosition = basePos;
        gunSr.sortingOrder = order;
        if (playerSr != null)
            gunSr.sortingLayerID = playerSr.sortingLayerID; // 每帧同步 Sorting Layer，防角色换层后枪掉队
    }

    /// <summary>
    /// 按装备的 GunData 资产找配对的枪图（资产引用相等才算配对），没配返回 null；
    /// 找到时顺便把该条目的 rotationOffset 和 scale 存进 currentRotationOffset / currentScale，
    /// ApplyGunPose 用它们算最终角度和缩放。未命中时两个都归默认（0 / 1），防换武器串台。
    /// </summary>
    private Sprite FindGunSprite(GunData equipped)
    {
        currentRotationOffset = 0f; // 没配/换条目都先归零，防上把武器的偏转角串台
        currentScale = 1f;          // 同理，缩放也先归 1（= 不变）
        if (equipped == null || entries == null) return null;
        foreach (GunSpriteEntry e in entries)
        {
            if (e.gunData != null && e.gunData == equipped)
            {
                currentRotationOffset = e.rotationOffset;
                currentScale = e.scale;
                if (currentScale <= 0f) currentScale = 1f; // 老数据没填过 Scale（反序列化成 0）→ 兜底回 1 = 不变，防止枪缩到看不见
                return e.gunSprite;
            }
        }
        return null;
    }

    // ======== 枪口闪光 ========

    /// <summary> 开火广播回调：在出膛点显示闪光，快速淡出 </summary>
    private void ShowMuzzleFlash()
    {
        if (flashGO == null) return;

        if (flashRoutine != null)
            StopCoroutine(flashRoutine); // 上一道闪光还没淡完就再开枪：打断重来

        // 优先用 firePoint 的出膛点；没配 firePoint 就按"角色中心 + 朝向 0.6 格"兜底
        Vector3 pos;
        if (gun != null && gun.firePoint != null)
            pos = gun.firePoint.position;
        else if (movement != null)
            pos = transform.position + (Vector3)(movement.GetFacingDirection() * 0.6f);
        else
            pos = transform.position;

        flashGO.transform.position = pos;
        SetFlashAlpha(1f);
        flashGO.SetActive(true);
        flashRoutine = StartCoroutine(FlashFadeRoutine());
    }

    /// <summary> 闪光淡出：0.05~0.08 秒内透明度从 1 掉到 0，然后隐藏 </summary>
    private IEnumerator FlashFadeRoutine()
    {
        float t = 0f;
        while (t < flashDuration)
        {
            t += Time.deltaTime;
            SetFlashAlpha(Mathf.Clamp01(1f - t / flashDuration));
            yield return null;
        }
        flashGO.SetActive(false);
        flashRoutine = null;
    }

    private void SetFlashAlpha(float alpha)
    {
        Color c = flashSr.color;
        c.a = alpha;
        flashSr.color = c;
    }

    // ======== 物体生成 ========

    /// <summary> 生成枪贴图子物体（挂在玩家下，跟着玩家走） </summary>
    private void CreateGunSprite()
    {
        gunGO = new GameObject("GunSprite");
        gunGO.transform.SetParent(transform, false);
        gunSr = gunGO.AddComponent<SpriteRenderer>();
        if (playerSr != null)
            gunSr.sortingLayerID = playerSr.sortingLayerID; // ⚠️ 必须跟角色同一 Sorting Layer：只跟 order 不跟 layer，枪会沉到地图瓦片下面
        gunGO.SetActive(false); // 初始隐藏（开枪时 HandleFired 里激活）
    }

    /// <summary> 生成枪口闪光物体（世界物体，不挂玩家下，方便摆到 firePoint 位置） </summary>
    private void CreateFlashSprite()
    {
        flashGO = new GameObject("MuzzleFlash");
        flashSr = flashGO.AddComponent<SpriteRenderer>();
        flashSr.sprite = flashSprite != null ? flashSprite : CreateDefaultFlashSprite();
        flashSr.color = flashColor;
        if (playerSr != null)
            flashSr.sortingLayerID = playerSr.sortingLayerID; // 闪光也跟角色同一 Sorting Layer
        flashSr.sortingOrder = (playerSr != null ? playerSr.sortingOrder : 0) + frontOrderOffset + 2; // 盖在枪上面
        flashGO.transform.localScale = Vector3.one * flashSize;
        flashGO.SetActive(false); // 初始隐藏
    }

    /// <summary> 代码生成黄色菱形闪光贴图（flashSprite 没拖时的兜底，零素材可跑） </summary>
    private Sprite CreateDefaultFlashSprite()
    {
        int size = 32;
        Texture2D tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float nx = x / (float)(size - 1) * 2f - 1f;
                float ny = y / (float)(size - 1) * 2f - 1f;
                // 菱形：|nx|+|ny| < 1 是内部，边缘快速淡出
                float alpha = Mathf.Clamp01((1f - (Mathf.Abs(nx) + Mathf.Abs(ny))) / 0.3f);
                tex.SetPixel(x, y, new Color(1f, 1f, 1f, alpha));
            }
        }
        tex.Apply();
        return Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f));
    }
}
