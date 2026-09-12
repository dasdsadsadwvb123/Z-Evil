using UnityEngine;
using UnityEngine.UI;
using System.Collections.Generic;

/// <summary>
/// 背包 UI：Tab 打开 → WASD 选物品 → 空格操作。
/// 草药：空格弹【操作菜单】（使用/合成/取消），合成面板只显示材料够的配方。
/// 纸条：Q 切换到【纸条列表】视图（数据来自 NoteJournal）→ WASD 选 → E 阅读（NoteReaderUI 白纸黑字）。
/// 保留原有：枪装备、宝石镶嵌、Tab 关闭、时间暂停等逻辑。
/// </summary>
public class InventoryUI : MonoBehaviour
{
    [Header("按键")]
    public KeyCode toggleKey = KeyCode.Tab;
    public KeyCode useKey = KeyCode.Space;
    [Tooltip("物品列表 / 纸条列表 视图切换键")]
    public KeyCode journalKey = KeyCode.Q;

    [Header("外观")]
    public int columns = 4;
    public float slotSize = 80f;
    public float padding = 10f;

    [Header("背包尺寸回调（放大背包面板与物品格，不改全局 UIScale）")]
    [Tooltip("整体倍率：1=原始大小，1.2~1.4=放大（推荐 1.3）。只影响背包这一个 UI，不动 UIScale")]
    public float inventoryScale = 1.3f;

    [Header("物品网格滚动")]
    [Tooltip("可视区最多显示几行（列数=columns）。超出的行靠滚动 / W·S 选择自动跟随查看")]
    public int visibleRows = 4;
    [Tooltip("鼠标滚轮滚动速度（像素/滚轮格）")]
    public float wheelScrollSpeed = 60f;
    [Tooltip("是否显示极简滚动条（内容超出可视区时才出现）")]
    public bool showScrollbar = true;

    [Header("纸条列表视图")]
    [Tooltip("纸条列表中部的大字提示")]
    public string noteViewHint = "按 E 查看";
    [Tooltip("大字提示字号（占满中部空白感，一般比列表文字大 2~3 倍）")]
    public int noteHintFontSize = 64;
    [Tooltip("大字提示颜色（带暖意的浅黄，暗底对比强）")]
    public Color noteHintColor = new Color(1f, 0.92f, 0.6f, 1f);
    [Tooltip("大字提示轻微呼吸闪烁（更醒目）")]
    public bool noteHintPulse = true;

    private Inventory inventory;
    private GameObject canvasObj;
    private GameObject bagPanel;        // 背包统一暗色面板（左信息栏 + 右物品网格都装在里面 → 不再压游戏画面）
    private GameObject slotContainer;   // 物品网格「视口」：RectMask2D 裁剪（面板右半栏）
    private GameObject slotContent;     // 网格「内容容器」：格子都挂这里，滚动位移只作用于它
    private RectTransform contentRect;  // slotContent 的 RectTransform（缓存，省掉每次 GetComponent）
    private RectTransform viewportRect; // 视口的 RectTransform
    private GameObject scrollbarTrack;  // 极简滚动条：轨道
    private GameObject scrollbarHandle; // 极简滚动条：滑块
    private GameObject infoBoard;       // 物品信息区底板（米白色）
    private Text infoNameText;          // 物品名称（黑字，标题感）
    private Text infoDescText;          // 物品描述（黑字，自动换行）
    private float cellW, cellH, cellSpacing;  // 运行时算出的格子宽 / 高 / 间距（已乘 inventoryScale）
    private float contentHeight = 0f;         // 内容总高（按当前物品数算）
    private float viewportHeight = 0f;        // 视口高（= 可见行数那一屏）
    private float scrollY = 0f;               // 当前滚动位移（0=顶部，越大越往下看）
    private float uiScale = 1f;               // 实际生效的尺寸倍率（= inventoryScale），供格子内文字复用时用
    private int selectedIndex = 0;
    private bool isOpen = false;
    private List<GameObject> slotObjs = new List<GameObject>();

    // ======== 状态机：物品网格 → 操作菜单 → 合成面板 → 纸条列表 ========
    private enum UIState { Grid, Menu, Combine, Notes }
    private UIState uiState = UIState.Grid;

    private GameObject menuPanel;                 // 操作菜单（使用/合成/取消）
    private List<Text> menuOptionTexts = new List<Text>();
    private List<string> menuOptions = new List<string>();
    private int menuSelected = 0;

    private GameObject combinePanel;              // 合成面板
    private List<Text> combineOptionTexts = new List<Text>();
    private List<Inventory.CombineRecipe> combineList = new List<Inventory.CombineRecipe>();
    private int combineSelected = 0;

    private string pendingMessage = null;         // 提示消息（使用/合成结果）
    private float messageUntil = 0f;              // 消息显示到什么时候

    // ======== 纸条列表视图（Q 切换；数据来自 NoteJournal） ========
    private GameObject notesPanel;                // 纸条列表面板
    private List<Text> noteLineTexts = new List<Text>(); // 预建的标题行
    private Text notesEmptyText;                  // "还没有捡到任何纸条"
    private Text noteHintText;                    // 中部大字提示（"按 E 查看"）
    private Text notesOverflowText;               // 纸条过多（>12 张）的省略提示
    private int noteSelected = 0;                 // 当前选中第几张纸条
    private int suppressFrame = -1;               // 本帧余波：阅读面板刚合上，忽略本帧按键（防同帧又弹面板）

    /// <summary> 界面在哪一帧被打开：外部 OpenExternal 弹出当帧，Tab 不作关闭指令（防同帧自关，柜子同款守卫） </summary>
    private int openedFrame = -1;

    // ======== 健康指示（生化2式：颜色代替数字，仅背包可见） ========
    private HealthSystem healthSystem;
    private Image healthCircleImg;   // 圆形色块（按血量变色）
    private Text healthStateText;    // 状态字（健康/注意/危险/濒死）

    private void Start()
    {
        inventory = GetComponent<Inventory>();
        healthSystem = GetComponent<HealthSystem>();
    }

    private void Update()
    {
        // 阅读面板刚合上的那一帧：忽略本帧按键（防同帧又把面板/背包弹回来）
        if (Time.frameCount == suppressFrame) return;

        // 纸条阅读面板开着时，按键全部交给它自己处理（含 Tab 联动关背包）
        if (NoteReaderUI.IsOpen) return;

        // 箱子界面开着时，背包完全不响应（TAB 归箱子界面消费，用来关箱子）
        if (ContainerUI.IsOpen) return;

        // Tab 开关背包：
        //  - 外部弹出（OpenExternal）当帧的 Tab 不作关闭指令——防"同帧自关"（柜子 openedFrame 同款守卫）
        //  - 箱子界面刚关闭的当帧也不吃 Tab——否则"用 TAB 关箱子"这一下会顺带把背包打开（想开背包请再按一次）
        if (Input.GetKeyDown(toggleKey) && Time.frameCount != openedFrame
            && Time.frameCount != ContainerUI.LastClosedFrame)
        {
            if (isOpen) Close();
            else Open();
        }

        if (!isOpen) return;

        // 按当前界面状态分发按键处理
        switch (uiState)
        {
            case UIState.Grid:    HandleGridInput();    break;
            case UIState.Menu:    HandleMenuInput();    break;
            case UIState.Combine: HandleCombineInput(); break;
            case UIState.Notes:   HandleNotesInput();   break;
        }
    }

    // ======== 状态1：背包网格（原有 WASD 选择 + 空格操作） ========

    private void HandleGridInput()
    {
        // Q：切到纸条列表（背包空也能切）
        if (Input.GetKeyDown(journalKey))
        {
            SwitchToNotes();
            return;
        }

        int cols = columns;
        int total = inventory.items.Count;
        if (total == 0) return;

        // WASD 选择（原有逻辑）
        if (Input.GetKeyDown(KeyCode.W) || Input.GetKeyDown(KeyCode.UpArrow))
        {
            int target = selectedIndex - cols;
            if (target >= 0) selectedIndex = target;
            UpdateSelection();
        }
        if (Input.GetKeyDown(KeyCode.S) || Input.GetKeyDown(KeyCode.DownArrow))
        {
            int target = selectedIndex + cols;
            if (target < total) selectedIndex = target;
            UpdateSelection();
        }
        if (Input.GetKeyDown(KeyCode.A) || Input.GetKeyDown(KeyCode.LeftArrow))
        {
            if (selectedIndex > 0) selectedIndex--;
            UpdateSelection();
        }
        if (Input.GetKeyDown(KeyCode.D) || Input.GetKeyDown(KeyCode.RightArrow))
        {
            if (selectedIndex < total - 1) selectedIndex++;
            UpdateSelection();
        }

        // 鼠标滚轮：手动滚动物品网格（滚轮上 → 看更上面的物品；夹紧不越界）
        float wheel = Input.mouseScrollDelta.y;
        if (Mathf.Abs(wheel) > 0.0001f)
        {
            float maxScroll = Mathf.Max(0f, contentHeight - viewportHeight);
            scrollY = Mathf.Clamp(scrollY - wheel * wheelScrollSpeed, 0f, maxScroll);
            ApplyScroll();
        }

        // E：使用宝石（靠近宝石铁门时镶嵌）或阅读纸条（白纸黑字面板，纸条不消耗可反复读）
        if (Input.GetKeyDown(KeyCode.E))
        {
            InventoryItem selected = inventory.GetItemAt(selectedIndex);
            if (selected != null)
            {
                // 1) 宝石 → 镶嵌（原逻辑一行未动；镶嵌成功 toast 由 GemDoor 发，失败在这里提示）
                if (GemDoor.IsGemItem(selected.itemID))
                {
                    bool ok = GemDoor.TryEmbed(selected.itemID);
                    if (ok) RefreshSlots();                    // 宝石已从背包移除，刷新格子
                    else ShowInfoMessage("要靠近铁门才能镶嵌"); // 不在门边/已镶过 → 提示且宝石保留
                }
                // 2) 纸条 → 打开白纸黑字阅读面板（NotePaper 静态注册表查文本；不消耗）
                else if (NotePaper.TryGetText(selected.itemID) != null)
                {
                    NoteReaderUI.Open(NotePaper.TryGetTitle(selected.itemID), NotePaper.TryGetText(selected.itemID));
                }
            }
        }

        // 空格：按物品类型处理
        if (Input.GetKeyDown(useKey))
        {
            InventoryItem item = inventory.GetItemAt(selectedIndex);
            if (item == null) return;

            // 枪 → 直接装备（原有逻辑）
            if (item.itemType == ItemType.Gun)
            {
                inventory.EquipAt(selectedIndex);
                Close();
                return;
            }

            // 草药 → 弹操作菜单（使用/合成/取消）
            if (item.itemType == ItemType.Herb)
            {
                OpenMenuFor(item);
                return;
            }

            // 其他物品（钥匙/宝石/说明书）→ 显示描述
            ShowInfoMessage("<b>" + item.itemName + "</b>\n" + item.description);
        }
    }

    // ======== 状态4：纸条列表（Q 从物品网格切入；WASD 选、E 阅读） ========

    private void HandleNotesInput()
    {
        // 中部大字轻微呼吸闪烁（用 unscaledTime，暂停中也动）
        if (noteHintPulse && noteHintText != null && noteHintText.gameObject.activeSelf)
        {
            Color c = noteHintColor;
            c.a = Mathf.Lerp(0.7f, 1f, (Mathf.Sin(Time.unscaledTime * 3f) + 1f) * 0.5f);
            noteHintText.color = c;
        }

        int count = NoteJournal.Notes.Count;
        int selectable = Mathf.Min(count, noteLineTexts.Count);

        if (selectable > 0)
        {
            if (Input.GetKeyDown(KeyCode.W) || Input.GetKeyDown(KeyCode.UpArrow) ||
                Input.GetKeyDown(KeyCode.A) || Input.GetKeyDown(KeyCode.LeftArrow))
            {
                noteSelected = (noteSelected - 1 + selectable) % selectable;
                RefreshNoteList();
            }
            if (Input.GetKeyDown(KeyCode.S) || Input.GetKeyDown(KeyCode.DownArrow) ||
                Input.GetKeyDown(KeyCode.D) || Input.GetKeyDown(KeyCode.RightArrow))
            {
                noteSelected = (noteSelected + 1) % selectable;
                RefreshNoteList();
            }
        }

        // Q：返回物品列表
        if (Input.GetKeyDown(journalKey))
        {
            SwitchToGrid();
            return;
        }

        // Esc：也返回物品列表（顺手）
        if (Input.GetKeyDown(KeyCode.Escape))
        {
            SwitchToGrid();
            return;
        }

        // E：打开阅读面板看内容（合上后回到本纸条列表）
        if (Input.GetKeyDown(KeyCode.E) && count > 0)
        {
            NoteJournal.NoteEntry n = NoteJournal.Notes[noteSelected];
            NoteReaderUI.Open(n.title, n.text, this); // owner = 本背包
        }
    }

    private void SwitchToNotes()
    {
        uiState = UIState.Notes;
        noteSelected = 0;
        ApplyView(true);
        RefreshNoteList();
    }

    private void SwitchToGrid()
    {
        uiState = UIState.Grid;
        ApplyView(false);
        UpdateInfo();
    }

    /// <summary> 切换物品视图 / 纸条视图的显示（两个视图共用同一暗色底） </summary>
    private void ApplyView(bool showNotes)
    {
        if (bagPanel != null) bagPanel.SetActive(!showNotes); // 背包整块（左信息栏 + 右网格）一起切
        if (notesPanel != null) notesPanel.SetActive(showNotes);

        Transform tipT = canvasObj != null ? canvasObj.transform.Find("Tip") : null;
        if (tipT != null) tipT.gameObject.SetActive(!showNotes);
    }

    /// <summary> 按 NoteJournal 刷新纸条列表显示（标题 + 高亮；空则灰字提示；中部大字动态摆放） </summary>
    private void RefreshNoteList()
    {
        var notes = NoteJournal.Notes;
        int count = notes.Count;
        int visibleMax = noteLineTexts.Count;
        int selectable = Mathf.Min(count, visibleMax);

        if (count == 0) noteSelected = 0;
        else noteSelected = Mathf.Clamp(noteSelected, 0, selectable - 1);

        // 空列表 → 灰字提示（与中部大字互斥）
        if (notesEmptyText != null) notesEmptyText.gameObject.SetActive(count == 0);

        for (int i = 0; i < visibleMax; i++)
        {
            if (i < count)
            {
                noteLineTexts[i].gameObject.SetActive(true);
                string title = string.IsNullOrEmpty(notes[i].title) ? "纸条" : notes[i].title;
                noteLineTexts[i].text = (i == noteSelected ? "▶ " : "  ") + title;
                noteLineTexts[i].color = (i == noteSelected) ? Color.yellow : Color.white;
            }
            else
            {
                noteLineTexts[i].gameObject.SetActive(false);
            }
        }

        // 纸条太多（>12 张）→ 省略提示
        if (notesOverflowText != null)
        {
            if (count > visibleMax)
            {
                notesOverflowText.gameObject.SetActive(true);
                notesOverflowText.text = "…还有 " + (count - visibleMax) + " 张（共 " + count + " 张）";
            }
            else
            {
                notesOverflowText.gameObject.SetActive(false);
            }
        }

        LayoutNoteHint(count);
    }

    /// <summary> 把中部大字"按 E 查看"摆到列表下方的空白区正中；空白不够（纸条太多）就隐藏 </summary>
    private void LayoutNoteHint(int count)
    {
        if (noteHintText == null) return;

        if (count <= 0)
        {
            noteHintText.gameObject.SetActive(false);
            return;
        }

        const float listTop = 66f;                 // 第一行距面板顶
        float listBottom = listTop + count * 30f;  // 列表底部（距面板顶）
        const float hintAreaTop = 455f;            // 底部操作提示上方
        float gap = hintAreaTop - listBottom;
        float hintH = noteHintFontSize + 16f;

        if (gap < hintH + 20f)                     // 空白不够 → 藏起来（别压到列表）
        {
            noteHintText.gameObject.SetActive(false);
            return;
        }

        float centerY = listBottom + gap * 0.5f;
        noteHintText.gameObject.SetActive(true);
        noteHintText.text = noteViewHint;
        RectTransform rt = noteHintText.GetComponent<RectTransform>();
        rt.anchoredPosition = new Vector2(0f, -centerY); // pivot 在顶部 → 取负号
    }

    /// <summary> 阅读面板 E/F/Esc 合上 → 回到纸条列表视图（不关背包，仍暂停） </summary>
    public void ReturnToNoteList()
    {
        if (!isOpen) return;
        suppressFrame = Time.frameCount; // 本帧余波：别再响应按键（防同帧又弹面板）
        uiState = UIState.Notes;
        ApplyView(true);
        RefreshNoteList();
    }

    /// <summary> 阅读面板 Tab 合上 → 连背包一起关，回游戏 </summary>
    public void CloseFromNoteReader()
    {
        suppressFrame = Time.frameCount;
        isOpen = false;
        Time.timeScale = 1f;
        if (canvasObj != null) Destroy(canvasObj);
    }

    /// <summary> 创建纸条列表面板（暗色风格与操作菜单/合成面板一致） </summary>
    private void CreateNotesPanel()
    {
        notesPanel = new GameObject("NotesPanel");
        notesPanel.transform.SetParent(canvasObj.transform, false);
        Image bg = notesPanel.AddComponent<Image>();
        bg.color = new Color(0.1f, 0.1f, 0.1f, 0.95f);
        RectTransform rect = notesPanel.GetComponent<RectTransform>();
        rect.anchorMin = new Vector2(0.5f, 0.5f);
        rect.anchorMax = new Vector2(0.5f, 0.5f);
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.anchoredPosition = new Vector2(0f, 0f);
        rect.sizeDelta = new Vector2(560f, 520f);

        float rowW = 520f; // 统一内容宽度（比面板 560 窄，左右各留 20 边距 → 绝不越出面板）

        // 标题
        GameObject title = new GameObject("Title");
        title.transform.SetParent(notesPanel.transform, false);
        Text titleText = title.AddComponent<Text>();
        titleText.text = "=== 纸 条 ===";
        titleText.fontSize = 26;
        titleText.color = Color.yellow;
        titleText.alignment = TextAnchor.MiddleCenter;
        titleText.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        RectTransform titleRect = title.GetComponent<RectTransform>();
        titleRect.anchorMin = new Vector2(0.5f, 1f);
        titleRect.anchorMax = new Vector2(0.5f, 1f);
        titleRect.pivot = new Vector2(0.5f, 1f);
        titleRect.anchoredPosition = new Vector2(0f, -14f);
        titleRect.sizeDelta = new Vector2(rowW, 40f);

        // 纸条标题行（预建 12 行，按需显示）——锚在面板水平中心、宽度=内宽，不再跑出面板
        noteLineTexts.Clear();
        for (int i = 0; i < 12; i++)
        {
            GameObject line = new GameObject("Note_" + i);
            line.transform.SetParent(notesPanel.transform, false);
            Text t = line.AddComponent<Text>();
            t.fontSize = 20;
            t.alignment = TextAnchor.MiddleLeft;
            t.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            RectTransform lr = line.GetComponent<RectTransform>();
            lr.anchorMin = new Vector2(0.5f, 1f);
            lr.anchorMax = new Vector2(0.5f, 1f);
            lr.pivot = new Vector2(0.5f, 1f);
            lr.anchoredPosition = new Vector2(0f, -66f - i * 30f);
            lr.sizeDelta = new Vector2(rowW, 28f);
            noteLineTexts.Add(t);
        }

        // 中部大字提示（"按 E 查看"）——具体位置在 RefreshNoteList 里按列表长度动态摆
        GameObject bigHint = new GameObject("BigHint");
        bigHint.transform.SetParent(notesPanel.transform, false);
        noteHintText = bigHint.AddComponent<Text>();
        noteHintText.text = noteViewHint;
        noteHintText.fontSize = noteHintFontSize;
        noteHintText.fontStyle = FontStyle.Bold;
        noteHintText.color = noteHintColor;
        noteHintText.alignment = TextAnchor.MiddleCenter;
        noteHintText.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        noteHintText.raycastTarget = false;
        RectTransform bigRect = noteHintText.GetComponent<RectTransform>();
        bigRect.anchorMin = new Vector2(0.5f, 1f);
        bigRect.anchorMax = new Vector2(0.5f, 1f);
        bigRect.pivot = new Vector2(0.5f, 1f);
        bigRect.anchoredPosition = new Vector2(0f, -260f);
        bigRect.sizeDelta = new Vector2(rowW, noteHintFontSize + 16f);

        // 空提示（灰字）
        GameObject empty = new GameObject("Empty");
        empty.transform.SetParent(notesPanel.transform, false);
        notesEmptyText = empty.AddComponent<Text>();
        notesEmptyText.text = "还没有捡到任何纸条";
        notesEmptyText.fontSize = 20;
        notesEmptyText.color = new Color(0.5f, 0.5f, 0.5f);
        notesEmptyText.alignment = TextAnchor.MiddleCenter;
        notesEmptyText.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        RectTransform er = notesEmptyText.GetComponent<RectTransform>();
        er.anchorMin = new Vector2(0.5f, 1f);
        er.anchorMax = new Vector2(0.5f, 1f);
        er.pivot = new Vector2(0.5f, 1f);
        er.anchoredPosition = new Vector2(0f, -260f);
        er.sizeDelta = new Vector2(rowW, 40f);

        // 纸条过多（>12 张）时的省略提示
        GameObject overflow = new GameObject("Overflow");
        overflow.transform.SetParent(notesPanel.transform, false);
        notesOverflowText = overflow.AddComponent<Text>();
        notesOverflowText.fontSize = 15;
        notesOverflowText.color = new Color(0.6f, 0.6f, 0.6f);
        notesOverflowText.alignment = TextAnchor.MiddleCenter;
        notesOverflowText.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        RectTransform orr = notesOverflowText.GetComponent<RectTransform>();
        orr.anchorMin = new Vector2(0.5f, 0f);
        orr.anchorMax = new Vector2(0.5f, 0f);
        orr.pivot = new Vector2(0.5f, 0f);
        orr.anchoredPosition = new Vector2(0f, 46f);
        orr.sizeDelta = new Vector2(rowW, 24f);

        // 底部操作提示
        GameObject hint = new GameObject("Hint");
        hint.transform.SetParent(notesPanel.transform, false);
        Text hintText = hint.AddComponent<Text>();
        hintText.text = "W/S 选择 | E 阅读 | Q 返回物品 | Tab 关闭";
        hintText.fontSize = 16;
        hintText.color = Color.gray;
        hintText.alignment = TextAnchor.MiddleCenter;
        hintText.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        RectTransform hr = hintText.GetComponent<RectTransform>();
        hr.anchorMin = new Vector2(0.5f, 0f);
        hr.anchorMax = new Vector2(0.5f, 0f);
        hr.pivot = new Vector2(0.5f, 0f);
        hr.anchoredPosition = new Vector2(0f, 12f);
        hr.sizeDelta = new Vector2(rowW, 28f);

        notesPanel.SetActive(false);
    }

    // ======== 状态2：操作菜单 ========

    /// <summary> 为选中的草药构建菜单选项并打开菜单 </summary>
    private void OpenMenuFor(InventoryItem item)
    {
        menuOptions.Clear();

        // "使用"：草药都能选（红草选了会提示不能直接用）
        menuOptions.Add("使用");

        // "合成"：背包里有可用的配方才显示（只有可合成的才能出现）
        if (inventory.GetAvailableRecipes().Count > 0)
            menuOptions.Add("合成");

        menuOptions.Add("取消");

        menuSelected = 0;
        uiState = UIState.Menu;
        menuPanel.SetActive(true);
        RefreshMenuUI();
    }

    private void HandleMenuInput()
    {
        // W/S 上下选择（循环）
        if (Input.GetKeyDown(KeyCode.W) || Input.GetKeyDown(KeyCode.UpArrow))
        {
            menuSelected = (menuSelected - 1 + menuOptions.Count) % menuOptions.Count;
            RefreshMenuUI();
        }
        if (Input.GetKeyDown(KeyCode.S) || Input.GetKeyDown(KeyCode.DownArrow))
        {
            menuSelected = (menuSelected + 1) % menuOptions.Count;
            RefreshMenuUI();
        }

        // Esc 返回网格
        if (Input.GetKeyDown(KeyCode.Escape))
        {
            BackToGrid();
            return;
        }

        // 空格确认
        if (Input.GetKeyDown(useKey))
        {
            string option = menuOptions[menuSelected];
            if (option == "使用")
            {
                string msg = inventory.UseItem(selectedIndex);
                if (msg != null) ShowInfoMessage(msg);
                BackToGrid();
                RefreshSlots(); // 物品可能被用掉了，刷新格子
            }
            else if (option == "合成")
            {
                OpenCombinePanel();
            }
            else if (option == "取消")
            {
                BackToGrid();
            }
        }
    }

    /// <summary> 回到背包网格，隐藏所有面板 </summary>
    private void BackToGrid()
    {
        uiState = UIState.Grid;
        if (menuPanel != null) menuPanel.SetActive(false);
        if (combinePanel != null) combinePanel.SetActive(false);
        UpdateInfo();
    }

    private void RefreshMenuUI()
    {
        for (int i = 0; i < menuOptionTexts.Count; i++)
        {
            if (i < menuOptions.Count)
            {
                menuOptionTexts[i].gameObject.SetActive(true);
                menuOptionTexts[i].text = (i == menuSelected ? "▶ " : "  ") + menuOptions[i];
                menuOptionTexts[i].color = (i == menuSelected) ? Color.yellow : Color.white;
            }
            else
            {
                menuOptionTexts[i].gameObject.SetActive(false);
            }
        }
    }

    // ======== 状态3：合成面板 ========

    private void OpenCombinePanel()
    {
        // 刷新可用配方（材料够的才出现）
        combineList = inventory.GetAvailableRecipes();
        if (combineList.Count == 0)
        {
            ShowInfoMessage("没有可合成的配方（需要绿草 + 红草）");
            return;
        }

        combineSelected = 0;
        uiState = UIState.Combine;
        combinePanel.SetActive(true);
        RefreshCombineUI();
    }

    private void HandleCombineInput()
    {
        // W/S 选择配方（循环）
        if (Input.GetKeyDown(KeyCode.W) || Input.GetKeyDown(KeyCode.UpArrow))
        {
            combineSelected = (combineSelected - 1 + combineList.Count) % combineList.Count;
            RefreshCombineUI();
        }
        if (Input.GetKeyDown(KeyCode.S) || Input.GetKeyDown(KeyCode.DownArrow))
        {
            combineSelected = (combineSelected + 1) % combineList.Count;
            RefreshCombineUI();
        }

        // Esc 返回网格
        if (Input.GetKeyDown(KeyCode.Escape))
        {
            BackToGrid();
            return;
        }

        // 空格合成
        if (Input.GetKeyDown(useKey))
        {
            Inventory.CombineRecipe recipe = combineList[combineSelected];
            if (inventory.TryCombine(recipe))
            {
                ShowInfoMessage("合成成功：" + recipe.resultName + "！");
                RefreshSlots(); // 材料被消耗，刷新格子

                // 刷新配方列表（材料可能不够了）
                combineList = inventory.GetAvailableRecipes();
                if (combineList.Count == 0)
                {
                    BackToGrid();
                    return;
                }
                combineSelected = Mathf.Min(combineSelected, combineList.Count - 1);
                RefreshCombineUI();
            }
        }
    }

    private void RefreshCombineUI()
    {
        for (int i = 0; i < combineOptionTexts.Count; i++)
        {
            if (i < combineList.Count)
            {
                combineOptionTexts[i].gameObject.SetActive(true);
                Inventory.CombineRecipe r = combineList[i];
                combineOptionTexts[i].text = (i == combineSelected ? "▶ " : "  ") + r.recipeName + " → " + r.resultName;
                combineOptionTexts[i].color = (i == combineSelected) ? Color.yellow : Color.white;
            }
            else
            {
                combineOptionTexts[i].gameObject.SetActive(false);
            }
        }
    }

    // ======== 消息提示（使用/合成结果，显示 2 秒） ========

    private void ShowInfoMessage(string msg)
    {
        pendingMessage = msg;
        messageUntil = Time.unscaledTime + 2f; // 用 unscaledTime，暂停时也计时
        UpdateInfo();
    }

    // ======== 原有逻辑（保留）：开关、UI 创建、格子刷新 ========

    private void Open()
    {
        if (isOpen) return;
        isOpen = true;
        Time.timeScale = 0f;
        uiState = UIState.Grid;
        pendingMessage = null;
        CreateUI();
    }

    /// <summary> 外部打开背包（宝石铁门按 F 弹出用）：复用 Open 的暂停/建界面逻辑，已开着就不动 </summary>
    public void OpenExternal()
    {
        openedFrame = Time.frameCount; // 记下弹出帧：这一帧的 Tab 不算关闭指令（同一次按键不能既开又关）
        Open();
    }

    private void Close()
    {
        isOpen = false;
        Time.timeScale = 1f;
        if (canvasObj != null) Destroy(canvasObj);
    }

    /// <summary> 外部关闭背包（纸条阅读面板合上时联动用）：关面板 + 恢复时间流速回游戏 </summary>
    public void CloseExternal()
    {
        if (!isOpen) return;
        isOpen = false;
        suppressFrame = Time.frameCount; // 本帧余波：防同帧 Tab 又把背包弹回来
        Time.timeScale = 1f;
        if (canvasObj != null) Destroy(canvasObj);
    }

    private void OnDestroy()
    {
        if (canvasObj != null) Destroy(canvasObj);
    }

    private void CreateUI()
    {
        canvasObj = new GameObject("InventoryCanvas");
        Canvas canvas = canvasObj.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 200;
        UIScale.Setup(canvasObj);
        canvasObj.AddComponent<GraphicRaycaster>();

        // 半透明背景
        GameObject bg = new GameObject("BG");
        bg.transform.SetParent(canvasObj.transform, false);
        Image bgImg = bg.AddComponent<Image>();
        bgImg.color = new Color(0, 0, 0, 0.7f);
        RectTransform bgRect = bg.GetComponent<RectTransform>();
        bgRect.anchorMin = Vector2.zero;
        bgRect.anchorMax = Vector2.one;
        bgRect.offsetMin = Vector2.zero;
        bgRect.offsetMax = Vector2.zero;

        // ===== 物品网格：视口（RectMask2D 裁剪） + 内容容器（滚动只动它） =====
        float scale = Mathf.Max(0.1f, inventoryScale);      // 尺寸倍率（放大背包用，不动全局 UIScale）
        uiScale = scale;                                    // 存一份：格子内文字字号、信息/提示行也按它放大
        cellW = slotSize * scale;                           // 格子宽
        cellH = (slotSize + 20f) * scale;                   // 格子高（+20 是留给名字的底栏）
        cellSpacing = padding * scale;                      // 格子间距

        // 视口尺寸：横向正好放下 columns 列；纵向正好放下 visibleRows 行
        float contentW = columns * cellW + (columns - 1) * cellSpacing + 2f * cellSpacing;
        float viewW = contentW;
        float viewH = visibleRows * cellH + (visibleRows - 1) * cellSpacing + 2f * cellSpacing;
        viewportHeight = viewH;

        // ===== 统一暗色面板：左「物品信息栏」 + 右「物品网格」（两者同处一块面板 → 信息区不再压着游戏画面） =====
        float infoW = 210f * scale;   // 左信息栏宽（小泽要求：在原来的基础上减半）
        float gap = 24f * scale;      // 信息栏与网格之间的间距
        float pad = 20f * scale;      // 面板内边距
        float panelW = infoW + gap + viewW + pad * 2f;
        float panelH = viewH + pad * 2f;

        bagPanel = new GameObject("BagPanel", typeof(RectTransform));
        bagPanel.transform.SetParent(canvasObj.transform, false);
        Image bagBg = bagPanel.AddComponent<Image>();
        bagBg.color = new Color(0.06f, 0.06f, 0.08f, 0.92f); // 与网格同色（左栏与右网格融为一体）
        bagBg.raycastTarget = false;                          // 不吃点击
        RectTransform bagRect = bagPanel.GetComponent<RectTransform>();
        bagRect.anchorMin = new Vector2(0.5f, 0.5f);
        bagRect.anchorMax = new Vector2(0.5f, 0.5f);
        bagRect.pivot = new Vector2(0.5f, 0.5f);
        bagRect.anchoredPosition = Vector2.zero;
        bagRect.sizeDelta = new Vector2(panelW, panelH);

        slotContainer = new GameObject("GridViewport", typeof(RectTransform));
        slotContainer.transform.SetParent(bagPanel.transform, false); // 挂进面板（右半栏）
        Image viewBg = slotContainer.AddComponent<Image>();
        viewBg.color = new Color(0.06f, 0.06f, 0.08f, 0.9f);  // 背包面板底色（放大后能看出是一块面板）
        viewBg.raycastTarget = false;                         // 不吃鼠标点击，避免挡住别的 UI
        slotContainer.AddComponent<RectMask2D>();             // 关键：把超出视口的格子裁掉
        viewportRect = slotContainer.GetComponent<RectTransform>();
        viewportRect.anchorMin = new Vector2(0.5f, 0.5f);
        viewportRect.anchorMax = new Vector2(0.5f, 0.5f);
        viewportRect.pivot = new Vector2(0.5f, 0.5f);
        viewportRect.anchoredPosition = new Vector2(panelW * 0.5f - pad - viewW * 0.5f, 0f); // 靠面板右半栏
        viewportRect.sizeDelta = new Vector2(viewW, viewH);

        // 内容容器：GridLayoutGroup 挂这里；滚动 = 改它的 anchoredPosition.y
        slotContent = new GameObject("Slots", typeof(RectTransform));
        slotContent.transform.SetParent(slotContainer.transform, false);
        GridLayoutGroup grid = slotContent.AddComponent<GridLayoutGroup>();
        grid.cellSize = new Vector2(cellW, cellH);
        grid.spacing = new Vector2(cellSpacing, cellSpacing);
        grid.padding = new RectOffset((int)cellSpacing, (int)cellSpacing, (int)cellSpacing, (int)cellSpacing);
        grid.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
        grid.constraintCount = columns;
        grid.childAlignment = TextAnchor.UpperCenter;         // 内容顶部对齐，铺不满也不跑偏
        contentRect = slotContent.GetComponent<RectTransform>();
        contentRect.anchorMin = new Vector2(0.5f, 1f);         // 顶部对齐视口顶部（往下滚 = 往上推内容）
        contentRect.anchorMax = new Vector2(0.5f, 1f);
        contentRect.pivot = new Vector2(0.5f, 1f);
        contentRect.anchoredPosition = Vector2.zero;
        contentRect.sizeDelta = new Vector2(viewW, viewH);

        CreateScrollbar();  // 极简滚动条（超出可视区才出现）

        // 操作提示
        GameObject tip = new GameObject("Tip");
        tip.transform.SetParent(canvasObj.transform, false);
        Text tipText = tip.AddComponent<Text>();
        tipText.text = "WASD 选择 | 空格 使用/合成 | E 使用宝石 | Q 切换纸条 | Tab 关闭";
        tipText.fontSize = Mathf.RoundToInt(20f * scale); // 底部操作提示：随倍率放大，免得"格子大、字小"
        tipText.color = Color.white;
        tipText.alignment = TextAnchor.MiddleCenter;
        tipText.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        RectTransform tipRect = tip.GetComponent<RectTransform>();
        tipRect.anchorMin = new Vector2(0.5f, 0f);
        tipRect.anchorMax = new Vector2(0.5f, 0f);
        tipRect.pivot = new Vector2(0.5f, 0.5f);
        tipRect.anchoredPosition = new Vector2(0, 30f);
        tipRect.sizeDelta = new Vector2(500f * scale, 40f); // 字号变大 → 提示行同步加宽，别被截断

        // ===== 物品信息区：移进面板左栏，铺米白底板 + 黑字（不再压着游戏画面） =====
        CreateInfoBoard(panelW, infoW, viewH, pad, scale);

        // 新增：操作菜单面板（默认隐藏）
        CreateMenuPanel();

        // 新增：合成面板（默认隐藏）
        CreateCombinePanel();

        // 新增：纸条列表面板（Q 切换进入，默认隐藏）
        CreateNotesPanel();

        // 新增：健康指示（生化2式颜色状态，仅打开背包时可见）
        CreateHealthUI();
        RefreshHealthUI();

        selectedIndex = 0;
        RefreshSlots();
    }

    /// <summary> 创建物品信息区（面板左栏）：米白底板 + 黑字（名称标题 + 描述正文，自动换行、超出截断） </summary>
    private void CreateInfoBoard(float panelW, float infoW, float infoH, float pad, float scale)
    {
        // 米白/纸色底板（对齐 NoteReaderUI 的纸色）
        infoBoard = new GameObject("InfoBoard", typeof(RectTransform));
        infoBoard.transform.SetParent(bagPanel.transform, false);
        Image boardImg = infoBoard.AddComponent<Image>();
        boardImg.color = new Color(0.96f, 0.95f, 0.9f, 0.95f); // 与 NoteReaderUI 纸片完全同色（米白）
        boardImg.raycastTarget = false;
        RectTransform boardRect = infoBoard.GetComponent<RectTransform>();
        boardRect.anchorMin = new Vector2(0.5f, 0.5f);
        boardRect.anchorMax = new Vector2(0.5f, 0.5f);
        boardRect.pivot = new Vector2(0.5f, 0.5f);
        // 左栏中心 = -面板宽/2 + 内边距 + 信息栏宽/2
        boardRect.anchoredPosition = new Vector2(-panelW * 0.5f + pad + infoW * 0.5f, 0f);
        boardRect.sizeDelta = new Vector2(infoW, infoH);

        // 与纸条纸片同款边框（暗一圈，模拟纸片边缘/阴影）——参数逐值对齐 NoteReaderUI
        GameObject border = new GameObject("Border", typeof(RectTransform));
        border.transform.SetParent(infoBoard.transform, false);
        Image borderImg = border.AddComponent<Image>();
        borderImg.color = new Color(0.55f, 0.52f, 0.45f, 0.9f); // 纸边灰褐（与纸条同色）
        RectTransform borderRect = borderImg.GetComponent<RectTransform>();
        borderRect.anchorMin = Vector2.zero;
        borderRect.anchorMax = Vector2.one;
        borderRect.offsetMin = new Vector2(-4f, -4f) * scale; // 边框外扩（随 inventoryScale 缩放）
        borderRect.offsetMax = new Vector2(4f, 4f) * scale;
        borderImg.raycastTarget = false;
        borderRect.SetSiblingIndex(0); // 与纸条同款层次（垫到信息板下）

        float inner = 12f * scale;            // 板内边距（窄板收紧一点，给文字多留宽度）
        float nameH = 40f * scale;            // 名称行高
        float textW = infoW - inner * 2f;     // 文字宽度（板宽减左右内边距）

        // 名称（黑字加粗，标题感）——左上锚定 + 固定尺寸（不用 offset，避免与 anchoredPosition 打架）
        GameObject nameGO = new GameObject("Name", typeof(RectTransform));
        nameGO.transform.SetParent(infoBoard.transform, false);
        infoNameText = nameGO.AddComponent<Text>();
        infoNameText.fontSize = Mathf.RoundToInt(22f * scale); // 窄板：字号适度下调，避免名称挤成两字一行
        infoNameText.fontStyle = FontStyle.Bold;
        infoNameText.color = Color.black;
        infoNameText.alignment = TextAnchor.MiddleLeft;
        infoNameText.horizontalOverflow = HorizontalWrapMode.Wrap; // 名字过长也换行，不越出板
        infoNameText.verticalOverflow = VerticalWrapMode.Truncate;
        infoNameText.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        infoNameText.raycastTarget = false;
        RectTransform nameRect = nameGO.GetComponent<RectTransform>();
        nameRect.anchorMin = new Vector2(0f, 1f);
        nameRect.anchorMax = new Vector2(0f, 1f);
        nameRect.pivot = new Vector2(0f, 1f);
        nameRect.anchoredPosition = new Vector2(inner, -inner);
        nameRect.sizeDelta = new Vector2(textW, nameH);

        // 描述（黑字，自动换行；超出板高自动截断，不撑爆面板）
        GameObject descGO = new GameObject("Desc", typeof(RectTransform));
        descGO.transform.SetParent(infoBoard.transform, false);
        infoDescText = descGO.AddComponent<Text>();
        infoDescText.fontSize = Mathf.RoundToInt(17f * scale); // 窄板：描述字号下调，行数变多但仍自动换行/截断
        infoDescText.color = new Color(0.12f, 0.12f, 0.12f, 1f); // 近黑（比纯黑柔和，纸面上更耐看）
        infoDescText.alignment = TextAnchor.UpperLeft;
        infoDescText.horizontalOverflow = HorizontalWrapMode.Wrap;
        infoDescText.verticalOverflow = VerticalWrapMode.Truncate;
        infoDescText.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        infoDescText.raycastTarget = false;
        RectTransform descRect = descGO.GetComponent<RectTransform>();
        descRect.anchorMin = new Vector2(0f, 1f);
        descRect.anchorMax = new Vector2(0f, 1f);
        descRect.pivot = new Vector2(0f, 1f);
        descRect.anchoredPosition = new Vector2(inner, -(inner + nameH + 8f * scale));
        descRect.sizeDelta = new Vector2(textW, Mathf.Max(0f, infoH - inner * 2f - nameH - 8f * scale));
    }

    /// <summary> 创建操作菜单（使用/合成/取消） </summary>
    private void CreateMenuPanel()
    {
        menuPanel = new GameObject("MenuPanel");
        menuPanel.transform.SetParent(canvasObj.transform, false);
        Image bg = menuPanel.AddComponent<Image>();
        bg.color = new Color(0.1f, 0.1f, 0.1f, 0.92f);
        RectTransform rect = menuPanel.GetComponent<RectTransform>();
        rect.anchorMin = new Vector2(0.5f, 0.5f);
        rect.anchorMax = new Vector2(0.5f, 0.5f);
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.anchoredPosition = new Vector2(0, 0);
        rect.sizeDelta = new Vector2(300, 180);

        menuOptionTexts.Clear();
        for (int i = 0; i < 3; i++)
        {
            GameObject option = new GameObject("Option_" + i);
            option.transform.SetParent(menuPanel.transform, false);
            Text t = option.AddComponent<Text>();
            t.fontSize = 22;
            t.alignment = TextAnchor.MiddleCenter;
            t.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            RectTransform oRect = option.GetComponent<RectTransform>();
            oRect.anchorMin = new Vector2(0f, 1f);
            oRect.anchorMax = new Vector2(1f, 1f);
            oRect.pivot = new Vector2(0.5f, 1f);
            oRect.anchoredPosition = new Vector2(0, -10f - i * 55f);
            oRect.sizeDelta = new Vector2(280, 45);
            menuOptionTexts.Add(t);
        }
        menuPanel.SetActive(false);
    }

    /// <summary> 创建合成面板（标题 + 配方列表 + 提示） </summary>
    private void CreateCombinePanel()
    {
        combinePanel = new GameObject("CombinePanel");
        combinePanel.transform.SetParent(canvasObj.transform, false);
        Image bg = combinePanel.AddComponent<Image>();
        bg.color = new Color(0.1f, 0.1f, 0.1f, 0.92f);
        RectTransform rect = combinePanel.GetComponent<RectTransform>();
        rect.anchorMin = new Vector2(0.5f, 0.5f);
        rect.anchorMax = new Vector2(0.5f, 0.5f);
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.anchoredPosition = new Vector2(0, 0);
        rect.sizeDelta = new Vector2(420, 300);

        // 标题
        GameObject title = new GameObject("Title");
        title.transform.SetParent(combinePanel.transform, false);
        Text titleText = title.AddComponent<Text>();
        titleText.text = "=== 合 成 ===";
        titleText.fontSize = 24;
        titleText.color = Color.yellow;
        titleText.alignment = TextAnchor.MiddleCenter;
        titleText.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        RectTransform titleRect = title.GetComponent<RectTransform>();
        titleRect.anchorMin = new Vector2(0f, 1f);
        titleRect.anchorMax = new Vector2(1f, 1f);
        titleRect.pivot = new Vector2(0.5f, 1f);
        titleRect.anchoredPosition = new Vector2(0, -10f);
        titleRect.sizeDelta = new Vector2(400, 40);

        // 配方列表（预创建 5 个槽，按需显示）
        combineOptionTexts.Clear();
        for (int i = 0; i < 5; i++)
        {
            GameObject option = new GameObject("Recipe_" + i);
            option.transform.SetParent(combinePanel.transform, false);
            Text t = option.AddComponent<Text>();
            t.fontSize = 20;
            t.alignment = TextAnchor.MiddleCenter;
            t.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            RectTransform oRect = option.GetComponent<RectTransform>();
            oRect.anchorMin = new Vector2(0f, 1f);
            oRect.anchorMax = new Vector2(1f, 1f);
            oRect.pivot = new Vector2(0.5f, 1f);
            oRect.anchoredPosition = new Vector2(0, -60f - i * 45f);
            oRect.sizeDelta = new Vector2(400, 40);
            combineOptionTexts.Add(t);
        }

        // 底部提示
        GameObject hint = new GameObject("Hint");
        hint.transform.SetParent(combinePanel.transform, false);
        Text hintText = hint.AddComponent<Text>();
        hintText.text = "W/S 选择 | 空格 合成 | Esc 返回";
        hintText.fontSize = 16;
        hintText.color = Color.gray;
        hintText.alignment = TextAnchor.MiddleCenter;
        hintText.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        RectTransform hintRect = hint.GetComponent<RectTransform>();
        hintRect.anchorMin = new Vector2(0f, 0f);
        hintRect.anchorMax = new Vector2(1f, 0f);
        hintRect.pivot = new Vector2(0.5f, 0f);
        hintRect.anchoredPosition = new Vector2(0, 10f);
        hintRect.sizeDelta = new Vector2(400, 30);

        combinePanel.SetActive(false);
    }

    // ======== 健康指示（生化2式：颜色状态代替数字，仅打开背包可见） ========

    /// <summary> 创建健康指示 UI：圆形色块 + 状态文字（在物品信息下方） </summary>
    private void CreateHealthUI()
    {
        // 色块 + 文字 的容器（屏幕左下角，像生化危机的状态指示）
        // 注意：必须显式加 RectTransform（UI 坐标组件），否则拿不到 → 抛空引用
        GameObject group = new GameObject("HealthState", typeof(RectTransform));
        group.transform.SetParent(canvasObj.transform, false);
        RectTransform groupRect = group.GetComponent<RectTransform>();
        groupRect.anchorMin = new Vector2(0f, 0f); // 锚定左下角
        groupRect.anchorMax = new Vector2(0f, 0f);
        groupRect.pivot = new Vector2(0f, 0f);
        groupRect.anchoredPosition = new Vector2(24f, 24f); // 离左下角 24 像素
        groupRect.sizeDelta = new Vector2(400, 60);

        // 圆形色块（代码生成圆形贴图，无需素材）
        GameObject circle = new GameObject("Circle");
        circle.transform.SetParent(group.transform, false);
        healthCircleImg = circle.AddComponent<Image>();
        healthCircleImg.sprite = CreateCircleSprite();
        healthCircleImg.color = Color.white;
        RectTransform circleRect = healthCircleImg.GetComponent<RectTransform>();
        circleRect.anchorMin = new Vector2(0f, 0.5f);  // 容器左边缘、竖直居中
        circleRect.anchorMax = new Vector2(0f, 0.5f);
        circleRect.pivot = new Vector2(0.5f, 0.5f);
        circleRect.anchoredPosition = new Vector2(30f, 0f);
        circleRect.sizeDelta = new Vector2(46f, 46f);

        // 状态文字
        GameObject state = new GameObject("State");
        state.transform.SetParent(group.transform, false);
        healthStateText = state.AddComponent<Text>();
        healthStateText.fontSize = 30;
        healthStateText.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        healthStateText.alignment = TextAnchor.MiddleLeft;
        RectTransform stateRect = healthStateText.GetComponent<RectTransform>();
        stateRect.anchorMin = new Vector2(0f, 0.5f);   // 圆形右侧
        stateRect.anchorMax = new Vector2(0f, 0.5f);
        stateRect.pivot = new Vector2(0f, 0.5f);
        stateRect.anchoredPosition = new Vector2(62f, 0f);
        stateRect.sizeDelta = new Vector2(280f, 50f);
    }

    /// <summary> 代码生成实心圆形贴图（圆形色块用） </summary>
    private Sprite CreateCircleSprite()
    {
        int size = 128;
        Texture2D tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
        tex.wrapMode = TextureWrapMode.Clamp;
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float nx = x / (float)(size - 1) * 2f - 1f; // -1 ~ 1
                float ny = y / (float)(size - 1) * 2f - 1f;
                float dist = Mathf.Sqrt(nx * nx + ny * ny); // 到中心的距离（1=圆边）
                // 边缘抗锯齿：0.9 内全白，0.9~1 渐变透明
                float alpha = Mathf.Clamp01((1f - dist) / 0.1f);
                tex.SetPixel(x, y, new Color(1f, 1f, 1f, alpha));
            }
        }
        tex.Apply();
        return Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f));
    }

    /// <summary> 按当前血量百分比刷新健康色块颜色 + 状态文字 </summary>
    private void RefreshHealthUI()
    {
        if (healthSystem == null || healthCircleImg == null) return;

        float ratio = 0f;
        if (healthSystem.maxHealth > 0)
            ratio = (float)healthSystem.currentHealth / healthSystem.maxHealth;

        Color c;
        string state;

        if (ratio >= 1f)
        {
            c = Color.green;                 state = "健康";     // 100%
        }
        else if (ratio >= 0.7f)
        {
            c = new Color(0.6f, 1f, 0.2f);   state = "注意";     // 70~99% 黄绿
        }
        else if (ratio >= 0.3f)
        {
            c = Color.yellow;                state = "危险";     // 30~69% 黄
        }
        else
        {
            c = new Color(1f, 0.1f, 0.05f);  state = "濒死";     // <30% 血红
        }

        healthCircleImg.color = c;
        if (healthStateText != null)
        {
            healthStateText.text = state;
            healthStateText.color = c;
        }
    }

    private void RefreshSlots()
    {
        foreach (var s in slotObjs) Destroy(s);
        slotObjs.Clear();

        int total = inventory.items.Count;

        // 选中项兜底：物品被用掉/丢掉后，选中索引可能越界
        if (total > 0) selectedIndex = Mathf.Clamp(selectedIndex, 0, total - 1);
        else selectedIndex = 0;

        for (int i = 0; i < total; i++)
        {
            InventoryItem item = inventory.GetItemAt(i);
            if (item == null) continue;

            GameObject slot = new GameObject("Slot_" + i);
            slot.transform.SetParent(slotContent.transform, false); // 挂到滚动内容容器（不再直接挂 canvas）

            // 背景
            Image slotBg = slot.AddComponent<Image>();
            slotBg.color = (i == selectedIndex) ? new Color(0.3f, 0.6f, 1f, 0.8f) : new Color(0.2f, 0.2f, 0.2f, 0.8f);

            // 图标
            if (item.icon != null)
            {
                GameObject iconGO = new GameObject("Icon");
                iconGO.transform.SetParent(slot.transform, false);
                Image iconImg = iconGO.AddComponent<Image>();
                iconImg.sprite = item.icon;
                iconImg.preserveAspect = true;
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
            nameText.fontSize = Mathf.RoundToInt(12f * uiScale); // 物品名字号随倍率放大（原来固定 12 → 格子大、字小的割裂感）
            nameText.color = Color.white;
            nameText.alignment = TextAnchor.LowerCenter;
            nameText.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            RectTransform nameRect = nameGO.GetComponent<RectTransform>();
            nameRect.anchorMin = new Vector2(0f, 0f);
            nameRect.anchorMax = new Vector2(1f, 0.3f);
            nameRect.offsetMin = Vector2.zero;
            nameRect.offsetMax = Vector2.zero;

            slotObjs.Add(slot);
        }

        // 按物品数算内容总高（行数向上取整），据此设置滚动容器高度
        int rows = Mathf.Max(1, Mathf.CeilToInt(total / (float)columns));
        contentHeight = rows * cellH + (rows - 1) * cellSpacing + 2f * cellSpacing;
        if (contentRect != null)
            contentRect.sizeDelta = new Vector2(viewportRect.sizeDelta.x, Mathf.Max(contentHeight, viewportHeight));

        // 内容变短后，滚动位置要夹回合法范围
        scrollY = Mathf.Clamp(scrollY, 0f, Mathf.Max(0f, contentHeight - viewportHeight));
        ApplyScroll();

        UpdateInfo();
    }

    private void UpdateSelection()
    {
        for (int i = 0; i < slotObjs.Count; i++)
        {
            Image img = slotObjs[i].GetComponent<Image>();
            if (img != null)
                img.color = (i == selectedIndex) ? new Color(0.3f, 0.6f, 1f, 0.8f) : new Color(0.2f, 0.2f, 0.2f, 0.8f);
        }
        ScrollToIndex(selectedIndex); // 选中项超出可视区 → 视口自动位移让它露出来（到底/到顶夹紧）
        UpdateInfo();
    }

    // ======== 物品网格滚动（视口 + 内容位移；不依赖 ScrollRect，逻辑更直白） ========

    /// <summary> 建极简滚动条：右侧细轨道 + 滑块（内容超出可视区时才显示） </summary>
    private void CreateScrollbar()
    {
        if (!showScrollbar) return;

        scrollbarTrack = new GameObject("Scrollbar", typeof(RectTransform));
        scrollbarTrack.transform.SetParent(slotContainer.transform, false);
        Image trackImg = scrollbarTrack.AddComponent<Image>();
        trackImg.color = new Color(0f, 0f, 0f, 0.35f);
        RectTransform tr = scrollbarTrack.GetComponent<RectTransform>();
        tr.anchorMin = new Vector2(1f, 0f);
        tr.anchorMax = new Vector2(1f, 1f);
        tr.pivot = new Vector2(1f, 0.5f);
        tr.anchoredPosition = new Vector2(-5f, 0f);       // 贴视口右内边
        tr.sizeDelta = new Vector2(8f, -16f);             // 宽 8，上下各留 8 边距

        scrollbarHandle = new GameObject("Handle", typeof(RectTransform));
        scrollbarHandle.transform.SetParent(scrollbarTrack.transform, false);
        Image hImg = scrollbarHandle.AddComponent<Image>();
        hImg.color = new Color(0.7f, 0.7f, 0.75f, 0.9f);
        RectTransform hr = scrollbarHandle.GetComponent<RectTransform>();
        hr.anchorMin = new Vector2(0f, 1f);
        hr.anchorMax = new Vector2(1f, 1f);
        hr.pivot = new Vector2(0.5f, 1f);
        hr.anchoredPosition = Vector2.zero;
        hr.sizeDelta = new Vector2(0f, 40f);

        scrollbarTrack.SetActive(false); // 内容不满一屏先藏着
    }

    /// <summary> 把滚动位移应用到内容容器（唯一改位置的地方） </summary>
    private void ApplyScroll()
    {
        if (contentRect != null)
            contentRect.anchoredPosition = new Vector2(0f, scrollY);
        UpdateScrollbar();
    }

    /// <summary> 让第 index 个物品所在「整行」落进可视窗；已在窗内则不动（到底/到顶自然夹紧） </summary>
    private void ScrollToIndex(int index)
    {
        if (contentRect == null || viewportHeight <= 0f) return;

        int total = inventory.items.Count;
        if (total == 0) { scrollY = 0f; ApplyScroll(); return; }

        int row = Mathf.Clamp(index, 0, total - 1) / Mathf.Max(1, columns);
        float maxScroll = Mathf.Max(0f, contentHeight - viewportHeight);

        // 该行在「内容坐标（从内容顶部往下量）」里的上下边（+cellSpacing 是网格上内边距）
        float rowTop = cellSpacing + row * (cellH + cellSpacing);
        float rowBottom = rowTop + cellH;

        // 目标：让整行落在 [scrollY, scrollY + viewportHeight] 内
        if (rowTop < scrollY) scrollY = rowTop;                              // 行在上方 → 往上追
        else if (rowBottom > scrollY + viewportHeight) scrollY = rowBottom - viewportHeight; // 行在下方 → 往下露

        scrollY = Mathf.Clamp(scrollY, 0f, maxScroll);
        ApplyScroll();
    }

    /// <summary> 按当前滚动比例刷新滑块位置/长度（极简版，不做拖拽） </summary>
    private void UpdateScrollbar()
    {
        if (!showScrollbar || scrollbarTrack == null || scrollbarHandle == null) return;

        float viewH = Mathf.Max(1f, viewportHeight);
        float contentH = Mathf.Max(contentHeight, viewH);
        bool needScroll = contentH > viewH + 0.5f;   // 一屏放得下就不滚动

        scrollbarTrack.SetActive(needScroll);
        if (!needScroll) return;

        float trackH = Mathf.Max(1f, viewH - 16f);   // 轨道可用高（与 sizeDelta.y=-16 对应，避免依赖布局时机）
        float maxScroll = contentH - viewH;

        float handleH = Mathf.Clamp(trackH * (viewH / contentH), 24f, trackH);
        RectTransform hr = scrollbarHandle.GetComponent<RectTransform>();
        hr.sizeDelta = new Vector2(0f, handleH);

        float ratio = maxScroll > 0.001f ? Mathf.Clamp01(scrollY / maxScroll) : 0f;
        hr.anchoredPosition = new Vector2(0f, -ratio * (trackH - handleH)); // pivot 在顶部 → 取负号
    }

    private void UpdateInfo()
    {
        if (infoNameText == null || infoDescText == null) return;

        // 有提示消息且没过期 → 名称留空，消息填在描述板里（米白底黑字）
        if (pendingMessage != null && Time.unscaledTime < messageUntil)
        {
            infoNameText.text = "";
            infoDescText.text = pendingMessage;
            return;
        }
        pendingMessage = null;

        // 正常显示选中物品：名称（标题感）+ 描述
        InventoryItem item = inventory.GetItemAt(selectedIndex);
        if (item != null)
        {
            infoNameText.text = item.itemName;
            infoDescText.text = item.description;
        }
        else
        {
            infoNameText.text = "";
            infoDescText.text = "";
        }
    }
}
