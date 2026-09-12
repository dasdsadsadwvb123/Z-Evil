using UnityEngine;
using UnityEngine.UI;
using System.Collections.Generic;

/// <summary>
/// 档案室密码机：玩家持线索（照片编号顺序）靠近按 F 打开输入界面
/// → 按数字键 1-4 依次输入 → 回车确认。
/// 正确 → 在机器旁生成钥匙 PickupItem（itemID=ClinicKey，捡起走正常拾取流程）；
/// 错误 → 屏幕红闪 + 清空重输，可无限重试。
/// 挂到密码机物体上（建议：Square 占位图 + 可选小实体碰撞，不要勾 Is Trigger）。
/// 红闪为本脚本自建（DamageFlash 没有公开触发方法，为做到零修改现有脚本）。
/// </summary>
public class PasswordMachine : MonoBehaviour
{
    [Header("密码配置（照片正确顺序，如 3-1-4-2）")]
    [Tooltip("正确密码：几个数字就按几下，照片编号是几就填几")]
    public int[] correctCode = new int[] { 3, 1, 4, 2 };

    [Header("奖励钥匙（解开后生成）")]
    [Tooltip("把做好的钥匙 PickupItem 拖成 Prefab 后，把 Prefab 拖到这里")]
    public PickupItem keyPickupPrefab;
    [Tooltip("钥匙生成位置 = 密码机位置 + 这个偏移（默认脚下偏下一点）")]
    public Vector2 spawnOffset = new Vector2(0f, -1.2f);

    [Header("交互设置")]
    public string promptText = "按F 使用密码机";
    public KeyCode interactKey = KeyCode.F;
    public float interactRange = 1.5f;
    public float promptFontSize = 28f;

    [Header("音效（可选）")]
    public AudioSource successSound; // 解锁成功音
    public AudioSource errorSound;   // 密码错误音

    private bool solved = false;          // 是否已解开（解开就不再响应）
    private bool isOpen = false;          // 输入界面是否打开
    private string worldKey;              // 世界进度表钥匙（读档还原"解没解开"）
    private List<int> input = new List<int>(); // 玩家当前输入的数字

    private PixelGridMovement player;     // 玩家（自动查找，算距离用）
    private GameObject promptUI;          // "按F 使用"提示（同 DoorLock 套路）
    private Text promptUIText;

    // ---- 输入界面 UI（代码生成，风格对齐 InventoryUI） ----
    private GameObject canvasObj;
    private GameObject panelObj;
    private Text inputText;      // 输入显示区："3 1 _ _"
    private Text feedbackText;   // 反馈行："密码错误，已重置" / 空白
    private Image flashImage;    // 错误红闪（全屏红图）
    private float flashAlpha = 0f;

    private void Start()
    {
        player = FindObjectOfType<PixelGridMovement>();
        if (correctCode == null || correctCode.Length == 0)
            Debug.LogWarning("[密码机] correctCode 没填，这台机器永远解不开！", gameObject);
        CreatePromptUI();
        CreateMachineUI();

        // 读档自查：世界进度表记录过"这台机解开了" → 恢复成已解 + 补生成钥匙（若还没被捡走）
        worldKey = WorldState.KeyFor("Password", this);
        if (WorldState.GetBool(worldKey))
        {
            solved = true;
            HidePrompt();
            SpawnKeyIfNeeded();
        }
    }

    private void Update()
    {
        // 1. 错误红闪：有强度就每帧衰减淡出（不受界面开关影响）
        if (flashAlpha > 0f)
        {
            flashAlpha = Mathf.Max(0f, flashAlpha - 2.5f * Time.unscaledDeltaTime); // unscaled：暂停时也正常闪
            if (flashImage != null)
            {
                Color c = flashImage.color;
                c.a = flashAlpha;
                flashImage.color = c;
            }
        }

        if (solved) return; // 解开后的机器是摆设

        // 2. 输入界面开着：只处理输入按键
        if (isOpen)
        {
            HandleMachineInput();
            return;
        }

        // 3. 平时：距离检测 + 按 F 打开（门是实体墙用不了触发器，机器同理用距离）
        if (player == null) return;
        bool inRange = Vector2.Distance(transform.position, player.transform.position) <= interactRange;

        if (inRange)
        {
            ShowPrompt(promptText, Color.white);
            if (Input.GetKeyDown(interactKey))
                OpenMachine();
        }
        else
        {
            HidePrompt();
        }
    }

    // ======== 输入界面 ========

    private void OpenMachine()
    {
        isOpen = true;
        Time.timeScale = 0f;             // 暂停游戏（和 InventoryUI 同款做法）
        input.Clear();
        if (feedbackText != null) feedbackText.text = "";
        RefreshInputText();
        if (panelObj != null) panelObj.SetActive(true);
    }

    private void CloseMachine()
    {
        isOpen = false;
        Time.timeScale = 1f;             // ⚠️ 必须恢复，漏了会假死机
        if (panelObj != null) panelObj.SetActive(false);
    }

    /// <summary> 输入界面打开期间的按键分发：数字输入/退格/回车确认/Esc 关闭 </summary>
    private void HandleMachineInput()
    {
        // Esc 关闭（不消耗进度，下次打开重新输）
        if (Input.GetKeyDown(KeyCode.Escape))
        {
            CloseMachine();
            return;
        }

        // 回车确认（主键盘和小键盘的回车都支持）
        if (Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.KeypadEnter))
        {
            CheckCode();
            return;
        }

        // 退格：删掉上一个数字
        if (Input.GetKeyDown(KeyCode.Backspace) && input.Count > 0)
        {
            input.RemoveAt(input.Count - 1);
            RefreshInputText();
            return;
        }

        // 数字 1-4 输入（主键盘 + 小键盘都支持，位数满了忽略）
        if (input.Count < correctCode.Length)
        {
            for (int n = 1; n <= 4; n++)
            {
                bool pressed = Input.GetKeyDown((KeyCode)((int)KeyCode.Alpha1 + n - 1))
                            || Input.GetKeyDown((KeyCode)((int)KeyCode.Keypad1 + n - 1));
                if (pressed)
                {
                    input.Add(n);
                    RefreshInputText();
                    break; // 一帧只吃一个键
                }
            }
        }
    }

    /// <summary> 对照密码：全对 → 发钥匙；否则 → 红闪 + 清空重输 </summary>
    private void CheckCode()
    {
        // 没输满就按回车：不惩罚，只提示
        if (input.Count < correctCode.Length)
        {
            if (feedbackText != null) feedbackText.text = "还没输完（" + input.Count + "/" + correctCode.Length + "）";
            return;
        }

        bool allMatch = true;
        for (int i = 0; i < correctCode.Length; i++)
        {
            if (input[i] != correctCode[i]) { allMatch = false; break; }
        }

        if (allMatch)
        {
            SolveSuccess();
        }
        else
        {
            // 错误：屏幕红闪 + 清空重输（可无限重试）
            flashAlpha = 0.45f;
            if (errorSound != null) errorSound.Play();
            if (feedbackText != null) feedbackText.text = "密码错误，已重置";
            input.Clear();
            RefreshInputText();
        }
    }

    /// <summary> 解锁成功：关界面 + 在机器旁生成钥匙 </summary>
    private void SolveSuccess()
    {
        solved = true;
        WorldState.Set(worldKey, 1); // 世界进度表登记"密码机已解开"
        CloseMachine();
        HidePrompt();
        if (successSound != null) successSound.Play();
        SpawnKeyIfNeeded();
    }

    /// <summary> 在机器旁生成钥匙（解开时 / 读档还原解开态时都走这里） </summary>
    private void SpawnKeyIfNeeded()
    {
        if (keyPickupPrefab != null)
        {
            Vector2 spawnPos = (Vector2)transform.position + spawnOffset;
            PickupItem key = Instantiate(keyPickupPrefab, spawnPos, Quaternion.identity);
            // 防刷钥匙：如果这把钥匙以前被捡过（SaveSystem 已记录），PickupItem.Start
            // 会自动 Destroy 这个克隆体——现有防复活机制天然生效，不用我们额外写
            Debug.Log("[密码机] 已生成钥匙：" + key.itemID, gameObject);
        }
        else
        {
            Debug.LogWarning("[密码机] 解开了但 keyPickupPrefab 没拖，钥匙没生成！", gameObject);
        }
    }

    /// <summary> 刷新输入显示区：已输的显示数字，没输的显示下划线 </summary>
    private void RefreshInputText()
    {
        if (inputText == null) return;
        string s = "";
        for (int i = 0; i < correctCode.Length; i++)
        {
            if (i > 0) s += "  ";
            s += (i < input.Count) ? input[i].ToString() : "_";
        }
        inputText.text = s;
    }

    // ======== 提示 UI（纯代码生成，同 DoorLock 套路） ========

    private void CreatePromptUI()
    {
        promptUI = new GameObject("MachinePrompt", typeof(RectTransform)); // UI 物体必须带 RectTransform
        Canvas canvas = promptUI.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 100;
        UIScale.Setup(promptUI);
        promptUI.AddComponent<GraphicRaycaster>();

        GameObject bg = new GameObject("BG", typeof(RectTransform));
        bg.transform.SetParent(promptUI.transform, false);
        Image bgImg = bg.AddComponent<Image>();
        bgImg.color = new Color(0, 0, 0, 0.5f);
        RectTransform bgRect = bgImg.GetComponent<RectTransform>();
        bgRect.anchorMin = new Vector2(0.5f, 0f);
        bgRect.anchorMax = new Vector2(0.5f, 0f);
        bgRect.pivot = new Vector2(0.5f, 0.5f);
        bgRect.anchoredPosition = new Vector2(0, 50f);
        bgRect.sizeDelta = new Vector2(360, 60);

        GameObject textGO = new GameObject("Text", typeof(RectTransform));
        textGO.transform.SetParent(bg.transform, false);
        promptUIText = textGO.AddComponent<Text>();
        promptUIText.fontSize = (int)promptFontSize;
        promptUIText.alignment = TextAnchor.MiddleCenter;
        promptUIText.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        RectTransform textRect = promptUIText.GetComponent<RectTransform>();
        textRect.anchorMin = Vector2.zero;
        textRect.anchorMax = Vector2.one;
        textRect.offsetMin = Vector2.zero;
        textRect.offsetMax = Vector2.zero;

        promptUI.SetActive(false);
    }

    private void ShowPrompt(string text, Color color)
    {
        if (promptUI == null) return;
        if (promptUIText != null)
        {
            promptUIText.text = text;
            promptUIText.color = color;
        }
        promptUI.SetActive(true);
    }

    private void HidePrompt()
    {
        if (promptUI != null) promptUI.SetActive(false);
    }

    // ======== 输入界面 UI（风格对齐 InventoryUI：暗底白字黄标题） ========

    private void CreateMachineUI()
    {
        // 最外层 Canvas（排序 305：在背包 200 之上、红闪 320 之下）
        canvasObj = new GameObject("MachineCanvas", typeof(RectTransform)); // 必须带 RectTransform
        Canvas canvas = canvasObj.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 305;
        UIScale.Setup(canvasObj);
        canvasObj.AddComponent<GraphicRaycaster>();

        // 输入面板总根（全屏容器，开/关只切换它）
        panelObj = new GameObject("MachinePanelRoot", typeof(RectTransform));
        panelObj.transform.SetParent(canvasObj.transform, false);
        RectTransform rootRect = panelObj.GetComponent<RectTransform>();
        rootRect.anchorMin = Vector2.zero;
        rootRect.anchorMax = Vector2.one;
        rootRect.offsetMin = Vector2.zero;
        rootRect.offsetMax = Vector2.zero;

        // 全屏半透明黑底
        GameObject bg = new GameObject("DimBG", typeof(RectTransform));
        bg.transform.SetParent(panelObj.transform, false);
        Image bgImg = bg.AddComponent<Image>();
        bgImg.color = new Color(0f, 0f, 0f, 0.7f);
        RectTransform bgRect = bgImg.GetComponent<RectTransform>();
        bgRect.anchorMin = Vector2.zero;
        bgRect.anchorMax = Vector2.one;
        bgRect.offsetMin = Vector2.zero;
        bgRect.offsetMax = Vector2.zero;

        // 中央密码机面板
        GameObject panel = new GameObject("Panel", typeof(RectTransform));
        panel.transform.SetParent(panelObj.transform, false);
        Image panelImg = panel.AddComponent<Image>();
        panelImg.color = new Color(0.1f, 0.1f, 0.1f, 0.92f); // 和背包操作菜单同色
        RectTransform panelRect = panelImg.GetComponent<RectTransform>();
        panelRect.anchorMin = new Vector2(0.5f, 0.5f);
        panelRect.anchorMax = new Vector2(0.5f, 0.5f);
        panelRect.pivot = new Vector2(0.5f, 0.5f);
        panelRect.anchoredPosition = Vector2.zero;
        panelRect.sizeDelta = new Vector2(520f, 300f);

        // 标题（金黄）
        GameObject title = new GameObject("Title", typeof(RectTransform));
        title.transform.SetParent(panel.transform, false);
        Text titleText = title.AddComponent<Text>();
        titleText.text = "=== 密 码 机 ===";
        titleText.fontSize = 24;
        titleText.color = Color.yellow;
        titleText.alignment = TextAnchor.MiddleCenter;
        titleText.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        RectTransform titleRect = titleText.GetComponent<RectTransform>();
        titleRect.anchorMin = new Vector2(0f, 1f);
        titleRect.anchorMax = new Vector2(1f, 1f);
        titleRect.pivot = new Vector2(0.5f, 1f);
        titleRect.anchoredPosition = new Vector2(0f, -12f);
        titleRect.sizeDelta = new Vector2(500f, 40f);

        // 输入显示区（大号白字："3 1 _ _"）
        GameObject inputGO = new GameObject("InputDisplay", typeof(RectTransform));
        inputGO.transform.SetParent(panel.transform, false);
        inputText = inputGO.AddComponent<Text>();
        inputText.fontSize = 40;
        inputText.color = Color.white;
        inputText.alignment = TextAnchor.MiddleCenter;
        inputText.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        RectTransform inputRect = inputText.GetComponent<RectTransform>();
        inputRect.anchorMin = new Vector2(0.5f, 0.5f);
        inputRect.anchorMax = new Vector2(0.5f, 0.5f);
        inputRect.pivot = new Vector2(0.5f, 0.5f);
        inputRect.anchoredPosition = new Vector2(0f, 20f);
        inputRect.sizeDelta = new Vector2(480f, 70f);

        // 反馈行（红字提示"密码错误"之类）
        GameObject feedback = new GameObject("Feedback", typeof(RectTransform));
        feedback.transform.SetParent(panel.transform, false);
        feedbackText = feedback.AddComponent<Text>();
        feedbackText.fontSize = 20;
        feedbackText.color = new Color(1f, 0.45f, 0.4f);
        feedbackText.alignment = TextAnchor.MiddleCenter;
        feedbackText.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        RectTransform feedbackRect = feedbackText.GetComponent<RectTransform>();
        feedbackRect.anchorMin = new Vector2(0.5f, 0.5f);
        feedbackRect.anchorMax = new Vector2(0.5f, 0.5f);
        feedbackRect.pivot = new Vector2(0.5f, 0.5f);
        feedbackRect.anchoredPosition = new Vector2(0f, -40f);
        feedbackRect.sizeDelta = new Vector2(480f, 36f);

        // 底部操作提示（灰字）
        GameObject hint = new GameObject("Hint", typeof(RectTransform));
        hint.transform.SetParent(panel.transform, false);
        Text hintText = hint.AddComponent<Text>();
        hintText.text = "按 1-4 依次输入 | 回车 确认 | 退格 删除 | Esc 关闭";
        hintText.fontSize = 16;
        hintText.color = Color.gray;
        hintText.alignment = TextAnchor.MiddleCenter;
        hintText.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        RectTransform hintRect = hintText.GetComponent<RectTransform>();
        hintRect.anchorMin = new Vector2(0f, 0f);
        hintRect.anchorMax = new Vector2(1f, 0f);
        hintRect.pivot = new Vector2(0.5f, 0f);
        hintRect.anchoredPosition = new Vector2(0f, 12f);
        hintRect.sizeDelta = new Vector2(500f, 30f);

        panelObj.SetActive(false); // 初始隐藏

        // 错误红闪（独立 Canvas，盖在输入界面上：320 > 305）
        GameObject flashCanvas = new GameObject("MachineFlashCanvas", typeof(RectTransform));
        Canvas fc = flashCanvas.AddComponent<Canvas>();
        fc.renderMode = RenderMode.ScreenSpaceOverlay;
        fc.sortingOrder = 320;
        UIScale.Setup(flashCanvas);
        flashCanvas.AddComponent<GraphicRaycaster>();

        GameObject flashGO = new GameObject("Flash", typeof(RectTransform));
        flashGO.transform.SetParent(flashCanvas.transform, false);
        flashImage = flashGO.AddComponent<Image>();
        flashImage.color = new Color(1f, 0.15f, 0.1f, 0f); // 初始全透明
        flashImage.raycastTarget = false; // 不拦截按键
        RectTransform flashRect = flashImage.GetComponent<RectTransform>();
        flashRect.anchorMin = Vector2.zero;
        flashRect.anchorMax = Vector2.one;
        flashRect.offsetMin = Vector2.zero;
        flashRect.offsetMax = Vector2.zero;
    }

    private void OnDestroy()
    {
        // 兜底：场景卸载时如果界面还开着，恢复时间流速
        if (isOpen) Time.timeScale = 1f;
        if (promptUI != null) Destroy(promptUI);
        if (canvasObj != null) Destroy(canvasObj);
    }
}
