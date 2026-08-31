using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 武器 HUD：屏幕右下角显示当前装备武器的弹药/耐久。
/// 平时隐藏（黑框不常驻），装备武器/开枪/换枪时出现，3 秒后自动消失。
/// 挂到玩家（Player）物体上，需要和 Inventory 在同一个物体。
/// </summary>
public class AmmoUI : MonoBehaviour
{
    [Header("HUD 设置")]
    [Tooltip("出现后持续显示多少秒，然后自动隐藏")]
    public float displayDuration = 3f;

    private Inventory inventory;
    private Gun gun;              // 用来读取"换弹中"状态
    private Text ammoText;
    private GameObject bgGO;      // 底板引用，用于整块显隐
    private float lastShowTime;   // 上次"重新显示"的时间点

    private void Start()
    {
        inventory = GetComponent<Inventory>();
        gun = GetComponent<Gun>();
        CreateAmmoUI();

        // 订阅背包事件：开枪扣弹、换武器、补弹、拾取都会触发 onChanged
        if (inventory != null)
            inventory.onChanged += OnInventoryChanged;

        // 开局如果有装备武器，先显示一轮（否则要等第一次操作才出现）
        if (HasWeapon())
            ShowTemporarily();
    }

    private void OnDestroy()
    {
        // 退订事件，防止内存泄漏（脚本销毁后不再响应）
        if (inventory != null)
            inventory.onChanged -= OnInventoryChanged;
    }

    /// <summary> 背包有任何变化（开枪/换枪/补弹）→ 显示 HUD 并刷新 3 秒计时 </summary>
    private void OnInventoryChanged()
    {
        ShowTemporarily();
    }

    /// <summary> 显示 HUD，并记录显示时间（之后 3 秒内不会隐藏） </summary>
    private void ShowTemporarily()
    {
        if (bgGO == null) return;
        bgGO.SetActive(true);
        lastShowTime = Time.time;
    }

    /// <summary> 当前是否装备了武器（枪或近战） </summary>
    private bool HasWeapon()
    {
        if (inventory == null) return false;
        InventoryItem equipped = inventory.GetEquippedItem();
        return equipped != null && equipped.gunData != null;
    }

    /// <summary> 用代码动态创建 UI（和你项目里 InventoryUI 的风格保持一致） </summary>
    private void CreateAmmoUI()
    {
        // 1. 创建画布：ScreenSpaceOverlay = 屏幕最上层，2D 游戏最常用
        GameObject canvasGO = new GameObject("AmmoHUD");
        Canvas canvas = canvasGO.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 150; // 比背包(200)低、比拾取提示(100)高，互不遮挡
        canvasGO.AddComponent<CanvasScaler>().uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        canvasGO.AddComponent<GraphicRaycaster>();

        // 2. 半透明黑色底板：让文字在任何背景下都看得清
        bgGO = new GameObject("BG");
        bgGO.transform.SetParent(canvasGO.transform, false);
        Image bgImg = bgGO.AddComponent<Image>();
        bgImg.color = new Color(0, 0, 0, 0.6f);
        RectTransform bgRect = bgGO.GetComponent<RectTransform>();
        bgRect.anchorMin = new Vector2(1f, 0f); // 锚定屏幕右下角
        bgRect.anchorMax = new Vector2(1f, 0f);
        bgRect.pivot = new Vector2(1f, 0f);     // 以右下角为中心点
        bgRect.anchoredPosition = new Vector2(-20f, 20f); // 离右下角 20 像素
        bgRect.sizeDelta = new Vector2(240f, 52f);        // 底板尺寸

        // 3. 弹药文字：底板内部填满
        GameObject textGO = new GameObject("Text");
        textGO.transform.SetParent(bgGO.transform, false);
        ammoText = textGO.AddComponent<Text>();
        ammoText.fontSize = 24;
        ammoText.color = Color.white;
        ammoText.alignment = TextAnchor.MiddleCenter;
        ammoText.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        RectTransform textRect = textGO.GetComponent<RectTransform>();
        textRect.anchorMin = Vector2.zero;
        textRect.anchorMax = Vector2.one;
        textRect.offsetMin = Vector2.zero;
        textRect.offsetMax = Vector2.zero;

        // 默认隐藏：只有装备武器/操作时才显示
        bgGO.SetActive(false);
    }

    private void Update()
    {
        if (inventory == null || ammoText == null || bgGO == null) return;

        // 读取当前装备的物品条目
        InventoryItem equipped = inventory.GetEquippedItem();

        // 没装备武器（或装备的是钥匙/宝石等）→ 整个黑框隐藏，不占画面
        if (equipped == null || equipped.gunData == null)
        {
            bgGO.SetActive(false);
            return;
        }

        // 换弹中：显示"换弹中…"，并且这期间不隐藏（不受 3 秒限制）
        if (gun != null && gun.IsReloading)
        {
            bgGO.SetActive(true);
            ammoText.text = "换弹中…";
            return;
        }

        // 有武器：刷新文字内容（子弹 or 耐久）
        bool isRanged = equipped.gunData.range > 1f;
        if (isRanged)
        {
            // 远程武器 → 显示「弹夹当前 / 备弹」：手枪  12 / 30
            // 前面的数字 = 弹夹里随时能打出去的；后面的数字 = 备用弹药池
            int reserve = inventory.GetReserveAmmo(equipped.gunData.ammoType);
            ammoText.text = equipped.itemName + "  " + equipped.currentAmmo + " / " + reserve;
        }
        else if (equipped.maxDurability > 0)
        {
            // 近战武器（有耐久）→ 显示耐久：小刀  耐久 18 / 20
            ammoText.text = equipped.itemName + "  耐久 " + equipped.currentDurability + " / " + equipped.maxDurability;
        }
        else
        {
            // 无限耐久的近战武器 → 显示武器名即可
            ammoText.text = equipped.itemName;
        }

        // 3 秒留存：显示时长超过 displayDuration 秒后，自动隐藏
        if (Time.time - lastShowTime > displayDuration)
            bgGO.SetActive(false);
    }
}
