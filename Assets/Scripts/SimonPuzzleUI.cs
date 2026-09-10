using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;

/// <summary>
/// 心跳复律谜题界面（Simon 记忆序列，静态 Open 懒生成，PasswordPad 同款零搭建）：
/// 四个放电电极（红/黄/绿/蓝 2×2 圆形按钮）——仪器播放节律序列（电极依次闪亮 + 各自音调的心跳两连拍音），
/// 玩家照顺序点击复述；对 → 下一轮（序列加长 1 拍）；错 → 错误音 + 轮数重置回第 1 轮（界面不关重来）；
/// 连对 5 轮（长度 3→7 递增）→ 除颤成功 → 通知板侧吐钥匙卡。
/// 恐怖包装（第 5 轮专属）：播放序列时混进一个不属于四色电极的第五个低音（不影响判定，纯惊吓）+ 独白 toast。
/// 四个音调全部代码生成（AudioClip.Create 正弦波两连拍），零音频素材。
/// openedFrame 防同帧自关（柜子/背包同款守卫）。
/// </summary>
public class SimonPuzzleUI : MonoBehaviour
{
    private static SimonPuzzleUI instance;

    /// <summary> 谜题界面当前是否开着（除颤仪防重入） </summary>
    public static bool IsOpen { get; private set; }

    private const int TOTAL_ROUNDS = 5;     // 连对 5 轮通关
    private const float BEAT_INTERVAL = 0.62f; // 播放序列的节拍间隔（秒）

    // ---- 电极配置（颜色 / 亮起颜色 / 音调频率 Hz） ----
    private static readonly Color[] ELEC_COLOR   = { new Color(0.9f, 0.15f, 0.1f), new Color(0.95f, 0.8f, 0.15f), new Color(0.2f, 0.85f, 0.3f), new Color(0.2f, 0.5f, 0.95f) };
    private static readonly Color[] ELEC_LIT     = { new Color(1f, 0.5f, 0.45f),  new Color(1f, 0.95f, 0.55f),  new Color(0.6f, 1f, 0.65f),  new Color(0.55f, 0.8f, 1f) };
    // 音调频率在 SimonTones.GetHeartTone 里统一生成（330 起步每电极 +85 Hz），不在 UI 里重复配

    // ---- 运行时状态 ----
    private SimonPuzzleBoard board;
    private int openedFrame = -1;
    private int round = 1;                 // 当前轮（1~5）
    private List<int> sequence = new List<int>(); // 当前序列（每轮加长 1 拍）
    private int inputPos = 0;              // 玩家复述到第几拍
    private enum State { Showing, WaitingInput, Won }
    private State state = State.Showing;
    private bool ghostPlayed = false;      // 第 5 轮幽灵低音只出现一次
    private AudioSource audioSource;       // 心跳音/幽灵音都从这播

    // ---- UI 引用 ----
    private GameObject canvasObj;
    private Text statusText;                       // 轮次/提示行
    private Button[] elecButtons = new Button[4];
    private Image[] elecImages = new Image[4];

    /// <summary> 打开谜题（SimonPuzzleBoard 调用；没有实例就自动生成） </summary>
    public static void Open(SimonPuzzleBoard puzzleBoard)
    {
        if (instance == null)
        {
            GameObject go = new GameObject("SimonPuzzleUIRoot");
            instance = go.AddComponent<SimonPuzzleUI>();
        }
        instance.Show(puzzleBoard);
    }

    private void Show(SimonPuzzleBoard puzzleBoard)
    {
        board = puzzleBoard;

        EnsureEventSystem(); // 点击电极需要 EventSystem（场景里没有就自动补）

        IsOpen = true;
        openedFrame = Time.frameCount; // 开界面当帧的 F 不算关闭指令（防同帧自关）
        Time.timeScale = 0f;           // 暂停惯例

        if (audioSource == null)
        {
            audioSource = gameObject.AddComponent<AudioSource>();
            audioSource.playOnAwake = false;
        }

        round = 1;
        sequence.Clear();
        CreateUI();
        StartRound(); // 第 1 轮开始（播放序列）
    }

    /// <summary> 鼠标点击需要 EventSystem，场景里没有就自动生成一个 </summary>
    private static void EnsureEventSystem()
    {
        if (FindObjectOfType<EventSystem>() != null) return;
        GameObject es = new GameObject("EventSystem");
        es.AddComponent<EventSystem>();
        es.AddComponent<StandaloneInputModule>();
    }

    // ======== 界面构建 ========

    private void CreateUI()
    {
        if (canvasObj != null) Destroy(canvasObj);

        canvasObj = new GameObject("SimonPuzzleCanvas");
        Canvas canvas = canvasObj.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 305; // 密码面板同级（250 柜子 / 200 背包之上）
        canvasObj.AddComponent<CanvasScaler>().uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        canvasObj.AddComponent<GraphicRaycaster>();

        // 全屏暗底
        GameObject bg = new GameObject("DimBG");
        bg.transform.SetParent(canvasObj.transform, false);
        Image bgImg = bg.AddComponent<Image>();
        bgImg.color = new Color(0f, 0f, 0f, 0.75f);
        RectTransform bgRect = bgImg.GetComponent<RectTransform>();
        bgRect.anchorMin = Vector2.zero;
        bgRect.anchorMax = Vector2.one;
        bgRect.offsetMin = Vector2.zero;
        bgRect.offsetMax = Vector2.zero;

        // 中央面板
        GameObject panel = new GameObject("Panel");
        panel.transform.SetParent(canvasObj.transform, false);
        Image panelImg = panel.AddComponent<Image>();
        panelImg.color = new Color(0.1f, 0.1f, 0.1f, 0.92f); // 暗色面板惯例
        RectTransform panelRect = panelImg.GetComponent<RectTransform>();
        panelRect.anchorMin = new Vector2(0.5f, 0.5f);
        panelRect.anchorMax = new Vector2(0.5f, 0.5f);
        panelRect.pivot = new Vector2(0.5f, 0.5f);
        panelRect.anchoredPosition = Vector2.zero;
        panelRect.sizeDelta = new Vector2(480f, 560f);

        // 标题（金黄）
        GameObject title = new GameObject("Title");
        title.transform.SetParent(panel.transform, false);
        Text titleText = title.AddComponent<Text>();
        titleText.text = "=== 心 脏 复 律 ===";
        titleText.fontSize = 26;
        titleText.color = Color.yellow;
        titleText.alignment = TextAnchor.MiddleCenter;
        titleText.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        RectTransform titleRect = titleText.GetComponent<RectTransform>();
        titleRect.anchorMin = new Vector2(0f, 1f);
        titleRect.anchorMax = new Vector2(1f, 1f);
        titleRect.pivot = new Vector2(0.5f, 1f);
        titleRect.anchoredPosition = new Vector2(0f, -16f);
        titleRect.sizeDelta = new Vector2(440f, 40f);

        // 状态行（轮次/提示，红字）
        GameObject status = new GameObject("Status");
        status.transform.SetParent(panel.transform, false);
        statusText = status.AddComponent<Text>();
        statusText.fontSize = 20;
        statusText.color = new Color(1f, 0.45f, 0.4f);
        statusText.alignment = TextAnchor.MiddleCenter;
        statusText.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        RectTransform statusRect = statusText.GetComponent<RectTransform>();
        statusRect.anchorMin = new Vector2(0.5f, 1f);
        statusRect.anchorMax = new Vector2(0.5f, 1f);
        statusRect.pivot = new Vector2(0.5f, 1f);
        statusRect.anchoredPosition = new Vector2(0f, -58f);
        statusRect.sizeDelta = new Vector2(440f, 34f);

        // 2×2 电极网格
        GameObject grid = new GameObject("ElectrodeGrid");
        grid.transform.SetParent(panel.transform, false);
        GridLayoutGroup gridLayout = grid.AddComponent<GridLayoutGroup>();
        gridLayout.cellSize = new Vector2(130f, 130f);
        gridLayout.spacing = new Vector2(18f, 18f);
        gridLayout.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
        gridLayout.constraintCount = 2;
        RectTransform gridRect = grid.GetComponent<RectTransform>();
        gridRect.anchorMin = new Vector2(0.5f, 0.5f);
        gridRect.anchorMax = new Vector2(0.5f, 0.5f);
        gridRect.pivot = new Vector2(0.5f, 0.5f);
        gridRect.anchoredPosition = new Vector2(0f, 10f);

        // 四个圆形电极（代码生成圆贴图，InventoryUI 健康色块同款套路）
        Sprite circle = CreateCircleSprite();
        for (int i = 0; i < 4; i++)
        {
            GameObject elec = new GameObject("Electrode_" + i);
            elec.transform.SetParent(grid.transform, false);
            Image img = elec.AddComponent<Image>();
            img.sprite = circle;
            img.color = ELEC_COLOR[i];
            elecImages[i] = img;

            int captured = i; // 闭包捕获防呆
            Button btn = elec.AddComponent<Button>();
            btn.onClick.AddListener(() => OnElectrodeClicked(captured));
            elecButtons[i] = btn;
        }

        // 底部操作提示（灰字）
        GameObject hint = new GameObject("Hint");
        hint.transform.SetParent(panel.transform, false);
        Text hintText = hint.AddComponent<Text>();
        hintText.text = "记住闪亮的顺序，照原样点回来 | Esc / F 关闭";
        hintText.fontSize = 16;
        hintText.color = Color.gray;
        hintText.alignment = TextAnchor.MiddleCenter;
        hintText.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        RectTransform hintRect = hintText.GetComponent<RectTransform>();
        hintRect.anchorMin = new Vector2(0f, 0f);
        hintRect.anchorMax = new Vector2(1f, 0f);
        hintRect.pivot = new Vector2(0.5f, 0f);
        hintRect.anchoredPosition = new Vector2(0f, 14f);
        hintRect.sizeDelta = new Vector2(440f, 30f);
    }

    /// <summary> 代码生成圆形贴图（逐像素画圆 + 边缘抗锯齿，InventoryUI 健康色块同款） </summary>
    private Sprite CreateCircleSprite()
    {
        int size = 128;
        Texture2D tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
        tex.wrapMode = TextureWrapMode.Clamp;
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float nx = x / (float)(size - 1) * 2f - 1f;
                float ny = y / (float)(size - 1) * 2f - 1f;
                float dist = Mathf.Sqrt(nx * nx + ny * ny);
                float alpha = Mathf.Clamp01((1f - dist) / 0.1f);
                tex.SetPixel(x, y, new Color(1f, 1f, 1f, alpha));
            }
        }
        tex.Apply();
        return Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f));
    }

    // ======== 轮次管理 ========

    /// <summary> 开始一轮：确保序列长度 = 轮数+2 → 播放序列 </summary>
    private void StartRound()
    {
        inputPos = 0;
        ghostPlayed = false;
        int needLen = round + 2; // 第 N 轮 = N+2 拍（3→7）
        while (sequence.Count < needLen)
            sequence.Add(Random.Range(0, 4)); // 在原序列后面加拍（经典 Simon：同一段越来越长）

        SetStatus("第 " + round + " / " + TOTAL_ROUNDS + " 轮 —— 观察节律…");
        state = State.Showing;
        SetElectrodesInteractable(false);
        StartCoroutine(ShowSequenceRoutine());
    }

    /// <summary> 播放节律序列：电极依次闪亮 + 各自音调的心跳两连拍音；第 5 轮混进幽灵低音 </summary>
    private IEnumerator ShowSequenceRoutine()
    {
        yield return new WaitForSecondsRealtime(0.6f); // 开场呼吸

        for (int i = 0; i < sequence.Count; i++)
        {
            int idx = sequence[i];
            StartCoroutine(FlashElectrode(idx, 0.28f));
            audioSource.PlayOneShot(SimonTones.GetHeartTone(idx));

            // 第 5 轮恐怖包装：第 2 拍之后混进一个不属于四色电极的第五低音（不占判定，纯惊吓，只一次）
            if (round == TOTAL_ROUNDS && i == 1 && !ghostPlayed)
            {
                ghostPlayed = true;
                yield return new WaitForSecondsRealtime(0.32f);
                audioSource.PlayOneShot(SimonTones.GhostTone);
                board?.ShowToast("……刚才那个音，是谁按的？");
            }

            yield return new WaitForSecondsRealtime(BEAT_INTERVAL);
        }

        // 播完 → 玩家复述
        state = State.WaitingInput;
        inputPos = 0;
        SetElectrodesInteractable(true);
        SetStatus("照顺序点击电极（共 " + sequence.Count + " 拍）");
    }

    /// <summary> 电极闪亮片刻（播放/复述都走这个视觉反馈） </summary>
    private IEnumerator FlashElectrode(int idx, float duration)
    {
        elecImages[idx].color = ELEC_LIT[idx];
        yield return new WaitForSecondsRealtime(duration);
        elecImages[idx].color = ELEC_COLOR[idx];
    }

    /// <summary> 玩家点击电极：复述判定（WaitingInput 状态才响应） </summary>
    private void OnElectrodeClicked(int idx)
    {
        if (state != State.WaitingInput) return; // 播放中/已通关：点击无效

        StartCoroutine(FlashElectrode(idx, 0.22f));
        audioSource.PlayOneShot(SimonTones.GetHeartTone(idx));

        if (idx == sequence[inputPos])
        {
            // 这拍对了
            inputPos++;
            if (inputPos >= sequence.Count)
            {
                // 整轮全对
                round++;
                if (round > TOTAL_ROUNDS)
                {
                    Win();
                }
                else
                {
                    SetStatus("复律同步…… 第 " + round + " / " + TOTAL_ROUNDS + " 轮");
                    state = State.Showing;
                    SetElectrodesInteractable(false);
                    StartCoroutine(NextRoundRoutine());
                }
            }
        }
        else
        {
            // 错了：错误音 + 轮数重置回第 1 轮（界面不关，重来）
            board?.PlayError();
            StartCoroutine(WrongResetRoutine());
        }
    }

    private IEnumerator NextRoundRoutine()
    {
        yield return new WaitForSecondsRealtime(0.7f);
        StartRound();
    }

    private IEnumerator WrongResetRoutine()
    {
        state = State.Showing;
        SetElectrodesInteractable(false);
        SetStatus("心律失常！复律失败，从头再来");
        yield return new WaitForSecondsRealtime(1f);
        round = 1;
        sequence.Clear(); // 全部重置：新序列从 3 拍重新长
        StartRound();
    }

    /// <summary> 通关：全部电极闪白 2 次 → 关界面 → 板侧结算（成功音/钥匙卡） </summary>
    private void Win()
    {
        state = State.Won;
        SetStatus("除 颤 成 功");
        SetElectrodesInteractable(false);
        StartCoroutine(WinRoutine());
    }

    private IEnumerator WinRoutine()
    {
        for (int i = 0; i < 2; i++)
        {
            foreach (Image img in elecImages) img.color = Color.white;
            yield return new WaitForSecondsRealtime(0.2f);
            for (int k = 0; k < 4; k++) elecImages[k].color = ELEC_COLOR[k];
            yield return new WaitForSecondsRealtime(0.2f);
        }
        yield return new WaitForSecondsRealtime(0.3f);
        if (board != null) board.SolveSuccess(); // 先结算（成功音+钥匙卡）再关界面——Close 会停协程，顺序反了怕玄学
        Close();
    }

    // ======== 小工具 ========

    private void SetStatus(string text)
    {
        if (statusText != null) statusText.text = text;
    }

    private void SetElectrodesInteractable(bool on)
    {
        foreach (Button b in elecButtons)
            if (b != null) b.interactable = on;
    }

    // ======== 关闭 ========

    private void Update()
    {
        if (!IsOpen) return;

        // Esc/F 关闭：开界面当帧的 F 不算（除颤仪同帧开的，防同帧自关——柜子同款守卫）
        if (Input.GetKeyDown(KeyCode.Escape) || (Input.GetKeyDown(KeyCode.F) && Time.frameCount != openedFrame))
        {
            if (state == State.Won) return; // 演出中不允许关
            Close();
        }
    }

    private void Close()
    {
        IsOpen = false;
        Time.timeScale = 1f;
        StopAllCoroutines(); // 播放/闪亮协程全停
        if (canvasObj != null) Destroy(canvasObj);
    }

    private void OnDestroy()
    {
        if (IsOpen) { IsOpen = false; Time.timeScale = 1f; }
        if (canvasObj != null) Destroy(canvasObj);
    }
}

// ============================================================
// 代码生成音频（零素材）：四个心跳两连拍音调 + 幽灵低音 + 错误音 + 成功"滋"声
// ============================================================
public static class SimonTones
{
    private static AudioClip[] heartTones; // 四个心跳音调（懒生成缓存）

    /// <summary> 第 idx 个电极的心跳音（两连拍 "ba-dum"：短拍 + 间隔 + 重拍，正弦波 + 衰减包络） </summary>
    public static AudioClip GetHeartTone(int idx)
    {
        if (heartTones == null) heartTones = new AudioClip[4];
        if (heartTones[idx] == null)
            heartTones[idx] = CreateHeartTone("HeartTone_" + idx, 330 + idx * 85); // 330/415/500/585 Hz 附近，听感分明
        return heartTones[idx];
    }

    /// <summary> 第五个低音（110 Hz，不属于四色电极——第 5 轮惊吓专用） </summary>
    private static AudioClip ghostTone;
    public static AudioClip GhostTone
    {
        get
        {
            if (ghostTone == null) ghostTone = CreateTone("GhostTone", 110f, 0.45f, 1f);
            return ghostTone;
        }
    }

    /// <summary> 生成心跳两连拍：短拍(freq) → 间隔 → 重拍(freq，幅度更大)，尾部静音 </summary>
    private static AudioClip CreateHeartTone(string name, int freq)
    {
        int sr = 44100;
        float beat1 = 0.11f, gap = 0.07f, beat2 = 0.2f, tail = 0.12f;
        int total = Mathf.CeilToInt((beat1 + gap + beat2 + tail) * sr);
        float[] data = new float[total];

        for (int i = 0; i < total; i++)
        {
            float t = i / (float)sr;
            float v = 0f;
            if (t < beat1)
            {
                float k = t / beat1;
                v = Mathf.Sin(2f * Mathf.PI * freq * t) * (1f - k); // 第一拍：短 + 渐弱
            }
            else if (t > beat1 + gap && t < beat1 + gap + beat2)
            {
                float k = (t - beat1 - gap) / beat2;
                v = Mathf.Sin(2f * Mathf.PI * freq * t) * 1.2f * (1f - k); // 第二拍：更响更长的 "DUM"
            }
            data[i] = Mathf.Clamp(v, -1f, 1f) * 0.8f;
        }

        AudioClip clip = AudioClip.Create(name, total, 1, sr, false);
        clip.SetData(data, 0);
        return clip;
    }

    /// <summary> 生成单音（正弦波 + 渐弱包络；加一点二次谐波更"厚"） </summary>
    private static AudioClip CreateTone(string name, float freq, float duration, float volume)
    {
        int sr = 44100;
        int total = Mathf.CeilToInt(duration * sr);
        float[] data = new float[total];
        for (int i = 0; i < total; i++)
        {
            float t = i / (float)sr;
            float k = t / duration;
            float env = 1f - k; // 线性渐弱
            float v = Mathf.Sin(2f * Mathf.PI * freq * t) * 0.8f
                    + Mathf.Sin(2f * Mathf.PI * freq * 2f * t) * 0.2f; // 二次谐波
            data[i] = Mathf.Clamp(v, -1f, 1f) * volume * env;
        }
        AudioClip clip = AudioClip.Create(name, total, 1, sr, false);
        clip.SetData(data, 0);
        return clip;
    }

    /// <summary> 代码生成的成功"滋"声（扫频 700→1400 Hz，0.35 秒；小泽没拖 successClip 时用） </summary>
    public static void PlayZap(AudioSource src)
    {
        if (src == null) return;
        int sr = 44100;
        float dur = 0.35f;
        int total = Mathf.CeilToInt(dur * sr);
        float[] data = new float[total];
        for (int i = 0; i < total; i++)
        {
            float t = i / (float)sr;
            float k = t / dur;
            float freq = Mathf.Lerp(700f, 1400f, k); // 扫频上扬 = "充电完成"感
            float env = (1f - k) * 0.7f;
            data[i] = Mathf.Sin(2f * Mathf.PI * freq * t) * env;
        }
        AudioClip clip = AudioClip.Create("Zap", total, 1, sr, false);
        clip.SetData(data, 0);
        src.PlayOneShot(clip);
    }

    /// <summary> 代码生成的错误音（150 Hz 低频闷响 0.25 秒；小泽没拖 errorClip 时用） </summary>
    public static void PlayError(AudioSource src)
    {
        if (src == null) return;
        int sr = 44100;
        float dur = 0.25f;
        int total = Mathf.CeilToInt(dur * sr);
        float[] data = new float[total];
        for (int i = 0; i < total; i++)
        {
            float t = i / (float)sr;
            float env = 1f - t / dur;
            float v = Mathf.Sin(2f * Mathf.PI * 150f * t) * 0.9f
                    + Mathf.Sin(2f * Mathf.PI * 75f * t) * 0.3f; // 加次谐波更"闷"
            data[i] = Mathf.Clamp(v, -1f, 1f) * env;
        }
        AudioClip clip = AudioClip.Create("Error", total, 1, sr, false);
        clip.SetData(data, 0);
        src.PlayOneShot(clip);
    }
}
