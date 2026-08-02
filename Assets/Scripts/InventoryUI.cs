using UnityEngine;
using UnityEngine.UI;
using System.Collections.Generic;

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

    private void Start()
    {
        inventory = GetComponent<Inventory>();
    }

    private void Update()
    {
        if (Input.GetKeyDown(toggleKey))
        {
            if (isOpen) Close();
            else Open();
        }

        if (!isOpen) return;

        int cols = columns;
        int total = inventory.items.Count;
        if (total == 0) return;

        // WASD navigation
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

        if (Input.GetKeyDown(useKey))
        {
            InventoryItem item = inventory.GetItemAt(selectedIndex);
            if (item != null)
            {
                if (item.itemType == ItemType.Gun)
                {
                    inventory.EquipAt(selectedIndex);
                    Close();
                    return;
                }
                else
                {
                    Debug.Log("[背包] " + item.itemName + ": " + item.description);
                    Close();
                    return;
                }
            }
        }
    }

    private void Open()
    {
        if (isOpen) return;
        isOpen = true;
        Time.timeScale = 0f;
        CreateUI();
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
        tipText.text = "WASD 选择 | 空格 使用 | Tab 关闭";
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

        selectedIndex = 0;
        RefreshSlots();
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
        InventoryItem item = inventory.GetItemAt(selectedIndex);
        if (item != null)
            t.text = "<b>" + item.itemName + "</b>\n" + item.description;
        else
            t.text = "";
    }
}
