using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 倒计时触发区（推箱子房限时挑战，单文件零现有脚本改动）：
/// 玩家踩上触发区（Collider2D 勾 IsTrigger）→ 一次性触发：屏幕居中大号倒计时 + BGM 播放；
/// 60 秒内解开谜题 → 小泽把 PlateSwitch 的 On All Activated 接线到本脚本的 StopCountdown()
/// （纯 Inspector 接线零代码）→ 计时停止 + 文字消失 + 音乐停；
/// 归零还没解开 → 玩家一次性全扣血（走现有死亡序列/DeathScreen）+ 音乐停 + 文字消失。
/// 计时用 Time.deltaTime（受暂停影响：暂停游戏时倒计时跟着停，防穿帮）；
/// BGM 用独立音源恒定音量（不走距离听声——BGM 跑哪都听得到），不循环（固定播一段的语义）。
/// 触发过就不再触发（isTriggered）；玩家死亡重开场景自动重置。
///
/// ★ 边沿触发（防"倒计时中死亡 → 复活在触发区内的存档点 → 倒计时又自动开始"）：
///   只有"从圈外进入圈内"的那一刻才触发；已经在圈内（读档/复活把玩家搬到存档点，正好落在区内）不算进入，
///   玩家必须先走出触发区、再重新进入才会开始倒计时（挑战仍可主动重来）。
///   启动后先等 armDelay 秒让读档/复位的位移落定，这期间只同步"在不在圈内"、绝不触发。
/// </summary>
public class CountdownTrigger : MonoBehaviour
{
    [Header("倒计时设置")]
    [Tooltip("倒计时秒数（归零 = 玩家死亡）")]
    public float countdownTime = 60f;
    [Tooltip("最后几秒开始闪红提示紧迫感（0 = 不闪）")]
    public float flashLastSeconds = 10f;

    [Header("倒计时文字外观（屏幕居中大号）")]
    public Color countdownColor = Color.red;
    [Tooltip("倒计时数字字号")]
    public int countdownFontSize = 72;
    [Tooltip("数字在屏幕的高度位置（0.5 = 正中，往下调小）")]
    [Range(0.2f, 0.8f)]
    public float screenY = 0.62f;

    [Header("音乐（独立音源恒定音量，不走距离听声；不循环）")]
    [Tooltip("一分多钟的紧张曲（固定播一段，不循环）")]
    public AudioClip musicClip;
    [Tooltip("BGM 音量")]
    [Range(0f, 1f)]
    public float musicVolume = 1f;

    private bool isTriggered = false;  // 一次性触发标记（死亡重开场景自动重置）
    private bool running = false;      // 倒计时进行中
    private float timeLeft = 0f;
    private string worldKey;           // 世界进度表钥匙（0=没触发 1=已触发/进行中 2=已解开）

    // ===== 边沿触发相关（防复活重演）=====
    [Header("防复活重演")]
    [Tooltip("场景加载后多久内不响应触发：等读档/复活的玩家位移落定，防“一复活就重演倒计时”。默认 0.3 秒足够")]
    public float armDelay = 0.3f;

    private bool playerInside = false; // 上一帧玩家是否在触发区内（边沿检测用）
    private bool armed = false;        // 是否已"武装"（启动一小会后、玩家位置落定才允许触发）
    private float armTime = 0f;        // 武装时刻（Start 里算好）
    private Transform playerT;         // 缓存玩家 Transform（省得每帧 FindGameObjectWithTag）

    private GameObject countdownCanvas; // 屏幕居中大号数字
    private Text countdownText;
    private AudioSource musicSource;    // BGM 专用（不走距离听声）

    private void Start()
    {
        armTime = Time.unscaledTime + armDelay; // 边沿触发：启动后先等一小会再允许触发（等读档/复位位移落定）

        // 自动补触发碰撞体（防呆：忘了加 Collider2D 也能用）
        Collider2D col = GetComponent<Collider2D>();
        if (col == null)
        {
            BoxCollider2D box = gameObject.AddComponent<BoxCollider2D>();
            box.isTrigger = true;
            box.size = new Vector2(1f, 1f);
        }
        else
        {
            col.isTrigger = true; // 触发区必须是 Trigger（不挡路）
        }

        // BGM 独立音源：恒定音量全程可闻（spatialBlend=0 = 2D 声音，不吃距离衰减）
        musicSource = GetComponent<AudioSource>();
        if (musicSource == null) musicSource = gameObject.AddComponent<AudioSource>();
        musicSource.playOnAwake = false;
        musicSource.loop = false; // 固定播一段的语义
        musicSource.spatialBlend = 0f;

        CreateCountdownUI();

        // 读档自查：世界进度表记录过（已触发 / 已解开）→ 不再重新触发，避免读档后又被扣血
        // （限时挑战只认"结果"，不还原剩余秒数——避免读档卡在半途倒计时里被反噬）
        worldKey = WorldState.KeyFor("Countdown", this);
        if (WorldState.Get(worldKey, 0) >= 1)
        {
            isTriggered = true;
            running = false;
            Debug.Log("[倒计时] 读档还原：本挑战已触发/已完成，不再重新计时", gameObject);
        }
    }

    /// <summary> 真正开始倒计时（边沿触发里"从圈外进入圈内"、且玩家活着时才调用） </summary>
    private void StartCountdown()
    {
        if (isTriggered || running) return;          // 一次性：触发过就不再响应

        GameObject playerObj = GameObject.FindGameObjectWithTag("Player");
        HealthSystem playerHealth = playerObj != null ? playerObj.GetComponent<HealthSystem>() : null;
        if (playerHealth == null || playerHealth.isDead) return; // 死人不触发

        isTriggered = true;
        running = true;
        timeLeft = countdownTime;
        WorldState.Set(worldKey, 1); // 世界进度表登记"已触发"

        // 屏幕大号倒计时 + BGM
        if (countdownCanvas != null) countdownCanvas.SetActive(true);
        RefreshCountdownText();
        if (musicClip != null)
        {
            musicSource.clip = musicClip;
            musicSource.volume = musicVolume;
            musicSource.Play();
        }

        Debug.Log("[倒计时] 开始！" + countdownTime + " 秒内解开机关，否则……", gameObject);
    }

    /// <summary> 玩家是否在触发区内（用触发碰撞体做点判定：不依赖 OnTriggerEnter2D 的物理事件时序，最稳） </summary>
    private bool IsPlayerInsideZone()
    {
        if (playerT == null)
        {
            GameObject p = GameObject.FindGameObjectWithTag("Player");
            if (p != null) playerT = p.transform;
        }
        if (playerT == null) return false;

        Collider2D zone = GetComponent<Collider2D>();
        if (zone == null) return false;
        return zone.OverlapPoint(playerT.position);
    }

    private void Update()
    {
        // ===== 边沿触发（防"复活在触发区内 → 倒计时重演"） =====
        // 只有"从圈外进入圈内"才触发；已经在圈内（读档/复活把玩家搬到正好在区内的存档点）不算进入。
        // 启动后先等 armDelay 秒：这期间只同步"在不在圈内"，绝不触发（等读档/复位的位移落定）。
        if (!isTriggered)
        {
            if (!armed && Time.unscaledTime >= armTime) armed = true;

            bool inside = IsPlayerInsideZone();
            if (!armed)
            {
                playerInside = inside; // 未武装：只同步状态，不触发
            }
            else
            {
                if (inside && !playerInside) StartCountdown(); // 上升沿：圈外 → 圈内
                playerInside = inside;
            }
        }

        if (!running) return;

        // Time.deltaTime 受暂停影响：timeScale=0（暂停）时为 0 → 倒计时自然冻结，防穿帮
        timeLeft -= Time.deltaTime;

        if (timeLeft <= 0f)
        {
            timeLeft = 0f;
            RefreshCountdownText();
            OnCountdownExpired();
            return;
        }

        RefreshCountdownText();
    }

    /// <summary> 倒计时归零 → 玩家死亡（走现有死亡序列）+ 收尾 </summary>
    private void OnCountdownExpired()
    {
        running = false;
        StopMusic();
        HideCountdownUI();

        // 一次性全扣血：currentHealth 是 public 字段，直接读出来扣 → 触发现有死亡序列/DeathScreen
        GameObject playerObj = GameObject.FindGameObjectWithTag("Player");
        if (playerObj != null)
        {
            HealthSystem playerHealth = playerObj.GetComponent<HealthSystem>();
            if (playerHealth != null && !playerHealth.isDead)
            {
                playerHealth.TakeDamage(playerHealth.currentHealth);
                Debug.Log("[倒计时] 时间到！机关反噬——玩家死亡", gameObject);
            }
        }
    }

    /// <summary>
    /// 停止倒计时（谜题解开的收尾）——给 PlateSwitch 的 On All Activated 接线用：
    /// 小泽在 Inspector 里：选中 PlateSwitch 物体 → On All Activated 事件列表点 + →
    /// 拖本脚本所在物体进槽 → 函数下拉选 CountdownTrigger → StopCountdown()
    /// </summary>
    public void StopCountdown()
    {
        if (!running) return; // 没在倒计时（没触发过/已结束）→ 安静返回，不影响其他接线
        running = false;
        WorldState.Set(worldKey, 2); // 世界进度表登记"已解开"
        StopMusic();
        HideCountdownUI();
        Debug.Log("[倒计时] 机关解除，倒计时停止", gameObject);
    }

    private void StopMusic()
    {
        if (musicSource != null && musicSource.isPlaying) musicSource.Stop();
    }

    // ======== 倒计时 UI（代码生成，屏幕居中大号） ========

    private void CreateCountdownUI()
    {
        countdownCanvas = new GameObject("CountdownCanvas");
        Canvas canvas = countdownCanvas.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 400; // 压过普通 UI（提示 100/背包 200），不被挡
        UIScale.Setup(countdownCanvas);

        GameObject textGO = new GameObject("CountdownText");
        textGO.transform.SetParent(countdownCanvas.transform, false);
        countdownText = textGO.AddComponent<Text>();
        countdownText.fontSize = countdownFontSize;
        countdownText.fontStyle = FontStyle.Bold;
        countdownText.color = countdownColor;
        countdownText.alignment = TextAnchor.MiddleCenter;
        countdownText.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        RectTransform rt = countdownText.GetComponent<RectTransform>();
        rt.anchorMin = new Vector2(0.5f, screenY);
        rt.anchorMax = new Vector2(0.5f, screenY);
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.anchoredPosition = Vector2.zero;
        rt.sizeDelta = new Vector2(400f, 120f);

        countdownCanvas.SetActive(false); // 触发前隐藏
    }

    /// <summary> 刷新数字（逐秒递减；最后 N 秒闪红） </summary>
    private void RefreshCountdownText()
    {
        if (countdownText == null) return;
        countdownText.text = Mathf.CeilToInt(timeLeft).ToString();

        if (flashLastSeconds > 0f && timeLeft <= flashLastSeconds)
        {
            // 闪烁：红色 ↔ 白色交替（Time.time 受暂停影响，闪烁也跟着暂停，语义一致）
            float t = Mathf.PingPong(Time.time * 4f, 1f);
            countdownText.color = t < 0.5f ? Color.white : Color.red;
        }
        else
        {
            countdownText.color = countdownColor;
        }
    }

    private void HideCountdownUI()
    {
        if (countdownCanvas != null) countdownCanvas.SetActive(false);
    }

    private void OnDestroy()
    {
        if (countdownCanvas != null) Destroy(countdownCanvas);
    }
}
