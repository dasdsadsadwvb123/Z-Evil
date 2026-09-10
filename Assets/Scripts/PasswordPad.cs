using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 通用数字密码面板（静态 Open 调用，运行时自动生成不用手动挂——档案室密码机的通用化版本）：
/// 数字键 0-9 输入（主键盘+小键盘）、回车确认、退格删除、Esc 关闭；
/// 输错 → 全屏红闪 + 面板红字"密码错误，已重置" + onError 回调（调用方播音效），可无限重试；
/// 输对 → 面板关闭 + onSuccess 回调（调用方决定开什么：开柜/开门/给东西）。
/// 与 PasswordMachine（档案室取档机）互不影响——那台的"1-4 按键+发钥匙"逻辑原样保留。
/// 风格对齐 InventoryUI：暗底 0.1/0.1/0.1/0.92 + 黄标题 + 白字 + LegacyRuntime.ttf + timeScale=0 暂停。
/// </summary>
public class PasswordPad : MonoBehaviour
{
    private static PasswordPad instance;

    /// <summary> 密码面板当前是否开着（调用方防重入：开着时不要再按 F 重复弹） </summary>
    public static bool IsOpen { get; private set; }

    // 回调：onSuccess = 输对（面板已关）；onError = 输错确认（面板没关，已清空重输）；onCancel = Esc 关闭（面板已关）
    private System.Action onSuccess;
    private System.Action onError;
    private System.Action onCancel;

    private string correctPassword = "";
    private string input = "";

    // ---- UI 引用（代码生成） ----
    private GameObject canvasObj;
    private GameObject panelObj;
    private Text inputText;
    private Text feedbackText;
    private Image flashImage;
    private float flashAlpha = 0f;

    /// <summary> 打开密码面板（没有实例就自动生成一个常驻的） </summary>
    public static void Open(string password, System.Action onSuccess, System.Action onError, System.Action onCancel)
    {
        if (instance == null)
        {
            GameObject go = new GameObject("PasswordPadRoot");
            instance = go.AddComponent<PasswordPad>();
        }
        instance.Show(password, onSuccess, onError, onCancel);
    }

    private void Show(string password, System.Action onOk, System.Action onErr, System.Action onQuit)
    {
        correctPassword = password ?? "";
        onSuccess = onOk;
        onError = onErr;
        onCancel = onQuit;
        input = "";

        if (canvasObj == null) CreateUI();
        if (feedbackText != null) feedbackText.text = "";
        RefreshInputText();
        panelObj.SetActive(true);

        IsOpen = true;
        Time.timeScale = 0f; // 暂停惯例（和背包/柜子一致）
    }

    /// <summary> 关闭面板（恢复时间流速；whoCancelled = 是否 Esc 取消） </summary>
    private void Close(bool whoCancelled)
    {
        IsOpen = false;
        Time.timeScale = 1f; // ⚠️ 必须恢复，漏了会假死机（PasswordMachine 同款教训）
        panelObj.SetActive(false);
        if (whoCancelled && onCancel != null) onCancel();
    }

    private void Update()
    {
        // 错误红闪：有强度就每帧衰减淡出（unscaled：暂停时也正常闪，PasswordMachine 同款）
        if (flashAlpha > 0f)
        {
            flashAlpha = Mathf.Max(0f, flashAlpha - 2.5f * Time.unscaledDeltaTime);
            if (flashImage != null)
            {
                Color c = flashImage.color;
                c.a = flashAlpha;
                flashImage.color = c;
            }
        }

        if (!IsOpen) return;

        // Esc 关闭（取消：不触发成功/失败回调）
        if (Input.GetKeyDown(KeyCode.Escape))
        {
            Close(true);
            return;
        }

        // 回车确认（主键盘和小键盘都支持）
        if (Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.KeypadEnter))
        {
            CheckCode();
            return;
        }

        // 退格：删掉上一个数字
        if (Input.GetKeyDown(KeyCode.Backspace) && input.Length > 0)
        {
            input = input.Substring(0, input.Length - 1);
            RefreshInputText();
            return;
        }

        // 数字 0-9 输入（主键盘 + 小键盘都支持；密码几位就最多输几位）
        if (input.Length < correctPassword.Length)
        {
            for (int n = 0; n <= 9; n++)
            {
                bool pressed = Input.GetKeyDown((KeyCode)((int)KeyCode.Alpha0 + n))
                            || Input.GetKeyDown((KeyCode)((int)KeyCode.Keypad0 + n));
                if (pressed)
                {
                    input += n.ToString();
                    RefreshInputText();
                    break; // 一帧只吃一个键
                }
            }
        }
    }

    /// <summary> 对照密码：全对 → onSuccess（面板已关）；否则 → 红闪 + onError + 清空重输（可无限重试） </summary>
    private void CheckCode()
    {
        // 没输满就按回车：不惩罚，只提示
        if (input.Length < correctPassword.Length)
        {
            if (feedbackText != null) feedbackText.text = "还没输完（" + input.Length + "/" + correctPassword.Length + "）";
            return;
        }

        if (input == correctPassword)
        {
            Close(false);
            if (onSuccess != null) onSuccess();
        }
        else
        {
            flashAlpha = 0.45f; // 屏幕红闪
            if (feedbackText != null) feedbackText.text = "密码错误，已重置";
            input = "";
            RefreshInputText();
            if (onError != null) onError(); // 调用方播输错音效
        }
    }

    /// <summary> 刷新输入显示区：已输的显示数字，没输的显示下划线 </summary>
    private void RefreshInputText()
    {
        if (inputText == null) return;
        string s = "";
        for (int i = 0; i < correctPassword.Length; i++)
        {
            if (i > 0) s += "  ";
            s += (i < input.Length) ? input.Substring(i, 1) : "_";
        }
        inputText.text = s;
    }

    // ======== UI（代码生成，风格对齐 InventoryUI / PasswordMachine） ========

    private void CreateUI()
    {
        // 最外层 Canvas（排序 305：在背包 200 / 柜子 250 之上、红闪 320 之下）
        canvasObj = new GameObject("PasswordPadCanvas", typeof(RectTransform));
        Canvas canvas = canvasObj.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 305;
        canvasObj.AddComponent<CanvasScaler>().uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        canvasObj.AddComponent<GraphicRaycaster>();

        // 面板总根（全屏容器，开/关只切换它）
        panelObj = new GameObject("PadPanelRoot", typeof(RectTransform));
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

        // 中央面板
        GameObject panel = new GameObject("Panel", typeof(RectTransform));
        panel.transform.SetParent(panelObj.transform, false);
        Image panelImg = panel.AddComponent<Image>();
        panelImg.color = new Color(0.1f, 0.1f, 0.1f, 0.92f);
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
        titleText.text = "=== 密 码 锁 ===";
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

        // 输入显示区（大号白字："1 9 _ _"）
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

        // 反馈行（红字）
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
        hintText.text = "数字键 0-9 输入 | 回车 确认 | 退格 删除 | Esc 关闭";
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

        // 错误红闪（独立 Canvas，盖在面板上：320 > 305；PasswordMachine 同款）
        GameObject flashCanvas = new GameObject("PadFlashCanvas", typeof(RectTransform));
        Canvas fc = flashCanvas.AddComponent<Canvas>();
        fc.renderMode = RenderMode.ScreenSpaceOverlay;
        fc.sortingOrder = 320;
        flashCanvas.AddComponent<CanvasScaler>().uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        flashCanvas.AddComponent<GraphicRaycaster>();

        GameObject flashGO = new GameObject("Flash", typeof(RectTransform));
        flashGO.transform.SetParent(flashCanvas.transform, false);
        flashImage = flashGO.AddComponent<Image>();
        flashImage.color = new Color(1f, 0.15f, 0.1f, 0f);
        flashImage.raycastTarget = false;
        RectTransform flashRect = flashImage.GetComponent<RectTransform>();
        flashRect.anchorMin = Vector2.zero;
        flashRect.anchorMax = Vector2.one;
        flashRect.offsetMin = Vector2.zero;
        flashRect.offsetMax = Vector2.zero;
    }

    private void OnDestroy()
    {
        // 兜底：场景卸载时如果面板还开着，恢复时间流速（PasswordMachine 同款教训）
        if (IsOpen) Time.timeScale = 1f;
        if (canvasObj != null) Destroy(canvasObj);
    }
}
