using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement; // 用于记录/比较进场时的场景名（误判兜底恢复）

/// <summary>
/// 【场景 BGM 渐弱】挂在"本场景背景音乐 AudioSource 所在的那个物体"上。
///
/// 解决的问题：小泽从 game1 传到 game2 的那一刻，game1 正在播的 BGM 不要"啪"一下断掉，
/// 而是跟着黑幕一起渐渐弱下去（默认 0.6 秒）——黑幕盖住画面时音乐也刚好归零，转场更顺。
///
/// ⚠️ 只处理"离开本场景"的那一扇门：因为 game1 里既有"通往 game2 的跨场景门"，
/// 也有"同场景的破门"，一见转场就淡出会误伤同场景门 → 每次开破门音乐都会掉一下。
/// 所以本脚本用「Exit Portal 距离判定」只在玩家从指定那扇门离开时才淡出。
///
/// 挂法：只挂在"本场景播放背景音乐的那个 AudioSource 物体"上，把 exitPortal 拖成
/// "本场景通往下一个场景的那扇门"，其它什么都不用管。
/// </summary>
public class SceneBgmFade : MonoBehaviour
{
    [Header("音乐源")]
    [Tooltip("本场景的背景音乐源；留空就自动用本物体上的 AudioSource")]
    public AudioSource bgmSource;

    [Tooltip("正常播放时的目标音量")]
    public float targetVolume = 1f;

    [Header("开始播放的时机")]
    [Tooltip("true = 一进场景就把音乐从 0 渐入到目标音量（老行为）；false = 先静音待命，等『丧尸吼叫』开始响过 startAfterRoarSeconds 秒后自动开始播")]
    public bool playOnStart = false;
    [Tooltip("把 CinematicIntro 上那个『丧尸吼叫』的 AudioSource 拖这里：脚本检测到它开始响之后，再等 Start After Roar Seconds 秒就立刻开始播 BGM（不等它叫完）")]
    public AudioSource playAfterRoarSource;
    [Tooltip("音乐开始播放时的渐入时长（秒）；0 = 立刻满音量（小泽要的就是\"到点立马开始播放\"，所以默认 0）")]
    public float startFadeSeconds = 0f;
    [Tooltip("兜底超时（秒）：这么久还没检测到『丧尸吼叫』就直接开始播，避免漏拖引用导致永久没音乐")]
    public float waitRoarTimeout = 30f;
    [Tooltip("吼叫开始后等多久就立刻开始播音乐（秒）：不要等吼叫播完（它太长），默认 1.5 秒；想更早改成 1.0~1.2")]
    public float startAfterRoarSeconds = 1.5f;

    [Header("渐弱 / 渐入时长")]
    [Tooltip("离开场景时渐弱时长（秒）：默认和黑幕渐黑 0.6s 对齐")]
    public float fadeOutSeconds = 0.6f;
    [Tooltip("进场渐入时长（秒）：默认和渐亮 0.8s 对齐（用于 playOnStart=true 那条路径）")]
    public float fadeInSeconds = 0.8f;

    [Header("离开本场景的那扇门（关键）")]
    [Tooltip("第 1 扇『离开本场景』的跨场景传送门（game1 里就是那扇 targetSceneName=game2 的门）。三扇里任意一扇触发传送都会淡出，同场景的门不受影响")]
    public Transform exitPortal;
    [Tooltip("第 2 扇『离开本场景』的跨场景传送门（可选，留空不影响）——三扇门里任意一扇触发传送都会淡出")]
    public Transform exitPortal2;
    [Tooltip("第 3 扇『离开本场景』的跨场景传送门（可选，留空不影响）——三扇门里任意一扇触发传送都会淡出")]
    public Transform exitPortal3;
    [Tooltip("判定半径：转场开始时玩家离上面那扇门多近，才算\"是它触发的转场\"（三扇门共用这个半径）")]
    public float exitPortalRadius = 3f;

    [Header("可听范围（2D 音量圈：声音不做 3D 定位，只按距离改音量）")]
    [Tooltip("勾上 = 音乐只在玩家靠近时听得见：按平面距离衰减音量，声音始终 2D 居中（不会在左右耳之间飘）。用它就不用把 AudioSource 的 Spatial Blend 拉成 3D")]
    public bool useAudibleRange = false;
    [Tooltip("满音量半径（格）：玩家离本物体在这个距离内 = 完整音量。⚠️ 这里填的是真正的格数（用的是平面距离，不含相机高度差）")]
    public float rangeFullVolume = 12f;
    [Tooltip("完全静音距离（格）：超过这个距离音乐完全听不见（音量 0）")]
    public float rangeSilent = 16f;

    private GameObject player;             // 缓存玩家（避免每帧 FindGameObjectWithTag）
    private bool lastTransitioning = false; // 上一帧的转场状态，用来抓 false→true / true→false 跳变
    private bool isFadingOut = false;      // 淡出防重入
    private bool warnedNoPortal = false;   // "没拖门"的警告只报一次
    private Coroutine fadeRoutine;         // 当前音量渐变协程（同一时刻只允许一个，防叠加打架）
    private string startSceneName;         // 进场时的场景名（转场结束时用它判断"是不是真的换了场景"）

    private bool waitingForRoar = false;   // 是否在等『丧尸吼叫』开始响（响过后再按 startAfterRoarSeconds 计时开播）
    private bool roarStarted = false;      // 吼叫音源是否已经"真的响起来过"（两段式，第一段）
    private float roarWaitTimer = 0f;      // 等吼叫的计时（unscaledTime，用于兜底超时）
    private bool bgmStarted = false;       // 音乐是否已经开始播（防重入）

    private float baseVolume = 1f;         // "不含距离衰减"的基础音量：所有渐变/待命都改它，最终音量 = baseVolume × 距离衰减

    private void Awake()
    {
        if (bgmSource == null) bgmSource = GetComponent<AudioSource>();
        if (bgmSource == null)
        {
            Debug.LogWarning("[BGM渐弱] 没找到 AudioSource（也没拖），本脚本不生效", gameObject);
            enabled = false;
            return;
        }

        // 目标音量兜底：填了 0 就用音源当前音量
        if (targetVolume <= 0f) targetVolume = bgmSource.volume;
    }

    private void Start()
    {
        // 一律先停一次：音乐要"从头开始"，别在后台播到一半（Stop 只在 Start 里做，不在 Awake）
        if (bgmSource != null) bgmSource.Stop();

        if (playOnStart)
        {
            // 老行为：一进场景就从 0 渐入到目标音量
            baseVolume = 0f; ApplyVolume();
            bgmSource.Play();
            StartFade(targetVolume, fadeInSeconds);
        }
        else if (playAfterRoarSource != null)
        {
            // 先静音待命：待命音量设成目标值，等『丧尸吼叫』开始响、再计时到点后由 Update 立刻开播（此时 bgmSource 保持停止）
            baseVolume = targetVolume; ApplyVolume();
            waitingForRoar = true;
        }
        else
        {
            // 防呆：没拖吼叫音源就没法等它，改为进场直接播（绝不能永久静音）
            Debug.LogWarning("[BGM渐弱] 没拖『丧尸吼叫音源』，无法等它开始响，改为进场就直接播放", gameObject);
            baseVolume = 0f; ApplyVolume();
            bgmSource.Play();
            StartFade(targetVolume, fadeInSeconds);
        }

        // 记录进场时的场景名（转场结束时用它判断有没有真的换场景）
        startSceneName = SceneManager.GetActiveScene().name;

        // ★ 基线对齐：进场景那一刻可能"渐亮"还没结束（IsTransitioning 仍为 true），
        //   这里把上一帧状态对齐到当前值，让"进场时正在进行中的那次转场"不算新转场，
        //   避免进 game1 的第一帧被误判成"转场开始"而把音乐淡掉。
        lastTransitioning = FadeController.IsTransitioning;
    }

    private void Update()
    {
        if (bgmSource == null) return;
        ApplyVolume(); // ★ 每帧把"基础音量 × 距离衰减"写进音源（必须放在最前面：等吼叫那段有 return，放末尾会被跳过）

        // —— 等『丧尸吼叫』：吼叫开始响之后再过 startAfterRoarSeconds 秒就立刻开始播 BGM ——
        if (waitingForRoar)
        {
            // 第 1 段：先确认它真的响起来了（没响就不计时，避免"还没开叫"就开始数）
            if (!roarStarted && playAfterRoarSource != null && playAfterRoarSource.isPlaying)
            {
                roarStarted = true;
                roarWaitTimer = 0f;
            }

            // 第 2 段：响过之后开始计时，到点立刻开播（★ 不再等它播完）
            if (roarStarted)
            {
                roarWaitTimer += Time.unscaledDeltaTime;
                if (roarWaitTimer >= startAfterRoarSeconds)
                {
                    StartBgmNow();
                    waitingForRoar = false;
                    return;
                }
            }
            else
            {
                // 还没等到它响：走总超时兜底（防止漏拖/音源不对导致永久没音乐）
                roarWaitTimer += Time.unscaledDeltaTime;
                if (roarWaitTimer >= waitRoarTimeout)
                {
                    Debug.LogWarning("[BGM渐弱] 等待『丧尸吼叫』开始超时（" + waitRoarTimeout + " 秒），直接开始播放", gameObject);
                    StartBgmNow();
                    waitingForRoar = false;
                    return;
                }
            }
        }

        // —— 原有：转场淡出检测（一字未改） ——
        bool transitioning = FadeController.IsTransitioning; // 静态属性：有没有转场正在跑

        // 抓"false → true"这一下跳变（转场刚开始）
        if (transitioning && !lastTransitioning)
        {
            OnTransitionStarted();
        }
        // 抓"true → false"这一下跳变（转场刚结束）
        else if (!transitioning && lastTransitioning)
        {
            OnTransitionEnded();
        }

        lastTransitioning = transitioning;
    }

    /// <summary> 转场刚开始时被调用：只有"是那扇跨场景门触发的"才淡出 </summary>
    private void OnTransitionStarted()
    {
        // 三个槽位全空：防呆——按"任何转场都淡出"处理，但要警告一次
        if (!AnyPortalAssigned())
        {
            if (!warnedNoPortal)
            {
                warnedNoPortal = true;
                Debug.LogWarning("[BGM渐弱] 三扇 Exit Portal 都没拖，本次按\"任何转场都淡出\"处理；若是同场景门会误伤，建议把离开本场景的门拖进去", gameObject);
            }
            StartFadeOut();
            return;
        }

        // 拖了门：只在玩家离三扇门里任意一扇足够近时，才认定"是它触发的转场"→ 淡出
        if (PlayerNearAnyExitPortal())
        {
            StartFadeOut();
        }
        // 否则（同场景门等）什么都不做：音乐继续播，不受影响
    }

    /// <summary> 转场刚结束时被调用：只在"误判"时把音乐淡回来 </summary>
    private void OnTransitionEnded()
    {
        // 兜底恢复：只有"确实淡出过"且"场景名没变"（= 同场景，属于误判）时才淡回。
        if (!isFadingOut) return;
        if (SceneManager.GetActiveScene().name != startSceneName)
        {
            // 场景真的换了 → 什么都不做（正常跨场景；实际上旧场景对象这时已被销毁，这段根本不会执行）
            return;
        }

        // 场景没换 = 这次转场是同场景的（误判），把音乐在 fadeInSeconds 内淡回目标音量
        isFadingOut = false;
        StartFade(targetVolume, fadeInSeconds); // 复用统一入口（fadeRoutine 句柄负责防叠加，不会起第二个协程）
    }

    /// <summary> 三个槽位里有没有填过至少一扇门 </summary>
    private bool AnyPortalAssigned()
    {
        return exitPortal != null || exitPortal2 != null || exitPortal3 != null;
    }

    /// <summary> 玩家是否靠近三扇门里的任意一扇（空槽位自动跳过） </summary>
    private bool PlayerNearAnyExitPortal()
    {
        return PlayerNear(exitPortal) || PlayerNear(exitPortal2) || PlayerNear(exitPortal3);
    }

    /// <summary> 玩家是否在某扇门的判定半径内（portal 为空 → false） </summary>
    private bool PlayerNear(Transform portal)
    {
        if (portal == null) return false;
        if (player == null) player = GameObject.FindGameObjectWithTag("Player");
        if (player == null) return false;   // 找不到玩家 → 当作"不是它触发的"，宁可不动
        return Vector2.Distance(player.transform.position, portal.position) <= exitPortalRadius;
    }

    /// <summary> 立刻开始播放 BGM（防重入）：startFadeSeconds=0 时直接满音量，>0 时从 0 渐入 </summary>
    private void StartBgmNow()
    {
        if (bgmStarted) return;   // 只做一次
        bgmStarted = true;
        if (bgmSource == null) return;

        baseVolume = startFadeSeconds > 0f ? 0f : targetVolume; ApplyVolume();
        bgmSource.Play();
        if (startFadeSeconds > 0f) StartFade(targetVolume, startFadeSeconds);
    }

    /// <summary> 启动淡出（防重入） </summary>
    private void StartFadeOut()
    {
        if (isFadingOut) return;
        isFadingOut = true;
        StartFade(0f, fadeOutSeconds);
    }

    /// <summary> 启动一次音量渐变（同一时刻只保留一个渐变协程，避免渐入/淡出叠加打架） </summary>
    private void StartFade(float target, float duration)
    {
        if (fadeRoutine != null) StopCoroutine(fadeRoutine);
        fadeRoutine = StartCoroutine(FadeTo(target, duration));
    }

    /// <summary> 在 duration 秒内把"基础音量"线性渐变到 target（unscaledDeltaTime，不受 timeScale 影响） </summary>
    private IEnumerator FadeTo(float target, float duration)
    {
        if (bgmSource == null) yield break;

        if (duration <= 0f)
        {
            baseVolume = target; ApplyVolume();
            yield break;
        }

        float start = baseVolume;
        float t = 0f;
        while (t < duration)
        {
            t += Time.unscaledDeltaTime;
            baseVolume = Mathf.Lerp(start, target, Mathf.Clamp01(t / duration));
            ApplyVolume();
            yield return null;

            if (bgmSource == null) yield break; // 场景卸载中音源可能被销毁，防呆
        }
        if (bgmSource != null) { baseVolume = target; ApplyVolume(); } // 收尾钉死
    }

    /// <summary> 把"基础音量 × 距离衰减"写进 AudioSource（所有音量的唯一出口） </summary>
    private void ApplyVolume()
    {
        if (bgmSource == null) return;
        bgmSource.volume = baseVolume * RangeAttenuation();
    }

    /// <summary>
    /// 2D 音量圈衰减：用【平面距离】（Vector2，不含相机 z 高度差），
    /// 所以 Inspector 里填的数字就是地图上真正的格数——不会像 3D 那样被相机 -10 的高度差吃掉一大截。
    /// 声音本身仍是 2D（spatialBlend=0），两耳等量，不会随位置在左右声道间飘。
    /// </summary>
    private float RangeAttenuation()
    {
        if (!useAudibleRange) return 1f;                        // 没开范围 = 恒定音量（老行为）
        if (rangeSilent <= rangeFullVolume) return 1f;          // 配置不合法 → 不衰减（防呆）
        if (player == null) player = GameObject.FindGameObjectWithTag("Player");
        if (player == null) return 1f;                          // 找不到玩家 → 不衰减，宁可让它响

        float dist = Vector2.Distance(player.transform.position, transform.position);
        if (dist <= rangeFullVolume) return 1f;
        if (dist >= rangeSilent) return 0f;
        return (rangeSilent - dist) / (rangeSilent - rangeFullVolume);
    }

    // 调试可视化：选中时 Scene 窗口画出可听范围（绿圈=满音量，橙圈=出圈静音）
    private void OnDrawGizmosSelected()
    {
        if (!useAudibleRange) return;
        Gizmos.color = new Color(0.2f, 1f, 0.5f, 0.7f);   // 绿圈 = 满音量范围
        Gizmos.DrawWireSphere(transform.position, rangeFullVolume);
        Gizmos.color = new Color(1f, 0.5f, 0.2f, 0.6f);   // 橙圈 = 出圈就完全听不见
        Gizmos.DrawWireSphere(transform.position, rangeSilent);
    }
}
