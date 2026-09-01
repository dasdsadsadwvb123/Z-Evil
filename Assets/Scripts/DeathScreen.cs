using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 死亡界面：玩家死亡时弹出"你死了…"，按 R 从存档点继续（读档）。
/// 挂到玩家（Player）物体上（和 HealthSystem、SaveSystem 同物体）。
///
/// 原理：订阅 HealthSystem 的 OnDeath 事件 → 显示死亡遮罩界面 → 按 R 调用存档系统读档。
/// 不暂停世界（丧尸还在动，紧张感拉满）。
/// </summary>
public class DeathScreen : MonoBehaviour
{
    [Header("按键")]
    public KeyCode loadKey = KeyCode.R;

    private HealthSystem health;
    private SaveSystem saveSystem;
    private GameObject deathUI;
    private Text hintText;
    private bool isDead = false;

    private void Start()
    {
        // 订阅玩家死亡事件
        health = GetComponent<HealthSystem>();
        if (health != null)
            health.OnDeath += OnPlayerDeath;
        else
            Debug.LogWarning("[死亡界面] 玩家身上没有 HealthSystem，请把脚本挂对物体！", gameObject);

        saveSystem = GetComponent<SaveSystem>();

        CreateDeathUI(); // 创建界面，先隐藏
    }

    private void OnDestroy()
    {
        // 退订事件，防止内存泄漏
        if (health != null)
            health.OnDeath -= OnPlayerDeath;
    }

    /// <summary> 玩家死亡回调：显示死亡界面 </summary>
    private void OnPlayerDeath()
    {
        isDead = true;
        if (deathUI != null)
            deathUI.SetActive(true);

        // 死亡时清空背包的跨场景静态快照：
        // 防止"没存档却因为旧快照继承物品（比如复活有枪）"。
        // （如果按 R 读档，读档会重新恢复正确数据，不受影响）
        Inventory.ResetStaticState();
    }

    private void Update()
    {
        if (!isDead || deathUI == null) return;

        // 按 R 读档（世界不暂停，这里照常响应输入）
        if (Input.GetKeyDown(loadKey))
        {
            bool ok = saveSystem != null && saveSystem.LoadGame();
            if (!ok && hintText != null)
                hintText.text = "没有存档！请先到存档点按 F 存档";
        }
    }

    /// <summary> 创建死亡界面：全屏暗色遮罩 + 大字提示（初始隐藏） </summary>
    private void CreateDeathUI()
    {
        deathUI = new GameObject("DeathScreen");
        Canvas canvas = deathUI.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 400; // 盖在所有 UI 最上面
        deathUI.AddComponent<CanvasScaler>().uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        deathUI.AddComponent<GraphicRaycaster>();

        // 全屏暗红遮罩
        GameObject bg = new GameObject("BG");
        bg.transform.SetParent(deathUI.transform, false);
        Image bgImg = bg.AddComponent<Image>();
        bgImg.color = new Color(0.2f, 0f, 0f, 0.75f); // 暗红色，死亡氛围
        RectTransform bgRect = bg.GetComponent<RectTransform>();
        bgRect.anchorMin = Vector2.zero;
        bgRect.anchorMax = Vector2.one;
        bgRect.offsetMin = Vector2.zero;
        bgRect.offsetMax = Vector2.zero;

        // 主标题：你死了…
        GameObject title = new GameObject("Title");
        title.transform.SetParent(deathUI.transform, false);
        Text titleText = title.AddComponent<Text>();
        titleText.text = "你 死 了…";
        titleText.fontSize = 72;
        titleText.color = new Color(1f, 0.25f, 0.25f);
        titleText.alignment = TextAnchor.MiddleCenter;
        titleText.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        RectTransform titleRect = title.GetComponent<RectTransform>();
        titleRect.anchorMin = new Vector2(0.5f, 0.55f);
        titleRect.anchorMax = new Vector2(0.5f, 0.55f);
        titleRect.pivot = new Vector2(0.5f, 0.5f);
        titleRect.anchoredPosition = Vector2.zero;
        titleRect.sizeDelta = new Vector2(600, 120);

        // 提示：按 R 读档
        GameObject hint = new GameObject("Hint");
        hint.transform.SetParent(deathUI.transform, false);
        hintText = hint.AddComponent<Text>();
        hintText.text = "按 R 从存档点继续";
        hintText.fontSize = 30;
        hintText.color = Color.yellow;
        hintText.alignment = TextAnchor.MiddleCenter;
        hintText.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        RectTransform hintRect = hint.GetComponent<RectTransform>();
        hintRect.anchorMin = new Vector2(0.5f, 0.35f);
        hintRect.anchorMax = new Vector2(0.5f, 0.35f);
        hintRect.pivot = new Vector2(0.5f, 0.5f);
        hintRect.anchoredPosition = Vector2.zero;
        hintRect.sizeDelta = new Vector2(600, 60);

        deathUI.SetActive(false);
    }
}
