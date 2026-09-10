using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;

/// <summary>
/// 箱子/柜子取物界面（独立于玩家 TAB 的全新界面，由 Container 按 F 打开，运行时自动生成不用手动挂）：
/// - 单列布局：显示箱内物品列表，鼠标点击物品 = 取出到自己背包（只能拿不能放——往箱里塞东西是开发者在
///   Container 的 Inspector "Starter Items" 里预填，不是玩家操作）；
/// - 箱子空 → 中央大字"空空如也"；
/// - 也支持键盘：W/S 选择、空格取出、F/TAB/ESC 关闭；
/// - F 关闭时打开时间流速恢复（打开时 timeScale=0 暂停惯例，和玩家背包一致）；
/// - 风格照 InventoryUI：暗底 + 黄标题 + 白字 + LegacyRuntime.ttf。
/// </summary>
public class ContainerUI : MonoBehaviour
{
    private static ContainerUI instance;

    /// <summary> 界面当前是否开着（Container 开门前查它，防止"按 F 关闭的同帧又被重新打开"的帧序 bug） </summary>
    public static bool IsOpen { get; private set; }

    private Container box;                 // 当前打开的箱子
    private Inventory playerInventory;     // 玩家背包（取出的东西进这里）
    private GameObject canvasObj;
    private Transform itemGrid;            // 箱内物品列表容器
    private Text emptyText;                // "空空如也"
    private List<GameObject> slotObjs = new List<GameObject>();

    private int selectedIndex = 0;         // 键盘选择（W/S 上下、空格取出）

    private const int COLUMNS = 4;
    private const float SLOT_W = 80f, SLOT_H = 100f, PAD = 10f;

    /// <summary> 打开某只箱子（Container 调用；第一次会自动创建界面物体） </summary>
    public static void Open(Container container)
    {
        if (instance == null)
        {
            GameObject go = new GameObject("ContainerUIRoot");
            instance = go.AddComponent<ContainerUI>();
        }
        instance.Show(container);
    }

    private void Show(Container container)
    {
        box = container;

        // 找玩家背包（玩家身上挂 Inventory 的那个）
        if (playerInventory == null)
        {
            GameObject p = GameObject.FindGameObjectWithTag("Player");
            if (p != null) playerInventory = p.GetComponent<Inventory>();
        }
        if (playerInventory == null)
        {
            Debug.LogWarning("[箱子界面] 找不到玩家背包（Player 身上没有 Inventory），打不开");
            return;
        }

        EnsureEventSystem(); // 鼠标点击需要 EventSystem（场景里没有就自动补）

        IsOpen = true;
        Time.timeScale = 0f; // 暂停惯例（和玩家背包一致）
        selectedIndex = 0;
        CreateUI();
    }

    /// <summary> 鼠标点击需要 EventSystem，场景里没有就自动生成一个 </summary>
    private static void EnsureEventSystem()
    {
        if (FindObjectOfType<EventSystem>() != null) return;
        GameObject es = new GameObject("EventSystem");
        es.AddComponent<EventSystem>();
        es.AddComponent<StandaloneInputModule>();
    }

    // ======== 界面构建（风格照 InventoryUI） ========

    private void CreateUI()
    {
        if (canvasObj != null) Destroy(canvasObj);
        slotObjs.Clear();

        canvasObj = new GameObject("ContainerCanvas");
        Canvas canvas = canvasObj.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 250; // 压过玩家背包（200）
        canvasObj.AddComponent<CanvasScaler>().uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        canvasObj.AddComponent<GraphicRaycaster>();

        // 半透明背景
        GameObject bg = new GameObject("BG");
        bg.transform.SetParent(canvasObj.transform, false);
        Image bgImg = bg.AddComponent<Image>();
        bgImg.color = new Color(0, 0, 0, 0.7f);
        RectTransform bgRect = bgImg.GetComponent<RectTransform>();
        bgRect.anchorMin = Vector2.zero;
        bgRect.anchorMax = Vector2.one;
        bgRect.offsetMin = Vector2.zero;
        bgRect.offsetMax = Vector2.zero;

        // 标题（黄字，按箱子类型显示）
        GameObject title = new GameObject("Title");
        title.transform.SetParent(canvasObj.transform, false);
        Text titleText = title.AddComponent<Text>();
        titleText.text = box.type == Container.ContainerType.Cabinet ? "=== 柜 子 ===" : "=== 箱 子 ===";
        titleText.fontSize = 26;
        titleText.color = Color.yellow;
        titleText.alignment = TextAnchor.MiddleCenter;
        titleText.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        RectTransform titleRect = titleText.GetComponent<RectTransform>();
        titleRect.anchorMin = new Vector2(0.5f, 1f);
        titleRect.anchorMax = new Vector2(0.5f, 1f);
        titleRect.pivot = new Vector2(0.5f, 1f);
        titleRect.anchoredPosition = new Vector2(0, -20f);
        titleRect.sizeDelta = new Vector2(600, 50);

        // 单列网格：箱内物品列表（居中）
        GameObject grid = new GameObject("ItemGrid");
        grid.transform.SetParent(canvasObj.transform, false);
        GridLayoutGroup g = grid.AddComponent<GridLayoutGroup>();
        g.cellSize = new Vector2(SLOT_W, SLOT_H);
        g.spacing = new Vector2(PAD, PAD);
        g.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
        g.constraintCount = COLUMNS;
        itemGrid = grid.transform;
        RectTransform gRect = grid.GetComponent<RectTransform>();
        gRect.anchorMin = new Vector2(0.5f, 0.5f);
        gRect.anchorMax = new Vector2(0.5f, 0.5f);
        gRect.anchoredPosition = Vector2.zero;

        // "空空如也"（箱子空时显示在中央）
        GameObject empty = new GameObject("Empty");
        empty.transform.SetParent(canvasObj.transform, false);
        emptyText = empty.AddComponent<Text>();
        emptyText.text = "空空如也";
        emptyText.fontSize = 30;
        emptyText.color = Color.gray;
        emptyText.alignment = TextAnchor.MiddleCenter;
        emptyText.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        RectTransform emptyRect = emptyText.GetComponent<RectTransform>();
        emptyRect.anchorMin = new Vector2(0.5f, 0.5f);
        emptyRect.anchorMax = new Vector2(0.5f, 0.5f);
        emptyRect.sizeDelta = new Vector2(360, 60);

        // 操作提示
        GameObject tip = new GameObject("Tip");
        tip.transform.SetParent(canvasObj.transform, false);
        Text tipText = tip.AddComponent<Text>();
        tipText.text = "点击物品取出 | W/S 选择 空格取出 | F/TAB/ESC 关闭";
        tipText.fontSize = 18;
        tipText.color = Color.white;
        tipText.alignment = TextAnchor.MiddleCenter;
        tipText.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        RectTransform tipRect = tipText.GetComponent<RectTransform>();
        tipRect.anchorMin = new Vector2(0.5f, 0f);
        tipRect.anchorMax = new Vector2(0.5f, 0f);
        tipRect.pivot = new Vector2(0.5f, 0.5f);
        tipRect.anchoredPosition = new Vector2(0, 30f);
        tipRect.sizeDelta = new Vector2(700, 40);

        RefreshAll();
    }

    // ======== 刷新（全量重建，取出后调用——最稳不踩索引错位） ========

    private void RefreshAll()
    {
        // 清掉旧格子
        foreach (GameObject s in slotObjs) Destroy(s);
        slotObjs.Clear();

        // 箱内物品列表（点击 → 取出到背包）
        for (int i = 0; i < box.contents.Count; i++)
            slotObjs.Add(CreateSlot(i, box.contents[i]));

        // 空空如也：箱子空时显示
        if (emptyText != null) emptyText.gameObject.SetActive(box.contents.Count == 0);

        HighlightSelection();
    }

    /// <summary> 造一个物品格：底板 + 图标 + 名字 + 点击取出（照 InventoryUI 的格子套路） </summary>
    private GameObject CreateSlot(int index, InventoryItem item)
    {
        GameObject slot = new GameObject("Slot_" + index);
        slot.transform.SetParent(itemGrid, false);
        Image slotBg = slot.AddComponent<Image>();
        slotBg.color = new Color(0.2f, 0.2f, 0.2f, 0.8f);

        if (item != null)
        {
            // 图标
            if (item.icon != null)
            {
                GameObject iconGO = new GameObject("Icon");
                iconGO.transform.SetParent(slot.transform, false);
                Image iconImg = iconGO.AddComponent<Image>();
                iconImg.sprite = item.icon;
                iconImg.preserveAspect = true;
                iconImg.raycastTarget = false; // 点击穿透到底板按钮
                RectTransform iconRect = iconGO.GetComponent<RectTransform>();
                iconRect.anchorMin = new Vector2(0.1f, 0.3f);
                iconRect.anchorMax = new Vector2(0.9f, 0.9f);
                iconRect.offsetMin = Vector2.zero;
                iconRect.offsetMax = Vector2.zero;
            }

            // 名字
            GameObject nameGO = new GameObject("Name");
            nameGO.transform.SetParent(slot.transform, false);
            Text nameText = nameGO.AddComponent<Text>();
            nameText.text = item.itemName;
            nameText.fontSize = 12;
            nameText.color = Color.white;
            nameText.alignment = TextAnchor.LowerCenter;
            nameText.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            nameText.raycastTarget = false; // 点击穿透到底板按钮
            RectTransform nameRect = nameGO.GetComponent<RectTransform>();
            nameRect.anchorMin = new Vector2(0f, 0f);
            nameRect.anchorMax = new Vector2(1f, 0.3f);
            nameRect.offsetMin = Vector2.zero;
            nameRect.offsetMax = Vector2.zero;
        }

        // 点击取出：从箱子拿走这件，进自己背包
        int capturedIndex = index; // 闭包捕获（循环变量陷阱防呆）
        Button btn = slot.AddComponent<Button>();
        btn.onClick.AddListener(() => Take(capturedIndex));

        return slot;
    }

    /// <summary> 取出第 index 件：从箱子移除 → 塞进玩家背包 </summary>
    private void Take(int index)
    {
        // 先从箱子取出（含取出音效），再塞进背包列表
        InventoryItem item = box.TakeOut(index);
        if (item == null) return;
        playerInventory.items.Add(item);

        // 跨场景快照同步（背包加了东西，存档快照跟着刷新）
        playerInventory.RefreshSaveState();
        selectedIndex = 0;
        RefreshAll(); // 拿完就空 → 自动显示"空空如也"
    }

    // ======== 键盘操作（W/S 选 空格取出；F/TAB/ESC 关闭） ========

    private void Update()
    {
        if (box == null) return;

        if (Input.GetKeyDown(KeyCode.F) || Input.GetKeyDown(KeyCode.Tab) || Input.GetKeyDown(KeyCode.Escape))
        {
            Close();
            return;
        }

        int count = box.contents.Count;
        if (count > 0)
        {
            if (Input.GetKeyDown(KeyCode.W) || Input.GetKeyDown(KeyCode.UpArrow))
            {
                selectedIndex = Mathf.Max(0, selectedIndex - COLUMNS);
                HighlightSelection();
            }
            if (Input.GetKeyDown(KeyCode.S) || Input.GetKeyDown(KeyCode.DownArrow))
            {
                selectedIndex = Mathf.Min(count - 1, selectedIndex + COLUMNS);
                HighlightSelection();
            }
            if (Input.GetKeyDown(KeyCode.Space)) Take(selectedIndex);
        }
    }

    /// <summary> 键盘选中高亮（蓝底 = 当前选中） </summary>
    private void HighlightSelection()
    {
        for (int i = 0; i < slotObjs.Count; i++)
        {
            Image img = slotObjs[i].GetComponent<Image>();
            if (img == null) continue;
            img.color = (i == selectedIndex) ? new Color(0.3f, 0.6f, 1f, 0.8f) : new Color(0.2f, 0.2f, 0.2f, 0.8f);
        }
    }

    /// <summary> 关闭：恢复时间流速、拆界面 </summary>
    private void Close()
    {
        IsOpen = false;
        Time.timeScale = 1f;
        box = null;
        if (canvasObj != null) Destroy(canvasObj);
    }

    private void OnDestroy()
    {
        IsOpen = false;
        if (canvasObj != null) { Time.timeScale = 1f; Destroy(canvasObj); }
    }
}
