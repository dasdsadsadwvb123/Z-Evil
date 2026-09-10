using UnityEngine;
using UnityEngine.Rendering.Universal;

/// <summary>
/// 蜡烛光圈（挂在玩家身上）：**只在黑屋区域自动亮起**——
/// 背包里有指定 itemID（默认 "Candle"）的蜡烛 且 玩家在 DarkRoomZone 内 → 正常光圈；
/// 没拿蜡烛但在黑屋里 → 极小一圈微光（防寸步难行）；在明亮区域 → 光完全熄灭（防亮区过曝发白）。
/// 三种状态之间平滑过渡（Lerp），蜡烛是纯背包物品被动感应，不用装备、不占拿枪位置。
/// 烛光摇曳：亮度和半径正弦+噪声微抖（只在黑屋光亮起时摇），Inspector 可开关/调幅度。
/// 光源自动生成：不用手动加 Light 2D，本脚本在玩家脚下自动生成一个 Point Light 2D 子物体。
/// 前提：场景里要有 DarknessController + DarkRoomZone（黑暗系统是烛光亮起的前提）。
/// </summary>
public class CandleLight : MonoBehaviour
{
    [Header("蜡烛拾取联动")]
    [Tooltip("蜡烛的 itemID：要和场景里蜡烛 PickupItem 的 itemID 一致（背包里有它 = 正常光圈）")]
    public string candleItemID = "Candle";

    [Header("光圈设置（拿到蜡烛后）")]
    [Tooltip("烛光半径（世界单位/格）：小泽自己调大小，3 = 周围 3 格可见")]
    public float lightRadius = 3f;
    [Tooltip("烛光亮度")]
    [Range(0f, 2f)]
    public float lightIntensity = 1f;
    [Tooltip("烛光颜色（暖黄 = 蜡烛味）")]
    public Color lightColor = new Color(1f, 0.85f, 0.55f);

    [Header("没拿蜡烛时")]
    [Tooltip("没蜡烛的光圈半径（很小的一圈，够看清脚边）")]
    public float noCandleRadius = 0.7f;
    [Tooltip("没蜡烛的光圈亮度（调 0 = 完全看不见，不推荐）")]
    [Range(0f, 2f)]
    public float noCandleIntensity = 0.45f;

    [Header("烛光摇曳（氛围）")]
    [Tooltip("开关：关掉 = 恒定亮度不抖")]
    public bool candleFlicker = true;
    [Tooltip("摇曳幅度（0.08 = 亮度上下抖 8%）")]
    [Range(0f, 0.5f)]
    public float flickerStrength = 0.08f;
    [Tooltip("摇曳速度（越大抖得越快）")]
    public float flickerSpeed = 8f;

    [Header("亮灭过渡")]
    [Tooltip("黑屋亮起/出黑屋熄灭的过渡速度：越大切换越快（4 = 约四分之一秒渐变）")]
    public float lightTransitionSpeed = 4f;

    private Inventory inventory;
    private DarknessController darkController; // 黑暗总管（查"玩家是否在黑屋里"用）
    private Light2D candle;               // 自动生成的蜡烛点光源
    private bool hasCandleLast = true;    // 上一帧有没有蜡烛（捡到蜡烛瞬间打日志用）
    private bool wasLightOn = false;      // 上一帧光是否亮着（进黑屋亮起瞬间打日志用）
    private float noiseSeed;              // 摇曳噪声的随机种子（每只蜡烛抖得不一样）

    private void Start()
    {
        inventory = GetComponent<Inventory>();
        if (inventory == null)
            Debug.LogWarning("[蜡烛] 玩家身上没有 Inventory 组件，无法判断有没有捡蜡烛 → 按没蜡烛处理", gameObject);
        hasCandleLast = HasCandle();

        // 找黑暗总管（查"玩家是否在黑屋"）：没找到就当"永远在黑屋"兜底，
        // 保证没搭黑暗系统的旧场景里蜡烛光不至于永远熄灭
        darkController = DarknessController.FindInScene();
        if (darkController == null)
            Debug.LogWarning("[蜡烛] 场景里没有 DarknessController → 蜡烛光会常亮（想自动感应请先搭黑暗系统）", gameObject);
        wasLightOn = InDarkRoom() && HasCandle();

        // 自动生成蜡烛点光源（挂在玩家下面，跟着玩家走）
        GameObject go = new GameObject("CandleLight (自动生成)");
        go.transform.SetParent(transform, false);
        candle = go.AddComponent<Light2D>();
        candle.lightType = Light2D.LightType.Point;
        candle.color = lightColor;
        candle.intensity = 0f; // 出生先灭着，进黑屋才自动亮起（防止开局在亮区发白一帧）
        candle.pointLightOuterRadius = lightRadius;

        noiseSeed = Random.Range(0f, 100f);
    }

    private void Update()
    {
        if (candle == null) return;

        // ---- 1. 感应状态：在不在黑屋 / 有没有蜡烛 ----
        bool hasCandle = HasCandle();
        bool inDark = InDarkRoom();
        bool lightOn = inDark; // 光只在黑屋里存在（有蜡烛=正常光圈，没蜡烛=微光）；亮区完全熄灭防过曝

        if (hasCandle && !hasCandleLast)
            Debug.Log("[蜡烛] 拾取成功（不用装备，进黑屋自动亮）");
        if (lightOn && !wasLightOn)
            Debug.Log("[蜡烛] 进黑屋，烛光亮起！");
        if (!lightOn && wasLightOn)
            Debug.Log("[蜡烛] 出黑屋，烛光熄灭");
        hasCandleLast = hasCandle;
        wasLightOn = lightOn;

        // ---- 2. 目标亮度/半径（三态：亮区熄灭 / 黑屋+蜡烛 / 黑屋没蜡烛微光） ----
        float targetRadius;
        float targetIntensity;
        if (!inDark)
        {
            targetRadius = lightRadius;    // 半径保持，亮度归 0 = 光熄灭
            targetIntensity = 0f;
        }
        else if (hasCandle)
        {
            targetRadius = lightRadius;
            targetIntensity = lightIntensity;
        }
        else
        {
            targetRadius = noCandleRadius; // 黑屋里没蜡烛：留极小微光防寸步难行
            targetIntensity = noCandleIntensity;
        }

        // ---- 3. 烛光摇曳：只在黑屋光亮起时摇（亮区无光可摇） ----
        if (candleFlicker && hasCandle && inDark)
        {
            float t = Time.time * flickerSpeed + noiseSeed;
            float wave = Mathf.Sin(t) * 0.6f + (Mathf.PerlinNoise(t * 0.7f, noiseSeed) - 0.5f) * 2f;
            float flicker = 1f + wave * flickerStrength;
            targetIntensity *= flicker;
            targetRadius *= 1f + wave * flickerStrength * 0.5f; // 半径抖动减半，避免边缘晃太凶
        }

        // ---- 4. 平滑过渡：亮起/熄灭/微光互切都是渐变，不会啪一下闪 ----
        candle.pointLightOuterRadius = Mathf.Lerp(candle.pointLightOuterRadius, targetRadius, lightTransitionSpeed * Time.deltaTime);
        candle.intensity = Mathf.Lerp(candle.intensity, targetIntensity, lightTransitionSpeed * Time.deltaTime);
        candle.color = lightColor;
    }

    /// <summary> 背包里有没有蜡烛（按 itemID 查，和 PickupItem 的 itemID 对上） </summary>
    private bool HasCandle()
    {
        if (inventory == null || string.IsNullOrEmpty(candleItemID)) return false;
        return inventory.HasItem(candleItemID);
    }

    /// <summary>
    /// 玩家现在在不在黑屋里（问 DarknessController）。
    /// 场景没搭黑暗系统时返回 true 兜底：蜡烛光常亮，保持旧场景行为。
    /// </summary>
    private bool InDarkRoom()
    {
        if (darkController == null) return true;
        return darkController.IsDark();
    }
}
