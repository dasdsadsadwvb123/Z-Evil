using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 受伤红闪：玩家受击时，屏幕四周边缘泛起红色警示框（像"屏幕受伤了"）。
/// 挂到玩家（Player）物体上（和 HealthSystem 同物体）。
///
/// 原理：订阅 HealthSystem.OnDamaged 事件 → 受击瞬间把红色强度冲到峰值 → 每帧衰减淡出。
/// 边缘遮罩贴图是代码生成的（运行时逐像素画），不需要额外美术素材。
/// 纯代码实现，不碰 Animator。
/// </summary>
public class DamageFlash : MonoBehaviour
{
    [Header("受击红闪")]
    [Tooltip("受击瞬间边缘红色强度（0~1，越大越红）")]
    public float flashIntensity = 0.8f;

    [Tooltip("红色消退速度（越大消失越快）")]
    public float fadeSpeed = 2.5f;

    [Tooltip("红框颜色（想换中毒绿色之类的就改这里）")]
    public Color flashColor = new Color(1f, 0.15f, 0.1f);

    private HealthSystem health;
    private Image flashImage;
    private float currentAlpha = 0f; // 当前红色强度（受击时冲到峰值，然后慢慢归零）

    private void Start()
    {
        // 订阅受伤事件：HealthSystem.TakeDamage 成功时会广播 OnDamaged
        health = GetComponent<HealthSystem>();
        if (health != null)
            health.OnDamaged += OnDamaged;
        else
            Debug.LogWarning("[红闪] 玩家身上没有 HealthSystem，受击红闪不会工作！", gameObject);

        CreateFlashUI();
    }

    private void OnDestroy()
    {
        // 退订事件，防止内存泄漏
        if (health != null)
            health.OnDamaged -= OnDamaged;
    }

    /// <summary> 受伤回调：红色强度冲到峰值，接下来的 Update 会让它慢慢消退 </summary>
    private void OnDamaged()
    {
        currentAlpha = flashIntensity;
    }

    private void Update()
    {
        if (flashImage == null) return;

        if (currentAlpha > 0f)
        {
            // 红色每帧衰减 → 形成"闪一下然后淡出"的效果
            currentAlpha = Mathf.Max(0f, currentAlpha - fadeSpeed * Time.deltaTime);

            Color c = flashColor;
            c.a = currentAlpha;
            flashImage.color = c;
        }
    }

    // ======== UI 创建 ========

    private void CreateFlashUI()
    {
        // 1. 画布（盖在画面上层，但低于死亡界面 400）
        GameObject canvasGO = new GameObject("DamageFlashCanvas");
        Canvas canvas = canvasGO.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 300;
        canvasGO.AddComponent<CanvasScaler>().uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        canvasGO.AddComponent<GraphicRaycaster>();

        // 2. 全屏红色边缘图片（贴图是代码生成的）
        GameObject imgGO = new GameObject("Flash");
        imgGO.transform.SetParent(canvasGO.transform, false);
        flashImage = imgGO.AddComponent<Image>();
        flashImage.sprite = CreateEdgeVignetteSprite(); // 边缘白、中心透明的遮罩
        flashImage.color = new Color(flashColor.r, flashColor.g, flashColor.b, 0f); // 初始全透明
        flashImage.raycastTarget = false; // 不拦截鼠标点击/UI

        RectTransform rect = flashImage.GetComponent<RectTransform>();
        rect.anchorMin = Vector2.zero;  // 铺满整个屏幕
        rect.anchorMax = Vector2.one;
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;
    }

    /// <summary>
    /// 用代码生成"边缘白、中心透明"的方形渐变遮罩贴图（Vignette 效果）。
    /// 中心 α=0（完全透明），越靠边缘 α 越大（越红）→ 受伤时像屏幕四周渗血。
    /// </summary>
    private Sprite CreateEdgeVignetteSprite()
    {
        int size = 256;
        Texture2D tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
        tex.wrapMode = TextureWrapMode.Clamp;

        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                // 把坐标归一化到 -1 ~ 1（中心是 0）
                float nx = x / (float)(size - 1) * 2f - 1f;
                float ny = y / (float)(size - 1) * 2f - 1f;

                // 取"到四个边里最近的距离"（方形边缘，不是圆形）
                float edge = Mathf.Max(Mathf.Abs(nx), Mathf.Abs(ny)); // 0=中心 1=边缘
                // 平方加强：中间几乎透明，靠近边缘快速变浓
                float alpha = Mathf.Clamp01(edge * edge * 1.8f);

                tex.SetPixel(x, y, new Color(1f, 1f, 1f, alpha));
            }
        }
        tex.Apply();

        return Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f));
    }
}
