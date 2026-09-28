using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;

/// <summary>
/// 箱子/柜子取物界面（独立于玩家 TAB 的全新界面，由 Container 按 F 打开，运行时自动生成不用手动挂）：
/// - 单列布局：显示箱内物品列表，鼠标点击物品 = 取出到自己背包（只能拿不能放——往箱里塞东西是开发者在
///   Container 的 Inspector "Starter Items" 里预填，不是玩家操作）；
/// - 箱子空 → 中央大字"空空如也"；
/// - 也支持键盘：W/S 选择、F 取出（空格保留兼容）、TAB/ESC 关闭；
/// - TAB/ESC 关闭时恢复时间流速（打开时 timeScale=0 暂停惯例，和玩家背包一致）；
/// - 风格照 InventoryUI：暗底 + 黄标题 + 白字 + LegacyRuntime.ttf。
/// </summary>
public class ContainerUI : MonoBehaviour
{
    private static ContainerUI instance;

    /// <summary> 界面当前是否开着（Container 开门前查它，防止"按 F 关闭的同帧又被重新打开"的帧序 bug） </summary>
    public static bool IsOpen { get; private set; }

    /// <summary> 关闭发生在哪一帧（Time.frameCount）：Container 对关闭当帧的 F 视而不见，杜绝"关了又被同帧重开" </summary>
    public static int LastClosedFrame { get; private set; } = -1;

    /// <summary> 界面在哪一帧被打开：开柜当帧的 F 不算关闭指令（同一次按键不能既开又关） </summary>
    private int openedFrame = -1;

    private Container box;                 // 当前打开的箱子
    private Inventory playerInventory;     // 玩家背包（取出的东西进这里）
    private GameObject canvasObj;
    private Transform itemGrid;            // 箱内物品列表容器
    private Text emptyText;                // "空空如也"
    private List<GameObject> slotObjs = new List<GameObject>();

    private int selectedIndex = 0;         // 键盘选择（W/S 上下、空格取出）

    private const int COLUMNS = 4;
    private const float SLOT_W = 80f, SLOT_H = 100f, PAD = 10f;

    [Header("尺寸绑定（与背包 InventoryUI 联动）")]
    [Tooltip("柜子/箱子界面倍率：0 = 自动跟随背包（取 InventoryUI.inventoryScale）；>0 = 手动指定")]
    public float panelScale = 0f;

    // 实际使用的倍率：Show() 时解析（panelScale>0 用它，否则取背包的 inventoryScale，取不到用 1.3）
    private float uiScale = 1f;

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
        openedFrame = Time.frameCount; // 开柜当帧记下：这一帧的 F 不能当关闭指令（否则开→同帧自关=永远只能开一次）
        Time.timeScale = 0f; // 暂停惯例（和玩家背包一致）
        selectedIndex = 0;
        uiScale = ResolveScale(); // 解析倍率（默认跟随背包，做到"一个参数驱动两处"）
        CreateUI();
    }

    /// <summary> 解析界面倍率：panelScale>0 用手动值；否则自动跟随背包的 inventoryScale；再取不到用 1.3 </summary>
    private float ResolveScale()
    {
        if (panelScale > 0f) return Mathf.Max(0.1f, panelScale);

        InventoryUI bag = FindObjectOfType<InventoryUI>(); // 玩家身上那个（TAB 背包）
        if (bag != null && bag.inventoryScale > 0f) return Mathf.Max(0.1f, bag.inventoryScale);

        return 1.3f; // 兜底：和 InventoryUI.inventoryScale 默认值保持一致
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
        WorldInteractionBlocker.Attach(canvasObj);
        Canvas canvas = canvasObj.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 250; // 压过玩家背包（200）
        UIScale.Setup(canvasObj);
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
        titleText.fontSize = Mathf.RoundToInt(26f * uiScale); // 字号随倍率放大，和背包一致
        titleText.color = Color.yellow;
        titleText.alignment = TextAnchor.MiddleCenter;
        titleText.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        RectTransform titleRect = titleText.GetComponent<RectTransform>();
        titleRect.anchorMin = new Vector2(0.5f, 1f);
        titleRect.anchorMax = new Vector2(0.5f, 1f);
        titleRect.pivot = new Vector2(0.5f, 1f);
        titleRect.anchoredPosition = new Vector2(0, -20f);
        titleRect.sizeDelta = new Vector2(600, 50);

        // 面板容器（深色底，和背包面板同色/同列宽/同格尺寸）+ 单列网格：箱内物品列表
        float slotW = SLOT_W * uiScale;
        float slotH = SLOT_H * uiScale;
        float pad = PAD * uiScale;

        GameObject grid = new GameObject("ItemGrid");
        grid.transform.SetParent(canvasObj.transform, false);
        Image gridBg = grid.AddComponent<Image>();                 // 给网格加底板 → 看起来就是一块"面板"
        gridBg.color = new Color(0.06f, 0.06f, 0.08f, 0.9f);       // 与背包 GridViewport 同色
        gridBg.raycastTarget = false;
        GridLayoutGroup g = grid.AddComponent<GridLayoutGroup>();
        g.cellSize = new Vector2(slotW, slotH);
        g.spacing = new Vector2(pad, pad);
        g.padding = new RectOffset((int)pad, (int)pad, (int)pad, (int)pad);
        g.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
        g.constraintCount = COLUMNS;
        g.childAlignment = TextAnchor.UpperCenter;
        itemGrid = grid.transform;
        RectTransform gRect = grid.GetComponent<RectTransform>();
        gRect.anchorMin = new Vector2(0.5f, 0.5f);
        gRect.anchorMax = new Vector2(0.5f, 0.5f);
        gRect.pivot = new Vector2(0.5f, 0.5f);
        gRect.anchoredPosition = Vector2.zero;
        ResizePanel(); // 按物品行数把面板调到刚好装下（列宽与背包一致）

        // "空空如也"（箱子空时显示在中央）
        GameObject empty = new GameObject("Empty");
        empty.transform.SetParent(canvasObj.transform, false);
        emptyText = empty.AddComponent<Text>();
        emptyText.text = "空空如也";
        emptyText.fontSize = Mathf.RoundToInt(30f * uiScale);
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
        tipText.text = "W/S / ↑↓ 选择 · F / 空格 取出\n鼠标点击也可取出 · Tab / Esc 关闭";
        tipText.fontSize = Mathf.RoundToInt(18f * uiScale);
        tipText.color = Color.white;
        tipText.alignment = TextAnchor.MiddleCenter;
        tipText.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        RectTransform tipRect = tipText.GetComponent<RectTransform>();
        tipRect.anchorMin = new Vector2(0.5f, 0f);
        tipRect.anchorMax = new Vector2(0.5f, 0f);
        tipRect.pivot = new Vector2(0.5f, 0.5f);
        tipRect.anchoredPosition = new Vector2(0, 48f);
        tipRect.sizeDelta = new Vector2(700f * uiScale, 76f);

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

        ResizePanel(); // 物品数变了 → 面板尺寸跟着缩/涨（拿空后不会留一块空底）
        HighlightSelection();
    }

    /// <summary> 按当前物品数把面板（ItemGrid 底板）调到刚好装下所有格子（列宽/格尺寸与背包一致） </summary>
    private void ResizePanel()
    {
        RectTransform gRect = itemGrid as RectTransform;
        if (gRect == null) return;

        int count = box != null ? box.contents.Count : 0;
        int rows = Mathf.Max(1, Mathf.CeilToInt(count / (float)COLUMNS));
        float slotW = SLOT_W * uiScale, slotH = SLOT_H * uiScale, pad = PAD * uiScale;

        float w = COLUMNS * slotW + (COLUMNS - 1) * pad + 2f * pad;
        float h = rows * slotH + (rows - 1) * pad + 2f * pad;
        gRect.sizeDelta = new Vector2(w, h);
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
            nameText.fontSize = Mathf.RoundToInt(12f * uiScale); // 物品名字号也随倍率放大，和背包一致
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

    /// <summary> 取出第 index 件：弹药 → 直接进备用弹药池（不占背包格）；其他 → 塞进玩家背包 </summary>
    private void Take(int index)
    {
        // ---- 弹药防呆：先"看"一眼（不移除），配置无效就直接拦下 ----
        // （AmmoType 里 None=0；若小泽已配好的 Starter Items 因新加字段反序列化成默认 None/0，
        //  以前会静默 AddReserveAmmo(None,0) 加 0 发。这里改成明确警告 + 什么都不做。）
        if (index >= 0 && index < box.contents.Count)
        {
            InventoryItem peek = box.contents[index];
            if (peek != null && peek.itemType == ItemType.Ammo
                && (peek.ammoType == AmmoType.None || peek.ammoAmount <= 0))
            {
                Debug.LogWarning("[箱子] '" + box.name + "' 的 Starter Items 第 " + (index + 1)
                    + " 件弹药没配 Ammo Type / Ammo Amount（当前 " + peek.ammoType + " / " + peek.ammoAmount
                    + "）——请在 Inspector 里补上。已拦下：不消耗、不发 0、不污染弹药池。", box);
                return; // 保留该件在箱内，什么都不改（无 _Taken_ 副作用）
            }
        }

        // 先从箱子取出（含取出音效 + 记录"已取走"计数），再按类型分流
        InventoryItem item = box.TakeOut(index);
        if (item == null)
        {
            RefreshAll(); // 取出失败（索引越界等）→ 也刷新一下，避免界面与数据不同步
            return;
        }

        // 弹药：和地上弹药包一样，直接加进备用弹药池，不占背包格
        // （AddReserveAmmo 内部会发 toast："手枪弹药 +10" 这类带数量的提示）
        if (item.itemType == ItemType.Ammo)
        {
            playerInventory.AddReserveAmmo(item.ammoType, item.ammoAmount);
        }
        else
        {
            // 其他类型（草药/钥匙/宝石/枪/纸条等）照旧进背包
            playerInventory.items.Add(item);
        }

        // 跨场景快照同步（背包/弹药变了，存档快照跟着刷新）
        playerInventory.RefreshSaveState();
        selectedIndex = 0;
        RefreshAll(); // 拿完就空 → 自动显示"空空如也"
    }

    // ======== 键盘操作（W/S 选；F 取出、空格兼容；TAB/ESC 关闭） ========

    private void Update()
    {
        if (box == null) return;

        // ---- 关闭/返回：TAB（新增）/ ESC ----
        // TAB 加同帧守卫：开箱当帧的 TAB 不算关闭（与取物键 F 同一套防抖逻辑，防连按误触）
        if (Input.GetKeyDown(KeyCode.Tab) && Time.frameCount != openedFrame)
        {
            Close();
            return;
        }
        if (Input.GetKeyDown(KeyCode.Escape))
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

            // F = 取出当前选中（主推键）。
            // ⚠️ 同帧防呆：开箱用的也是 F → 开箱当帧的这次 F 是"打开箱子"那一下，绝不算取出，
            //    否则一按开箱键就把第一件东西取走了。用 openedFrame 守卫挡掉。
            if (Input.GetKeyDown(KeyCode.F) && Time.frameCount != openedFrame)
            {
                Take(selectedIndex);
                return;
            }

            // 空格 = 取出（保留旧键，兼容老习惯）
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
        LastClosedFrame = Time.frameCount; // 标记：这帧关的，Container 这帧别再开门
        Time.timeScale = 1f;
        box = null;
        if (canvasObj != null) Destroy(canvasObj);
    }

    private void OnDestroy()
    {
        IsOpen = false;
        LastClosedFrame = Time.frameCount;
        if (canvasObj != null) { Time.timeScale = 1f; Destroy(canvasObj); }
    }
}
