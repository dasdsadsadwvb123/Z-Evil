using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// 传送转场黑屏管理器（DontDestroyOnLoad 跨场景单例，运行时自动生成不用手动挂）：
/// 玩家传送时屏幕渐渐变黑 → 加载/切换场景 → 新场景里渐渐变亮——告别突兀闪切。
///
/// 时长（Inspector 可调，转场中黑屏时选中 Hierarchy 里的 FadeControllerRoot 就能改）：
/// - 渐黑 0.6 秒（离开当前场景）
/// - 渐亮 0.8 秒（新场景"睁眼"感，稍慢）
///
/// 公开 API：
/// - FadeController.TransitionToScene("场景名")：渐黑 → 加载新场景 → 渐亮
/// - FadeController.Transition(() => { ... })：渐黑 → 执行任意动作（如同场景瞬移）→ 渐亮
///
/// 技术细节：全屏黑 Image 挂 Screen Space Overlay Canvas（sortingOrder 999 压过一切 UI）；
/// alpha 动画全程用 unscaledTime（不受 timeScale=0 暂停影响）；
/// 转场中收到新的传送请求会被【忽略】（不再"立即执行"——否则会和正在跑的转场协程各加载一次场景，
/// 导致场景被重复加载、spawn 数据被吃空 → 玩家落到默认出生点）。IsTransitioning 供 Portal 做防重入。
/// </summary>
public class FadeController : MonoBehaviour
{
    public static FadeController Instance { get; private set; }

    [Header("转场时长（秒）")]
    [Tooltip("渐黑时长：离开当前场景")]
    public float fadeOutDuration = 0.6f;
    [Tooltip("渐亮时长：新场景揭示（稍慢有'睁眼'感）")]
    public float fadeInDuration = 0.8f;

    private CanvasGroup group;        // 黑屏 Canvas 的透明度/射线开关
    private bool isTransitioning = false;

    /// <summary> 是否正在转场（渐黑/加载/渐亮期间为 true）。传送防重入的唯一锁源：Portal 与 TeleportManager 都看它 </summary>
    public static bool IsTransitioning { get { return Instance != null && Instance.isTransitioning; } }

    /// <summary> 确保转场器存在（没有就自动生成常驻物体，和 AudibleAudio 同款零搭建惯例） </summary>
    private static FadeController Ensure()
    {
        if (Instance != null) return Instance;

        Instance = FindObjectOfType<FadeController>();
        if (Instance != null) return Instance;

        GameObject go = new GameObject("FadeControllerRoot");
        Instance = go.AddComponent<FadeController>();
        DontDestroyOnLoad(go);
        Instance.CreateUI();
        return Instance;
    }

    /// <summary> 跨场景传送转场：渐黑 → 加载 sceneName → 渐亮。转场中调用 = 忽略（防重复加载，丢弃连按的第二次） </summary>
    public static void TransitionToScene(string sceneName)
    {
        FadeController fc = Ensure();
        if (fc.isTransitioning)
        {
            // 转场中又收到传送请求：直接忽略，不做任何加载。
            // （旧实现是"立即 LoadScene"，会让正在跑的转场协程之后又 LoadScene 一次 → 场景重复加载、spawn 数据被吃空）
            Debug.LogWarning("[转场] 已有转场进行中，忽略重复的跨场景传送请求：" + sceneName);
            return;
        }
        fc.StartCoroutine(fc.TransitionRoutine(sceneName));
    }

    /// <summary> 包裹任意"瞬间动作"（如同场景瞬移）：渐黑 → 执行 → 渐亮。转场中调用 = 忽略（丢弃连按的第二次） </summary>
    public static void Transition(System.Action action)
    {
        FadeController fc = Ensure();
        if (fc.isTransitioning)
        {
            Debug.LogWarning("[转场] 已有转场进行中，忽略重复的同场景传送请求");
            return;
        }
        fc.StartCoroutine(fc.TransitionRoutine(action));
    }

    private IEnumerator TransitionRoutine(string sceneName)
    {
        isTransitioning = true;
        group.blocksRaycasts = true; // 黑屏期间挡住点击（防误触底下 UI）

        yield return FadeTo(1f, fadeOutDuration);   // 渐黑
        SceneManager.LoadScene(sceneName);          // 切场景（同步：当帧完成加载）
        yield return null;                          // 等一帧：新场景 Awake/Start 跑完（玩家瞬移到出生点）
        yield return FadeTo(0f, fadeInDuration);    // 渐亮

        group.blocksRaycasts = false;
        isTransitioning = false;
    }

    private IEnumerator TransitionRoutine(System.Action action)
    {
        isTransitioning = true;
        group.blocksRaycasts = true;

        yield return FadeTo(1f, fadeOutDuration);   // 渐黑
        action?.Invoke();                           // 黑透瞬间执行（如瞬移）
        yield return FadeTo(0f, fadeInDuration);    // 渐亮

        group.blocksRaycasts = false;
        isTransitioning = false;
    }

    /// <summary> alpha 从当前值渐变到 target（unscaledTime：暂停也照常转场） </summary>
    private IEnumerator FadeTo(float target, float duration)
    {
        float start = group.alpha;
        float t = 0f;
        while (t < duration)
        {
            t += Time.unscaledDeltaTime;
            group.alpha = Mathf.Lerp(start, target, Mathf.Clamp01(t / duration));
            yield return null;
        }
        group.alpha = target; // 收尾钉死，不留半透明尾巴
    }

    /// <summary> 生成全屏黑 Canvas（平时 alpha=0 隐藏 + 不挡射线，转场时才顶上来） </summary>
    private void CreateUI()
    {
        GameObject canvasGO = new GameObject("FadeCanvas");
        canvasGO.transform.SetParent(transform, false);

        Canvas canvas = canvasGO.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 999; // 压过一切 UI（柜子 250 / 背包 200 / 提示 100）

        CanvasScaler scaler = canvasGO.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);

        // 全屏黑色
        GameObject black = new GameObject("Black");
        black.transform.SetParent(canvasGO.transform, false);
        Image img = black.AddComponent<Image>();
        img.color = Color.black;
        img.raycastTarget = true; // 黑屏时挡射线（配合 CanvasGroup.blocksRaycasts 开关）
        RectTransform rect = img.GetComponent<RectTransform>();
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;

        // CanvasGroup 管 alpha + 射线开关（Image.color 不动，动画只改 group.alpha）
        group = canvasGO.AddComponent<CanvasGroup>();
        group.alpha = 0f;               // 平时完全透明（隐藏但不销毁）
        group.blocksRaycasts = false;   // 平时不挡点击
        group.interactable = false;
    }

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }
}
