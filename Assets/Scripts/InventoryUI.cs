using UnityEngine;
using UnityEngine.UI;
using System.Collections.Generic;

/// <summary>
/// 背包 UI：Tab 打开 → WASD 选物品 → 空格操作。
/// 新增：草药物品按空格弹【操作菜单】（使用/合成/取消），合成面板只显示材料够的配方。
/// 保留原有：枪装备、Tab 关闭、时间暂停等逻辑。
/// </summary>
public class InventoryUI : MonoBehaviour
{
    [Header("按键")]
    public KeyCode toggleKey = KeyCode.Tab;
    public KeyCode useKey = KeyCode.Space;

    [Header("外观")]
    public int columns = 4;
    public float slotSize = 80f;
    public float padding = 10f;

    private Inventory inventory;
    private GameObject canvasObj;
    private GameObject slotContainer;
    private int selectedIndex = 0;
    private bool isOpen = false;
    private List<GameObject> slotObjs = new List<GameObject>();

    // ======== 状态机：网格 → 操作菜单 → 合成面板 ========
    private enum UIState { Grid, Menu, Combine }
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
        // Tab 开关背包：外部弹出（OpenExternal）当帧的 Tab 不作关闭指令——防"同帧自关"（柜子 openedFrame 同款守卫）
        if (Input.GetKeyDown(toggleKey) && Time.frameCount != openedFrame)
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
        }
    }

    // ======== 状态1：背包网格（原有 WASD 选择 + 空格操作） ========

    private void HandleGridInput()
    {
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

        // E：使用宝石（靠近宝石铁门时镶嵌；镶嵌成功 toast 由 GemDoor 发，失败在这里提示）
        if (Input.GetKeyDown(KeyCode.E))
        {
            InventoryItem gem = inventory.GetItemAt(selectedIndex);
            if (gem != null && GemDoor.IsGemItem(gem.itemID))
            {
                bool ok = GemDoor.TryEmbed(gem.itemID);
                if (ok) RefreshSlots();                    // 宝石已从背包移除，刷新格子
                else ShowInfoMessage("要靠近铁门才能镶嵌"); // 不在门边/已镶过 → 提示且宝石保留
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
        Debug.Log("[宝石门诊断] OpenExternal 被调用，弹出帧=" + openedFrame + "，isOpen(调用前)=" + isOpen); // ⚠️ 排查用临时日志
        Open();
    }

    private void Close()
    {
        isOpen = false;
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
        canvasObj.AddComponent<CanvasScaler>().uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
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

        // Slot容器
        slotContainer = new GameObject("Slots");
        slotContainer.transform.SetParent(canvasObj.transform, false);
        GridLayoutGroup grid = slotContainer.AddComponent<GridLayoutGroup>();
        grid.cellSize = new Vector2(slotSize, slotSize + 20f);
        grid.spacing = new Vector2(padding, padding);
        grid.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
        grid.constraintCount = columns;

        RectTransform gridRect = slotContainer.GetComponent<RectTransform>();
        gridRect.anchorMin = new Vector2(0.5f, 0.5f);
        gridRect.anchorMax = new Vector2(0.5f, 0.5f);
        gridRect.pivot = new Vector2(0.5f, 0.5f);
        gridRect.anchoredPosition = Vector2.zero;

        // 操作提示
        GameObject tip = new GameObject("Tip");
        tip.transform.SetParent(canvasObj.transform, false);
        Text tipText = tip.AddComponent<Text>();
        tipText.text = "WASD 选择 | 空格 使用/合成 | E 使用宝石 | Tab 关闭";
        tipText.fontSize = 20;
        tipText.color = Color.white;
        tipText.alignment = TextAnchor.MiddleCenter;
        tipText.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        RectTransform tipRect = tip.GetComponent<RectTransform>();
        tipRect.anchorMin = new Vector2(0.5f, 0f);
        tipRect.anchorMax = new Vector2(0.5f, 0f);
        tipRect.pivot = new Vector2(0.5f, 0.5f);
        tipRect.anchoredPosition = new Vector2(0, 30f);
        tipRect.sizeDelta = new Vector2(500, 40);

        // 物品信息显示
        GameObject info = new GameObject("Info");
        info.transform.SetParent(canvasObj.transform, false);
        Text infoText = info.AddComponent<Text>();
        infoText.fontSize = 22;
        infoText.color = Color.white;
        infoText.alignment = TextAnchor.UpperLeft;
        infoText.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        RectTransform infoRect = info.GetComponent<RectTransform>();
        infoRect.anchorMin = new Vector2(0.5f, 1f);
        infoRect.anchorMax = new Vector2(0.5f, 1f);
        infoRect.pivot = new Vector2(0.5f, 1f);
        infoRect.anchoredPosition = new Vector2(0, -20f);
        infoRect.sizeDelta = new Vector2(600, 100);

        // 新增：操作菜单面板（默认隐藏）
        CreateMenuPanel();

        // 新增：合成面板（默认隐藏）
        CreateCombinePanel();

        // 新增：健康指示（生化2式颜色状态，仅打开背包时可见）
        CreateHealthUI();
        RefreshHealthUI();

        selectedIndex = 0;
        RefreshSlots();
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
        for (int i = 0; i < total; i++)
        {
            InventoryItem item = inventory.GetItemAt(i);
            if (item == null) continue;

            GameObject slot = new GameObject("Slot_" + i);
            slot.transform.SetParent(slotContainer.transform, false);

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
            nameText.fontSize = 12;
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
        UpdateInfo();
    }

    private void UpdateInfo()
    {
        Transform infoT = canvasObj?.transform.Find("Info");
        if (infoT == null) return;
        Text t = infoT.GetComponent<Text>();

        // 有提示消息且没过期 → 显示消息
        if (pendingMessage != null && Time.unscaledTime < messageUntil)
        {
            t.text = pendingMessage;
            return;
        }
        pendingMessage = null;

        // 正常显示选中物品信息
        InventoryItem item = inventory.GetItemAt(selectedIndex);
        if (item != null)
            t.text = "<b>" + item.itemName + "</b>\n" + item.description;
        else
            t.text = "";
    }
}
