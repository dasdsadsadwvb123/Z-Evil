using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 弹药 HUD：屏幕右下角实时显示当前装备武器的剩余子弹。
/// 挂到玩家（Player）物体上，需要和 Inventory 在同一个物体。
/// </summary>
public class AmmoUI : MonoBehaviour
{
    private Inventory inventory;
    private Text ammoText;

    private void Start()
    {
        inventory = GetComponent<Inventory>();
        CreateAmmoUI();
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
        GameObject bg = new GameObject("BG");
        bg.transform.SetParent(canvasGO.transform, false);
        Image bgImg = bg.AddComponent<Image>();
        bgImg.color = new Color(0, 0, 0, 0.6f);
        RectTransform bgRect = bg.GetComponent<RectTransform>();
        bgRect.anchorMin = new Vector2(1f, 0f); // 锚定屏幕右下角
        bgRect.anchorMax = new Vector2(1f, 0f);
        bgRect.pivot = new Vector2(1f, 0f);     // 以右下角为中心点
        bgRect.anchoredPosition = new Vector2(-20f, 20f); // 离右下角 20 像素
        bgRect.sizeDelta = new Vector2(240f, 52f);        // 底板尺寸

        // 3. 弹药文字：底板内部填满
        GameObject textGO = new GameObject("Text");
        textGO.transform.SetParent(bg.transform, false);
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
    }

    private void Update()
    {
        if (inventory == null || ammoText == null) return;

        // 读取当前装备的物品条目
        InventoryItem equipped = inventory.GetEquippedItem();

        // 没装备枪（或装备的是钥匙/宝石等）→ 不显示弹药
        if (equipped == null || equipped.gunData == null)
        {
            ammoText.text = "";
            return;
        }

        // 显示格式：手枪  12 / 15 发
        ammoText.text = equipped.itemName + "  " + equipped.currentAmmo + " / " + equipped.maxAmmo + " 发";
    }
}
