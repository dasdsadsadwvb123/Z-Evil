using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.SceneManagement;
using Cinemachine; // 相机接管（Cinemachine 2.10.7，和 CarDriveIn.cs 同款引用）

/// <summary>
/// 【结局演出】罐子房的完整落幕演出，全部由它一个人干完（别再拆脚本）。
///
/// 挂到"罐子房那个演出物体"上（一个空物体即可，脚本会自己代码生成黑幕/标题/字幕 UI）。
///
/// 设计：暴君死后「其实游戏就已经结束了」，后半场纯属过场——【玩家不再出场】，
/// 由本脚本接管相机（Cinemachine）直接把镜头切/推到罐子房让玩家看到罐子闪红，
/// 再硬切黑屏 → 两幕标题 → 滚动字幕 → 显示"点击任意位置退出游戏"提示 → 等点击 → 退出游戏。
///
/// 演出顺序：
///   0. 立刻接管相机看向 stageTarget（罐子房中心；可瞬时切或自然跟）
///   1. 等 startDelay 秒 → 那一堆罐子同时闪红（统一节拍）+ 播"快要爆炸"警报音，持续 flashDuration 秒
///   2. 闪红结束 → 电影的"硬切/直切"：黑幕【瞬间】全黑（不是渐变，要"啪"一下）+ 一声重音
///   3. 黑屏上依次出现两幕标题：第一幕淡入→停→淡出；第二幕淡入→停→淡出
///   4. 黑屏白字向上滚的电影式落幕：整段字幕由一个 Text 从屏幕下方往上滚（第一行「感谢游玩」最大）
///   5. 滚完 → 停 Mathf.Max(endHoldSeconds, quitPromptDelay) 秒 →
///      ✅ 勾 waitClickToQuit：显示呼吸闪烁的提示，等玩家点击任意位置 → 退出游戏（推荐）
///      ⬜ 不勾 waitClickToQuit：SceneManager.LoadScene(returnSceneName) 回到一开始的界面（默认 Intro，旧行为）
///
/// 两种模式：
///   ✅ 罐子房是【独立 Scene】：勾 autoPlayOnStart（场景一进来就自动演，留 startDelay 给玩家看清房间）。
///   ⬜ 罐子房在【同一个场景】里：不勾 autoPlayOnStart（等 EndingTrigger 调 Play()）。
/// </summary>
public class EndingScene : MonoBehaviour
{
    // ============================ Inspector 字段 ============================

    [Header("相机接管（后半场不出现玩家，镜头直接看向罐子房）")]
    [Tooltip("场景里的 Cinemachine Virtual Camera（留空则自动用 FindObjectOfType 找）")]
    public CinemachineVirtualCamera virtualCamera;
    [Tooltip("罐子房中心的一个空物体：决定镜头看哪儿（小泽自己摆位置决定构图）")]
    public Transform stageTarget;
    [Tooltip("勾 = 瞬间切到罐子房（不要'飞过去'的过程）；不勾 = 让镜头自然跟过去")]
    public bool snapCamera = true;

    [Header("罐子闪红")]
    [Tooltip("罐子的父物体（一个或多个都行）；脚本会自动把父物体下所有 SpriteRenderer 收进来，不用一个个拖")]
    public Transform[] vatRoots;
    [Tooltip("闪红用的颜色")]
    public Color flashColor = new Color(1f, 0.12f, 0.12f);
    [Tooltip("闪烁间隔（秒）：所有罐子用统一节拍一起切换")]
    public float blinkInterval = 0.1f;
    [Tooltip("闪红持续多久（秒）")]
    public float flashDuration = 3f;
    [Tooltip("进来后等多久开始闪（秒）：跨场景模式留给小泽看清房间")]
    public float startDelay = 0f;
    [Tooltip("“快要爆炸”的警报音（不拖 = 静音不报错）")]
    public AudioClip alarmClip;
    [Tooltip("警报音音量")]
    public float alarmVolume = 0.9f;
    [Tooltip("警报音重复播放的间隔（秒）")]
    public float alarmInterval = 0.6f;
    [Tooltip("勾 = 场景一进来就自动播（罐子房是独立 Scene 时用）；不勾 = 等 EndingTrigger 调 Play()")]
    public bool autoPlayOnStart = false;

    [Header("硬切与标题")]
    [Tooltip("硬切那一下的重音（不拖 = 静音）")]
    public AudioClip hardCutClip;
    [Tooltip("标题两幕，按顺序依次演（小泽可以改文字）")]
    public string[] titleTexts = new string[] { "Z-Evil", "未完待续" };
    [Tooltip("标题字号")]
    public int titleFontSize = 130;
    [Tooltip("每幕淡入/淡出时长（秒）")]
    public float titleFadeSeconds = 0.6f;
    [Tooltip("每幕停住时长（秒）")]
    public float titleHoldSeconds = 1.6f;

    [Header("滚动落幕")]
    [Tooltip("整段落幕字幕（支持 Unity 富文本，如 <size=140> 标签）")]
    [TextArea(10, 30)]
    public string creditsText = "<size=140>感谢游玩</size>\n\n\n（下面这些行小泽自己填写）\n\n策划 / 剧本　小泽\n美术　（画师名字）\n特效 / 程序　小泽\n\n感谢每一个陪我把它做完的夜晚。";
    [Tooltip("滚动速度（像素/秒，1920×1080 参考分辨率下）")]
    public float scrollSpeed = 90f;
    [Tooltip("滚完之后停多久再回标题（秒）")]
    public float endHoldSeconds = 1.5f;
    [Tooltip("回哪个场景（一开始的界面）；只在【不勾】Wait Click To Quit 时才会用到")]
    public string returnSceneName = "Intro";

    [Header("落幕结束后的退出提示（照开场 IntroTitle 的形式）")]
    [Tooltip("勾上 = 落幕滚完后显示提示文字、等玩家点击任意位置再退出游戏（推荐）。不勾 = 沿用旧行为：自动加载 Return Scene Name 回开场界面")]
    public bool waitClickToQuit = true;
    [Tooltip("提示文字（白字、居中、透明度呼吸闪烁；留空则不显示文字，但依然等点击）")]
    public string quitPrompt = "点击任意位置退出游戏";
    [Tooltip("提示文字呼吸闪烁速度（和开场 IntroTitle 的 promptPulseSpeed 同款手感；数值越大闪得越快）")]
    public float quitPromptPulseSpeed = 2.5f;
    [Tooltip("落幕滚完后再等多久才显示提示文字（秒）")]
    public float quitPromptDelay = 0.5f;

    // ============================ 运行时缓存 ============================

    private SpriteRenderer[] vatRenderers;   // 自动收集到的所有罐子
    private Color[] vatOriginalColors;       // 每个罐子的原色（结束时必须写回）
    private AudioSource audioSource;         // 自建的 2D 过场音源
    private Image blackScreen;               // 全屏黑幕（硬切/标题/字幕的背景）
    private Text titleLabel;                 // 标题文字（两幕复用同一个）
    private CanvasGroup titleGroup;          // 标题整组透明度（连描边/投影一起淡）
    private Text creditsLabel;               // 滚动字幕
    private Text quitPromptLabel;            // "点击任意位置退出游戏"提示（落幕滚完后才显示）
    private bool playing = false;            // 防重入

    private Transform originalFollow;        // 接管前相机原本跟谁（本项目结束时不必恢复，仅作记录/防呆）
    private Transform originalLookAt;        // 接管前相机原本看谁

    private const float RefHeight = 1080f;   // 参考分辨率高度（UIScale 里也是 1920×1080）

    // ============================ 生命周期 ============================

    /// <summary>
    /// 所有引用缓存都放 Awake（同场景模式是 SetActive(true) 后同帧就调 Play()，不能依赖 Start 已跑完）。
    /// </summary>
    private void Awake()
    {
        CacheAudioSource();
        CacheVats();
        BuildUI();
    }

    private void Start()
    {
        // Start 只负责"是否自动开演"这一步判断
        if (autoPlayOnStart) Play();
    }

    // ============================ 公开入口 ============================

    /// <summary> 开始结局演出（防重入：重复调用会被忽略并打警告） </summary>
    public void Play()
    {
        if (playing)
        {
            Debug.LogWarning("[结局演出] Play() 被重复调用，已忽略", this);
            return;
        }
        playing = true;
        StartCoroutine(EndingRoutine());
    }

    // ============================ 主流程 ============================

    private IEnumerator EndingRoutine()
    {
        // ── 第 0 步：接管相机看向罐子房（必须放在 startDelay 等待"之前"：同场景模式是黑透瞬间被调用的） ──
        TakeOverCamera();

        // ── 第 1 步：进来先等一会儿 ──
        if (startDelay > 0f) yield return new WaitForSecondsRealtime(startDelay);

        // ── 第 2 步：罐子齐闪红 + 警报 ──
        yield return FlashVats();

        // ── 第 3 步：硬切——"啪"一下瞬间全黑（不是渐变） ──
        // 用纯黑（和 FadeController 渐黑到底的纯黑无缝衔接，不会出现"黑里透光"的跳变）
        if (blackScreen != null) blackScreen.color = Color.black;
        if (hardCutClip != null && audioSource != null) audioSource.PlayOneShot(hardCutClip, 1f);
        yield return null; // 让这一帧的黑先渲染出来

        // ── 第 4 步：两幕标题，分先后顺序 ──
        if (titleTexts != null && titleTexts.Length > 0 && titleLabel != null && titleGroup != null)
        {
            titleLabel.gameObject.SetActive(true);
            foreach (string t in titleTexts)
            {
                if (string.IsNullOrEmpty(t)) continue; // 空字符串跳过（防呆）
                titleLabel.text = t;
                yield return FadeGroup(titleGroup, 0f, 1f, titleFadeSeconds); // 淡入
                yield return new WaitForSecondsRealtime(titleHoldSeconds);    // 停住
                yield return FadeGroup(titleGroup, 1f, 0f, titleFadeSeconds); // 淡出
            }
            titleLabel.gameObject.SetActive(false);
        }

        // ── 第 5 步：黑屏白字向上滚（电影式落幕） ──
        yield return ScrollCredits();

        // ── 第 6 步：等一会儿（显示退出提示前的缓冲） ──
        // 两个值取大的：既保留老的 endHoldSeconds 手感，又满足新加的 quitPromptDelay
        yield return new WaitForSecondsRealtime(Mathf.Max(endHoldSeconds, quitPromptDelay));

        // ── 第 7 步：等玩家点击任意位置 → 退出游戏（推荐）；不勾则沿用旧行为回开场界面 ──
        if (waitClickToQuit)
        {
            yield return WaitClickToQuit(); // 显示呼吸闪烁的提示，等到鼠标左键/触摸按下
            QuitGame();                     // 立刻退出游戏（编辑器/打包分支内部分开处理）
        }
        else
        {
            // 旧行为：回到一开始的界面
            if (!string.IsNullOrEmpty(returnSceneName))
                SceneManager.LoadScene(returnSceneName);
            else
                Debug.LogWarning("[结局演出] returnSceneName 为空，无法回到标题场景", this);
        }
    }

    // ============================ 各阶段实现 ============================

    /// <summary>
    /// 接管相机：让 Cinemachine 虚拟相机跟随并看向罐子房中心的 stageTarget（照项目既有写法 CarDriveIn.cs）。
    /// 空引用一律只打 LogWarning 跳过，不崩。演出结束时不必恢复（反正要回 Intro 场景）。
    /// </summary>
    private void TakeOverCamera()
    {
        if (virtualCamera == null) virtualCamera = FindObjectOfType<CinemachineVirtualCamera>(); // 留空则自动找
        if (virtualCamera == null)
        {
            Debug.LogWarning("[结局演出] 没找到 CinemachineVirtualCamera，跳过镜头接管", this);
            return;
        }
        if (stageTarget == null)
        {
            Debug.LogWarning("[结局演出] stageTarget 为空，没地方让镜头看，跳过镜头接管", this);
            return;
        }

        // 记录原目标（本项目结束时不用恢复，留着便于排查；CarDriveIn 也是这么存的）
        originalFollow = virtualCamera.Follow;
        originalLookAt = virtualCamera.LookAt;

        // 只用 Follow / LookAt —— CarDriveIn.cs 已用过、100% 可用的两个成员
        virtualCamera.Follow = stageTarget;
        virtualCamera.LookAt = stageTarget;

        // 瞬时切过去：把 PreviousStateIsValid 置 false，强制下一帧忽略时间插值、直接定位到新目标。
        // （已核对：Cinemachine 2.10.7 的 CinemachineVirtualCameraBase 里是 public virtual bool PreviousStateIsValid { get; set; }）
        // 若小泽想改成"飞过去"，把 snapCamera 取消勾选即可；也可直接把 vcam 的阻尼调 0 让它更快到位。
        if (snapCamera) virtualCamera.PreviousStateIsValid = false;
    }

    /// <summary> 罐子齐闪红：所有罐子用【统一 bool 状态】一起切换（同一节拍），全程 unscaledDeltaTime </summary>
    private IEnumerator FlashVats()
    {
        int n = (vatRenderers != null) ? vatRenderers.Length : 0;
        bool on = false;                  // 统一的闪烁状态
        float elapsed = 0f;
        float blinkTimer = blinkInterval; // 提前给它一个间隔：第一帧就立即变红（响应更快）
        float alarmTimer = alarmInterval; // 提前给它一个间隔：第一帧就响第一声警报

        while (elapsed < flashDuration)
        {
            float dt = Time.unscaledDeltaTime;
            elapsed += dt;
            blinkTimer += dt;
            alarmTimer += dt;

            // 统一节拍：到点了就整体翻转一次（所有罐子共用同一个 on）
            if (n > 0 && blinkInterval > 0f && blinkTimer >= blinkInterval)
            {
                blinkTimer -= blinkInterval;
                on = !on;
                SetVatFlash(on);
            }

            // 警报音按 alarmInterval 重复播（2D 恒定音量，用受控音源，不用 PlayClipAtPoint）
            if (alarmClip != null && audioSource != null && alarmInterval > 0f && alarmTimer >= alarmInterval)
            {
                alarmTimer -= alarmInterval;
                audioSource.PlayOneShot(alarmClip, alarmVolume);
            }

            yield return null;
        }

        // 收尾必须把每个罐子的原色写回，别把它们永久染红
        if (n > 0) SetVatFlash(false);
    }

    /// <summary> 滚动字幕：从屏幕下方往上滚到屏幕上方之外 </summary>
    private IEnumerator ScrollCredits()
    {
        if (creditsLabel == null) yield break;

        creditsLabel.gameObject.SetActive(true);
        creditsLabel.text = creditsText;

        RectTransform rt = creditsLabel.rectTransform;
        // 先定宽，再让 Text 自己算需要多高（preferredHeight）
        rt.sizeDelta = new Vector2(1500f, 0f);
        Canvas.ForceUpdateCanvases(); // 强制立即刷新，保证 preferredHeight 当场算出来
        float h = creditsLabel.preferredHeight;
        if (h < 10f) h = 10f;
        rt.sizeDelta = new Vector2(1500f, h);

        // 锚点/轴心都在屏幕底部：anchoredPosition.y 就是"字幕底边"相对屏幕底的高度
        float y = -h;                     // 起点：整段字幕完全在屏幕下方
        float endY = RefHeight + 120f;    // 终点：整段字幕完全滚出屏幕上方（留点余量）
        rt.anchoredPosition = new Vector2(0f, y);

        while (y < endY)
        {
            y += scrollSpeed * Time.unscaledDeltaTime;
            rt.anchoredPosition = new Vector2(0f, y);
            yield return null;
        }
    }

    /// <summary>
    /// 显示"点击任意位置退出游戏"提示，并原地等待玩家点击（鼠标左键 / 触摸）。
    /// 提示文字照开场 IntroTitle 那样做透明度呼吸闪烁；全程 unscaledDeltaTime，暂停也照常跳。
    /// 用"循环 + 每帧查一次输入"代替 WaitUntil：这样同一条协程里就能顺手做呼吸闪烁，不用另外加 Update。
    /// </summary>
    private IEnumerator WaitClickToQuit()
    {
        // 先显示提示（文字留空的话就只等点击、不显示东西）
        if (quitPromptLabel != null && !string.IsNullOrEmpty(quitPrompt))
        {
            quitPromptLabel.text = quitPrompt;
            quitPromptLabel.gameObject.SetActive(true);
        }

        float t = 0f;
        while (!Input.GetMouseButtonDown(0))
        {
            t += Time.unscaledDeltaTime;
            if (quitPromptLabel != null && quitPromptLabel.gameObject.activeSelf)
            {
                // sin 输出 -1~1，先映射到 0~1，再用 Lerp 夹到 0.4~1.0 之间来回"呼吸"
                float a = Mathf.Lerp(0.4f, 1f, 0.5f + 0.5f * Mathf.Sin(t * quitPromptPulseSpeed));
                Color c = quitPromptLabel.color;
                c.a = a;
                quitPromptLabel.color = c;
            }
            yield return null;
        }

        // 点下去瞬间把提示钉成全亮，给个"确实按到了"的反馈
        if (quitPromptLabel != null) quitPromptLabel.color = new Color(1f, 1f, 1f, 1f);
    }

    /// <summary>
    /// 真正退出游戏。
    /// ⚠️ 关键：编辑器里 Application.Quit() 是【无效】的（编辑模式下它不退），必须用 UnityEditor.EditorApplication.isPlaying = false。
    /// 所以用 #if UNITY_EDITOR 隔开：#else 分支才是打包后真正跑的 Application.Quit()。
    /// </summary>
    private void QuitGame()
    {
#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false; // 编辑器里"停止播放"，等效于退出游戏
#else
        Application.Quit(); // 打包后的真机/PC 上真正退出
#endif
    }

    // ============================ 小工具 ============================

    /// <summary> 把某个 CanvasGroup 的 alpha 从 from 渐变到 to（unscaledTime，暂停也照常） </summary>
    private IEnumerator FadeGroup(CanvasGroup group, float from, float to, float duration)
    {
        if (group == null) yield break;
        if (duration <= 0f) { group.alpha = to; yield break; }

        float t = 0f;
        while (t < duration)
        {
            t += Time.unscaledDeltaTime;
            group.alpha = Mathf.Lerp(from, to, Mathf.Clamp01(t / duration));
            yield return null;
        }
        group.alpha = to; // 收尾钉死
    }

    /// <summary> 统一设置所有罐子：true = 闪红，false = 还原各自原色 </summary>
    private void SetVatFlash(bool red)
    {
        if (vatRenderers == null) return;
        for (int i = 0; i < vatRenderers.Length; i++)
        {
            if (vatRenderers[i] == null) continue; // 空引用防呆
            vatRenderers[i].color = red ? flashColor : vatOriginalColors[i];
        }
    }

    /// <summary> 自动收集所有罐子的 SpriteRenderer，并记录它们进去时的原色 </summary>
    private void CacheVats()
    {
        List<SpriteRenderer> list = new List<SpriteRenderer>();
        if (vatRoots != null)
        {
            foreach (Transform root in vatRoots)
            {
                if (root == null) continue;
                // includeInactive = true：防止罐子所在的父物体暂时隐藏就收不到
                list.AddRange(root.GetComponentsInChildren<SpriteRenderer>(true));
            }
        }

        vatRenderers = list.ToArray();
        vatOriginalColors = new Color[vatRenderers.Length];
        for (int i = 0; i < vatRenderers.Length; i++)
            vatOriginalColors[i] = vatRenderers[i].color; // 记录原色，演出结束写回

        if (vatRenderers.Length == 0)
            Debug.LogWarning("[结局演出] 没有收集到任何罐子 SpriteRenderer（检查 vatRoots 是否拖了罐子的父物体），闪红阶段会跳过罐子", this);
    }

    /// <summary> 自建一个 2D 过场音源（恒定音量，不吃距离衰减） </summary>
    private void CacheAudioSource()
    {
        audioSource = GetComponent<AudioSource>();
        if (audioSource == null) audioSource = gameObject.AddComponent<AudioSource>();
        audioSource.playOnAwake = false;
        audioSource.loop = false;
        audioSource.spatialBlend = 0f; // 2D 过场音：恒定音量
        audioSource.volume = 1f;       // 单发音量用 PlayOneShot 的 volumeScale 控制
    }

    /// <summary> 代码生成结局 UI（黑幕 / 标题 / 字幕），初始全部隐藏 </summary>
    private void BuildUI()
    {
        // ── 画布：sortingOrder 1000 压过 FadeController 的黑幕（999） ──
        GameObject canvasGO = new GameObject("EndingCanvas");
        canvasGO.transform.SetParent(transform, false); // 挂到演出物体下：随它激活，Hierarchy 也整洁
        Canvas canvas = canvasGO.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 1000;
        UIScale.Setup(canvasGO);               // ★ 必须用工具类：1920×1080 + 按高匹配，别手写 CanvasScaler
        canvasGO.AddComponent<GraphicRaycaster>();

        // ── 全屏黑幕（硬切时"啪"一下变纯黑，平时 alpha=0 隐藏） ──
        GameObject blackGO = new GameObject("BlackScreen", typeof(RectTransform)); // ★ UI 物体必须带 RectTransform
        blackGO.transform.SetParent(canvasGO.transform, false);
        blackScreen = blackGO.AddComponent<Image>();
        blackScreen.color = new Color(0f, 0f, 0f, 0f); // 初始透明
        blackScreen.raycastTarget = false;
        RectTransform br = blackScreen.rectTransform;
        br.anchorMin = Vector2.zero;
        br.anchorMax = Vector2.one;
        br.offsetMin = Vector2.zero;
        br.offsetMax = Vector2.zero;

        // ── 标题文字（黄色，两幕复用同一个；用 CanvasGroup 整组淡入淡出） ──
        GameObject titleGO = new GameObject("TitleText", typeof(RectTransform));
        titleGO.transform.SetParent(canvasGO.transform, false);
        titleLabel = titleGO.AddComponent<Text>();
        titleLabel.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        titleLabel.fontSize = titleFontSize;
        titleLabel.color = new Color(1f, 0.9f, 0.3f); // 黄色标题（项目惯例）
        titleLabel.alignment = TextAnchor.MiddleCenter;
        titleLabel.horizontalOverflow = HorizontalWrapMode.Overflow;
        titleLabel.verticalOverflow = VerticalWrapMode.Overflow;
        titleLabel.raycastTarget = false;
        AddOutlineShadow(titleGO); // 黑描边 + 投影，任何背景都看得清
        RectTransform tr = titleLabel.rectTransform;
        tr.anchorMin = new Vector2(0.5f, 0.5f);
        tr.anchorMax = new Vector2(0.5f, 0.5f);
        tr.pivot = new Vector2(0.5f, 0.5f);
        tr.anchoredPosition = Vector2.zero;
        tr.sizeDelta = new Vector2(1600f, 300f);
        titleGroup = titleGO.AddComponent<CanvasGroup>();
        titleGroup.alpha = 0f;
        titleGroup.blocksRaycasts = false;
        titleGroup.interactable = false;

        // ── 滚动字幕（白字，锚点在屏幕底部；滚动时改 anchoredPosition.y） ──
        GameObject creditsGO = new GameObject("CreditsText", typeof(RectTransform));
        creditsGO.transform.SetParent(canvasGO.transform, false);
        creditsLabel = creditsGO.AddComponent<Text>();
        creditsLabel.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        creditsLabel.fontSize = 56;
        creditsLabel.color = Color.white;                 // 正文白字
        creditsLabel.alignment = TextAnchor.UpperCenter;  // 顶部对齐，便于整段往上推
        creditsLabel.horizontalOverflow = HorizontalWrapMode.Wrap;
        creditsLabel.verticalOverflow = VerticalWrapMode.Overflow;
        creditsLabel.supportRichText = true;              // ★ 必须开：<size=140> 这类标签才生效
        creditsLabel.raycastTarget = false;
        AddOutlineShadow(creditsGO);
        RectTransform cr = creditsLabel.rectTransform;
        cr.anchorMin = new Vector2(0.5f, 0f); // 锚点放屏幕底部
        cr.anchorMax = new Vector2(0.5f, 0f);
        cr.pivot = new Vector2(0.5f, 0f);
        cr.anchoredPosition = new Vector2(0f, 0f);
        cr.sizeDelta = new Vector2(1500f, 100f); // 高度稍后按 preferredHeight 动态设
        creditsLabel.gameObject.SetActive(false); // 滚动阶段才显示

        // ── 退出提示（白字，屏幕中下方；落幕滚完之后才显示，平时隐藏） ──
        // ★ 放在最后一个子物体：渲染层最上，压在滚动字幕之上
        GameObject promptGO = new GameObject("QuitPrompt", typeof(RectTransform));
        promptGO.transform.SetParent(canvasGO.transform, false);
        quitPromptLabel = promptGO.AddComponent<Text>();
        quitPromptLabel.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        quitPromptLabel.fontSize = 40;
        quitPromptLabel.color = Color.white;               // 正文白字
        quitPromptLabel.alignment = TextAnchor.MiddleCenter;
        quitPromptLabel.horizontalOverflow = HorizontalWrapMode.Overflow;
        quitPromptLabel.verticalOverflow = VerticalWrapMode.Overflow;
        quitPromptLabel.raycastTarget = false;
        AddOutlineShadow(promptGO);                        // 黑描边 + 投影，黑屏上也看得清
        RectTransform pr = quitPromptLabel.rectTransform;
        pr.anchorMin = new Vector2(0.5f, 0f);              // 锚在屏幕底部
        pr.anchorMax = new Vector2(0.5f, 0f);
        pr.pivot = new Vector2(0.5f, 0.5f);
        pr.anchoredPosition = new Vector2(0f, 160f);       // 距屏幕底 160 像素
        pr.sizeDelta = new Vector2(1200f, 80f);
        quitPromptLabel.gameObject.SetActive(false);       // 落幕结束前不显示
    }

    /// <summary> 给文字加黑描边 + 投影（照 DialogueManager.CreateSkipHint 的写法） </summary>
    private void AddOutlineShadow(GameObject go)
    {
        Outline outline = go.AddComponent<Outline>();
        outline.effectColor = Color.black;
        outline.effectDistance = new Vector2(1.5f, -1.5f);

        Shadow shadow = go.AddComponent<Shadow>();
        shadow.effectColor = new Color(0f, 0f, 0f, 0.8f);
        shadow.effectDistance = new Vector2(2f, -2f);
    }
}
