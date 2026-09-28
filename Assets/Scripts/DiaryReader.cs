using UnityEngine;
using UnityEngine.UI;
using System.Collections.Generic;

/// <summary>
/// 单条日记的数据（struct：按排障知识库要求，本脚本不用嵌套类）。
/// itemID 必须和场景里日记 PickupItem 的 itemID 一字不差。
/// </summary>
[System.Serializable]
public struct DiaryEntry
{
    [Tooltip("日记物品的 itemID（和 PickupItem 上填的完全一致）")]
    public string itemID;
    [Tooltip("面板顶部标题，如：研究员日志 · 第 3 页")]
    public string title;
    [TextArea(6, 12)]
    [Tooltip("日记正文（约 170 字）")]
    public string content;
}

/// <summary>
/// 日记阅读面板：玩家捡到指定 itemID 的物品时，自动弹出全屏阅读面板。
/// 挂到场景里任意空物体上（建议命名 DiaryReader，每个场景一个）。
/// 纯代码生成 UI，风格与 InventoryUI 一致；不修改任何现有脚本。
/// </summary>
public class DiaryReader : MonoBehaviour
{
    [Header("日记内容表（捡到对应 itemID 就弹面板）")]
    public DiaryEntry[] entries;

    [Header("关闭按键")]
    public KeyCode closeKeyA = KeyCode.Space;
    public KeyCode closeKeyB = KeyCode.Escape;

    private Inventory inventory;                     // 场景里玩家的背包（自动查找）
    private Dictionary<string, DiaryEntry> entryMap; // itemID → 日记内容
    private Dictionary<string, bool> alreadyShown;   // 每条日记是否已弹过（防重复弹）

    private bool isOpen = false;                     // 面板当前是否打开

    // ---- UI 引用（Start 时纯代码生成，不用拖任何东西） ----
    private GameObject canvasObj;   // 最外层 Canvas
    private GameObject panelObj;    // 阅读面板总根（开/关就切换它）
    private Text titleText;         // 标题
    private Text contentText;       // 正文

    private void Start()
    {
        // 1. 找到背包（和 TriggerEvent 自动找 DialogueManager 同一个套路）
        inventory = FindObjectOfType<Inventory>();

        // 2. 把 Inspector 里的日记表装进 Dictionary（struct + Dictionary 模式）
        entryMap = new Dictionary<string, DiaryEntry>();
        alreadyShown = new Dictionary<string, bool>();
        if (entries != null)
        {
            foreach (DiaryEntry e in entries)
            {
                if (string.IsNullOrEmpty(e.itemID))
                {
                    Debug.LogWarning("[日记] 有一条日记没填 itemID，已跳过", gameObject);
                    continue;
                }
                entryMap[e.itemID] = e;
                // 读档/切场景时背包里已经有这本日记 → 标记"已弹过"，
                // 避免一进场景就莫名其妙弹面板
                alreadyShown[e.itemID] = (inventory != null && inventory.HasItem(e.itemID));
            }
        }

        // 3. 生成 UI（默认隐藏）
        CreateUI();
    }

    private void Update()
    {
        // 面板开着时：只处理关闭按键
        if (isOpen)
        {
            if (Input.GetKeyDown(closeKeyA) || Input.GetKeyDown(closeKeyB))
                ClosePanel();
            return;
        }

        // 面板没开时：轮询背包，看是否"刚"捡到日记
        if (inventory == null) return;
        foreach (KeyValuePair<string, DiaryEntry> kv in entryMap)
        {
            if (alreadyShown[kv.Key]) continue;      // 弹过的不再弹
            if (inventory.HasItem(kv.Key))
            {
                alreadyShown[kv.Key] = true;
                OpenPanel(kv.Value);
                break;                               // 一次只弹一本
            }
        }
    }

    /// <summary> 打开阅读面板：暂停时间 + 填文字 + 显示 </summary>
    private void OpenPanel(DiaryEntry entry)
    {
        isOpen = true;
        Time.timeScale = 0f;   // 暂停游戏（和 InventoryUI 同款做法），敌人也一起停

        // 先填字再显示，避免闪一帧上一次的旧内容
        if (titleText != null) titleText.text = entry.title;
        if (contentText != null) contentText.text = entry.content;
        if (panelObj != null) panelObj.SetActive(true);
    }

    /// <summary> 关闭阅读面板：恢复时间 + 隐藏 </summary>
    private void ClosePanel()
    {
        isOpen = false;
        Time.timeScale = 1f;   // ⚠️ 必须恢复，漏了这行游戏会永远暂停（假死机）
        if (panelObj != null) panelObj.SetActive(false);
    }

    private void OnDestroy()
    {
        // 场景卸载时如果面板还开着，把时间恢复回来，防止下个场景卡在暂停
        if (isOpen) Time.timeScale = 1f;
        if (canvasObj != null) Destroy(canvasObj);
    }

    // ======== UI 生成（风格对齐 InventoryUI：暗底白字黄标题、无素材） ========

    private void CreateUI()
    {
        // 最外层 Canvas（排序 300：拾取提示 260 之上、死亡界面 400 之下）
        // ⚠️ 坑：new GameObject 做 UI 必须带 typeof(RectTransform)，否则锚点全是错的
        canvasObj = new GameObject("DiaryCanvas", typeof(RectTransform));
        Canvas canvas = canvasObj.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 300;
        UIScale.Setup(canvasObj);
        canvasObj.AddComponent<GraphicRaycaster>();

        // 面板总根：全屏大小、默认隐藏，开/关只切换它
        panelObj = new GameObject("DiaryPanelRoot", typeof(RectTransform));
        WorldInteractionBlocker.Attach(panelObj);
        panelObj.transform.SetParent(canvasObj.transform, false);
        RectTransform rootRect = panelObj.GetComponent<RectTransform>();
        rootRect.anchorMin = Vector2.zero;
        rootRect.anchorMax = Vector2.one;
        rootRect.offsetMin = Vector2.zero;
        rootRect.offsetMax = Vector2.zero;

        // 全屏半透明黑底（聚拢视线，和背包打开时一个感觉）
        GameObject bg = new GameObject("DimBG", typeof(RectTransform));
        bg.transform.SetParent(panelObj.transform, false);
        Image bgImg = bg.AddComponent<Image>();
        bgImg.color = new Color(0f, 0f, 0f, 0.7f);
        RectTransform bgRect = bgImg.GetComponent<RectTransform>();
        bgRect.anchorMin = Vector2.zero;
        bgRect.anchorMax = Vector2.one;
        bgRect.offsetMin = Vector2.zero;
        bgRect.offsetMax = Vector2.zero;

        // 中央"旧纸"面板（近黑微暖色）
        GameObject paper = new GameObject("Paper", typeof(RectTransform));
        paper.transform.SetParent(panelObj.transform, false);
        Image paperImg = paper.AddComponent<Image>();
        paperImg.color = new Color(0.08f, 0.07f, 0.06f, 0.96f);
        RectTransform paperRect = paperImg.GetComponent<RectTransform>();
        paperRect.anchorMin = new Vector2(0.5f, 0.5f);
        paperRect.anchorMax = new Vector2(0.5f, 0.5f);
        paperRect.pivot = new Vector2(0.5f, 0.5f);
        paperRect.anchoredPosition = Vector2.zero;
        paperRect.sizeDelta = new Vector2(760f, 480f);

        // 标题（金黄，和背包合成面板标题同风格）
        GameObject title = new GameObject("Title", typeof(RectTransform));
        title.transform.SetParent(paper.transform, false);
        titleText = title.AddComponent<Text>();
        titleText.fontSize = 26;
        titleText.color = new Color(1f, 0.85f, 0.3f);
        titleText.alignment = TextAnchor.MiddleCenter;
        titleText.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        RectTransform titleRect = titleText.GetComponent<RectTransform>();
        titleRect.anchorMin = new Vector2(0f, 1f);
        titleRect.anchorMax = new Vector2(1f, 1f);
        titleRect.pivot = new Vector2(0.5f, 1f);
        titleRect.anchoredPosition = new Vector2(0f, -24f);
        titleRect.sizeDelta = new Vector2(720f, 44f);

        // 正文（白字，超宽自动换行，170 字放得下）
        GameObject content = new GameObject("Content", typeof(RectTransform));
        content.transform.SetParent(paper.transform, false);
        contentText = content.AddComponent<Text>();
        contentText.fontSize = 22;
        contentText.color = Color.white;
        contentText.alignment = TextAnchor.UpperLeft;
        contentText.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        contentText.horizontalOverflow = HorizontalWrapMode.Wrap; // 自动换行
        contentText.verticalOverflow = VerticalWrapMode.Overflow; // 超高不裁切（保险）
        RectTransform contentRect = contentText.GetComponent<RectTransform>();
        contentRect.anchorMin = new Vector2(0.5f, 0.5f);
        contentRect.anchorMax = new Vector2(0.5f, 0.5f);
        contentRect.pivot = new Vector2(0.5f, 0.5f);
        contentRect.anchoredPosition = new Vector2(0f, -20f); // 标题下方留空隙
        contentRect.sizeDelta = new Vector2(680f, 340f);

        // 底部操作提示（灰字）
        GameObject hint = new GameObject("Hint", typeof(RectTransform));
        hint.transform.SetParent(paper.transform, false);
        Text hintText = hint.AddComponent<Text>();
        hintText.text = "空格 / Esc 关闭";
        hintText.fontSize = 18;
        hintText.color = Color.gray;
        hintText.alignment = TextAnchor.MiddleCenter;
        hintText.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        RectTransform hintRect = hintText.GetComponent<RectTransform>();
        hintRect.anchorMin = new Vector2(0f, 0f);
        hintRect.anchorMax = new Vector2(1f, 0f);
        hintRect.pivot = new Vector2(0.5f, 0f);
        hintRect.anchoredPosition = new Vector2(0f, 14f);
        hintRect.sizeDelta = new Vector2(720f, 30f);

        panelObj.SetActive(false); // 初始隐藏
    }
}
