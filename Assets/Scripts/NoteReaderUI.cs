using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 纸条阅读面板（静态 Open 懒生成，PasswordPad 同款零搭建）：
/// 白纸黑字（小泽点名：纸片米白底 0.96/0.95/0.9，文字黑色）+ 标题居中 + 正文左上对齐。
/// 暗色全屏底压背、timeScale=0、openedFrame 防同帧自关——全套惯例照抄。
///
/// 关闭行为（新）：
/// - 由背包（纸条列表）打开时（传了 owner）：
///     · Tab 合上 → 连背包一起关，回游戏；
///     · E / F / Esc 合上 → 交还给背包，回到【纸条列表】（不直接回游戏）。
/// - 无 owner（从物品网格直接打开的旧路径）→ 保持旧行为：合上 = 联动关背包回游戏。
/// </summary>
public class NoteReaderUI : MonoBehaviour
{
    private static NoteReaderUI instance;

    /// <summary> 阅读面板当前是否开着 </summary>
    public static bool IsOpen { get; private set; }

    private int openedFrame = -1;      // 开面板当帧的 E 不算关闭指令（防同帧自关，柜子同款守卫）
    private float timeScaleBefore = 1f; // 打开前的 timeScale（关闭时还原，背包暂停不受影响）
    private InventoryUI owner;          // 打开它的背包（非空 = 关闭时交还给背包处理）

    // ---- UI 引用 ----
    private GameObject canvasObj;
    private Text titleText;
    private Text bodyText;

    /// <summary> 打开阅读面板（旧路径：无 owner，合上 = 联动关背包回游戏） </summary>
    public static void Open(string title, string text)
    {
        Open(title, text, null);
    }

    /// <summary> 打开阅读面板；owner = 打开它的背包（非空时：E/F/Esc 合上 → 回纸条列表；Tab → 连背包一起关） </summary>
    public static void Open(string title, string text, InventoryUI owner)
    {
        if (instance == null)
        {
            GameObject go = new GameObject("NoteReaderUIRoot");
            instance = go.AddComponent<NoteReaderUI>();
        }
        instance.Show(title, text, owner);
    }

    private void Show(string title, string text, InventoryUI owner)
    {
        this.owner = owner;
        timeScaleBefore = Time.timeScale; // 记下打开前流速（背包暂停=0，关闭时还原，背包不被误恢复）
        IsOpen = true;
        openedFrame = Time.frameCount;    // 开面板当帧的 E 不算关闭指令（防同帧自关）
        Time.timeScale = 0f;

        if (canvasObj == null) CreateUI();

        if (titleText != null) titleText.text = string.IsNullOrEmpty(title) ? "纸条" : title;
        if (bodyText != null) bodyText.text = text;
        canvasObj.SetActive(true);
    }

    // ======== 界面构建（白纸黑字） ========

    private void CreateUI()
    {
        canvasObj = new GameObject("NoteReaderCanvas");
        Canvas canvas = canvasObj.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 320; // 压过背包 200 / 柜子 250 / 密码面板 305
        CanvasScaler cs = canvasObj.AddComponent<CanvasScaler>(); cs.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize; cs.referenceResolution = new Vector2(1920f, 1080f); cs.matchWidthOrHeight = 1f;

        // 全屏暗底（压暗游戏画面，突出纸条）
        GameObject bg = new GameObject("DimBG", typeof(RectTransform));
        bg.transform.SetParent(canvasObj.transform, false);
        Image bgImg = bg.AddComponent<Image>();
        bgImg.color = new Color(0f, 0f, 0f, 0.6f);
        RectTransform bgRect = bgImg.GetComponent<RectTransform>();
        bgRect.anchorMin = Vector2.zero;
        bgRect.anchorMax = Vector2.one;
        bgRect.offsetMin = Vector2.zero;
        bgRect.offsetMax = Vector2.zero;

        // 纸片（米白底）
        GameObject paper = new GameObject("Paper", typeof(RectTransform));
        paper.transform.SetParent(canvasObj.transform, false);
        Image paperImg = paper.AddComponent<Image>();
        paperImg.color = new Color(0.96f, 0.95f, 0.9f, 0.95f); // 米白纸片（小泽点名白色系）
        RectTransform paperRect = paperImg.GetComponent<RectTransform>();
        paperRect.anchorMin = new Vector2(0.5f, 0.5f);
        paperRect.anchorMax = new Vector2(0.5f, 0.5f);
        paperRect.pivot = new Vector2(0.5f, 0.5f);
        paperRect.anchoredPosition = Vector2.zero;
        paperRect.sizeDelta = new Vector2(560f, 640f);

        // 纸片边框（暗一圈，模拟纸片边缘/阴影）
        GameObject border = new GameObject("Border", typeof(RectTransform));
        border.transform.SetParent(paper.transform, false);
        Image borderImg = border.AddComponent<Image>();
        borderImg.color = new Color(0.55f, 0.52f, 0.45f, 0.9f); // 纸边灰褐
        RectTransform borderRect = borderImg.GetComponent<RectTransform>();
        borderRect.anchorMin = Vector2.zero;
        borderRect.anchorMax = Vector2.one;
        borderRect.offsetMin = new Vector2(-4f, -4f);
        borderRect.offsetMax = new Vector2(4f, 4f);
        borderImg.raycastTarget = false;
        borderRect.SetSiblingIndex(0); // 垫到纸片下面只露边

        // 标题（黑字加粗，顶部居中）
        GameObject titleGO = new GameObject("Title", typeof(RectTransform));
        titleGO.transform.SetParent(paper.transform, false);
        titleText = titleGO.AddComponent<Text>();
        titleText.fontSize = 26;
        titleText.fontStyle = FontStyle.Bold;
        titleText.color = new Color(0.12f, 0.12f, 0.12f); // 黑字
        titleText.alignment = TextAnchor.MiddleCenter;
        titleText.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        titleText.raycastTarget = false;
        RectTransform titleRect = titleText.GetComponent<RectTransform>();
        titleRect.anchorMin = new Vector2(0.5f, 1f);
        titleRect.anchorMax = new Vector2(0.5f, 1f);
        titleRect.pivot = new Vector2(0.5f, 1f);
        titleRect.anchoredPosition = new Vector2(0f, -34f);
        titleRect.sizeDelta = new Vector2(480f, 44f);

        // 分隔横线（纸片上的一条折痕感）
        GameObject line = new GameObject("Line", typeof(RectTransform));
        line.transform.SetParent(paper.transform, false);
        Image lineImg = line.AddComponent<Image>();
        lineImg.color = new Color(0.5f, 0.5f, 0.5f, 0.5f);
        lineImg.raycastTarget = false;
        RectTransform lineRect = lineImg.GetComponent<RectTransform>();
        lineRect.anchorMin = new Vector2(0.5f, 1f);
        lineRect.anchorMax = new Vector2(0.5f, 1f);
        lineRect.pivot = new Vector2(0.5f, 1f);
        lineRect.anchoredPosition = new Vector2(0f, -88f);
        lineRect.sizeDelta = new Vector2(460f, 2f);

        // 正文（黑字，左上对齐，多行）
        GameObject bodyGO = new GameObject("Body", typeof(RectTransform));
        bodyGO.transform.SetParent(paper.transform, false);
        bodyText = bodyGO.AddComponent<Text>();
        bodyText.fontSize = 19;
        bodyText.color = new Color(0.1f, 0.1f, 0.1f); // 黑字
        bodyText.alignment = TextAnchor.UpperLeft;
        bodyText.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        bodyText.raycastTarget = false;
        RectTransform bodyRect = bodyText.GetComponent<RectTransform>();
        bodyRect.anchorMin = new Vector2(0.5f, 0.5f);
        bodyRect.anchorMax = new Vector2(0.5f, 0.5f);
        bodyRect.pivot = new Vector2(0.5f, 0.5f);
        bodyRect.anchoredPosition = new Vector2(0f, -40f);
        bodyRect.sizeDelta = new Vector2(490f, 420f);

        // 底部操作提示（灰字）
        GameObject hintGO = new GameObject("Hint", typeof(RectTransform));
        hintGO.transform.SetParent(paper.transform, false);
        Text hintText = hintGO.AddComponent<Text>();
        hintText.text = "Esc / F / E 合上纸条（回到纸条列表）";
        hintText.fontSize = 15;
        hintText.color = new Color(0.45f, 0.45f, 0.45f);
        hintText.alignment = TextAnchor.MiddleCenter;
        hintText.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        hintText.raycastTarget = false;
        RectTransform hintRect = hintText.GetComponent<RectTransform>();
        hintRect.anchorMin = new Vector2(0.5f, 0f);
        hintRect.anchorMax = new Vector2(0.5f, 0f);
        hintRect.pivot = new Vector2(0.5f, 0.5f);
        hintRect.anchoredPosition = new Vector2(0f, 20f);
        hintRect.sizeDelta = new Vector2(500f, 26f);
    }

    // ======== 关闭 ========

    private void Update()
    {
        if (!IsOpen) return;

        // Tab = 全部关掉回游戏；Esc/F/E = 交还给打开者（背包 → 回到纸条列表）
        bool tabPressed = Input.GetKeyDown(KeyCode.Tab);
        bool backPressed = Input.GetKeyDown(KeyCode.Escape)
            || Input.GetKeyDown(KeyCode.F)
            || (Input.GetKeyDown(KeyCode.E) && Time.frameCount != openedFrame); // 开面板当帧的 E 不算

        if (tabPressed) Close(true);
        else if (backPressed) Close(false);
    }

    /// <summary> closeEverything=true：连背包一起关回游戏；false：交还给 owner（回纸条列表） </summary>
    private void Close(bool closeEverything)
    {
        IsOpen = false;
        if (canvasObj != null) canvasObj.SetActive(false);

        InventoryUI ownerInv = owner;
        owner = null;

        // 交还给背包：回到纸条列表（背包仍开着 → 保持暂停流速）
        if (ownerInv != null && !closeEverything)
        {
            Time.timeScale = timeScaleBefore;
            ownerInv.ReturnToNoteList();
            return;
        }

        // 全部关掉 → 回游戏
        Time.timeScale = 1f;
        if (ownerInv != null)
        {
            ownerInv.CloseFromNoteReader();
            return;
        }

        // 无 owner（旧路径：从物品网格直接打开）→ 保持旧行为：联动关背包
        InventoryUI inv = FindObjectOfType<InventoryUI>();
        if (inv != null) inv.CloseExternal();
    }

    private void OnDestroy()
    {
        if (IsOpen) { IsOpen = false; Time.timeScale = timeScaleBefore; }
        if (canvasObj != null) Destroy(canvasObj);
    }
}
