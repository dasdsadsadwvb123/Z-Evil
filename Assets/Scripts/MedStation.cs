using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 医疗箱（一次性回满血装置）：靠近按 F → 玩家血量直接回到满状态 → 医疗箱消失（只能用一次）。
///
/// 设计要点：
/// - 回满不是"加 N 点"：直接 currentHealth = maxHealth——以后 maxHealth 调多少都自动适配；
/// - 一次性：使用后 Destroy 自己（"用完就没有了"）；没按 F 永远保留；
/// - 满血时按 F 也照常消耗（简单直接，不做"满血不消耗"的智能判断）；
/// - 死人不配用：玩家 isDead 时按 F 无效（不消耗、不回血）；
/// - 音效走 AudibleAudio.PlayAt 距离听声惯例（走近才听得见）。
///
/// 挂载：医疗箱物体上放 SpriteRenderer（小泽的医疗箱图）+ 本脚本。零其他依赖。
/// </summary>
public class MedStation : MonoBehaviour
{
    [Header("交互")]
    [Tooltip("玩家离医疗箱多近能按 F 使用")]
    public float interactRange = 1.5f;

    [Header("音效（不拖 = 静音；走距离听声惯例）")]
    [Tooltip("治疗音效（回满血那一刻播）")]
    public AudioClip healClip;

    private Transform player;
    private HealthSystem playerHealth; // 玩家血量系统（按 F 时懒查找）
    private Inventory playerInventory; // 玩家背包（借它的 OnItemNotice 通道发底部提示 toast）
    private GameObject promptUI;       // "按 F 使用医疗箱"提示（照 Cabinet 的轻量提示套路）
    private string worldKey;           // 世界进度表钥匙（记录"这个医疗箱已用掉"，读档后不再复活）

    private void Start()
    {
        // 读档自查：存档时这个医疗箱已经用掉了 → 直接消失，不建提示 UI
        // （worldKey = MedStation_场景名_层级路径，和 DoorLock 等同一套惯例）
        worldKey = WorldState.KeyFor("MedStation", this);
        if (WorldState.GetBool(worldKey))
        {
            Destroy(gameObject);
            return;
        }

        GameObject p = GameObject.FindGameObjectWithTag("Player");
        if (p != null) player = p.transform;
        else Debug.LogWarning("[医疗箱] 场景里没有 Player 标签的物体，没人能用我", gameObject);

        CreatePromptUI();
    }

    private void Update()
    {
        if (player == null) return;

        bool near = Vector2.Distance(transform.position, player.position) <= interactRange;
        if (promptUI != null) promptUI.SetActive(near);

        if (near && WorldInteractionBlocker.GetKeyDown(KeyCode.F))
        {
            // 懒查找玩家血量系统（第一次按 F 时找，之后缓存）
            if (playerHealth == null)
            {
                GameObject p = GameObject.FindGameObjectWithTag("Player");
                if (p != null) playerHealth = p.GetComponent<HealthSystem>();
            }
            if (playerHealth == null)
            {
                Debug.LogWarning("[医疗箱] 玩家身上没有 HealthSystem，用不了", gameObject);
                return;
            }

            // 死人不配用：不消耗、不回血（医疗箱保留给活人）
            if (playerHealth.isDead)
            {
                Debug.Log("[医疗箱] 玩家已经死了，医疗箱没有反应", gameObject);
                return;
            }

            // 回满血：直接把当前血量拉到上限（以后 maxHealth 调多少都自动适配）
            playerHealth.currentHealth = playerHealth.maxHealth;
            Debug.Log("[医疗箱] 血量回满：" + playerHealth.currentHealth + "/" + playerHealth.maxHealth);

            // 底部提示 toast："血量已回满"——走玩家背包的 OnItemNotice 通道
            //（和捡东西"获得 XX"同一条链路，PickupToast 订阅显示；UI 层恒定显示，不走距离听声）
            if (playerInventory == null)
            {
                GameObject p = GameObject.FindGameObjectWithTag("Player");
                if (p != null) playerInventory = p.GetComponent<Inventory>();
            }
            playerInventory?.OnItemNotice?.Invoke("血量已回满");

            // 治疗音效（距离听声惯例）
            AudibleAudio.PlayAt(healClip, transform.position);

            // 世界进度表登记"医疗箱已用掉"（读档后不再复活）——必须放在 Destroy 之前
            WorldState.Set(worldKey, 1);

            // 一次性：用完就没有了
            Destroy(gameObject);
        }
    }

    // ======== 提示 UI（照 Cabinet/PlayerPickup 的轻量提示套路） ========

    private void CreatePromptUI()
    {
        promptUI = new GameObject("MedStationPrompt");
        Canvas canvas = promptUI.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 100;

        GameObject bg = new GameObject("BG");
        bg.transform.SetParent(promptUI.transform, false);
        Image bgImg = bg.AddComponent<Image>();
        bgImg.color = new Color(0, 0, 0, 0.5f);
        RectTransform bgRect = bgImg.GetComponent<RectTransform>();
        bgRect.anchorMin = new Vector2(0.5f, 0f);
        bgRect.anchorMax = new Vector2(0.5f, 0f);
        bgRect.pivot = new Vector2(0.5f, 0.5f);
        bgRect.anchoredPosition = new Vector2(0, 50f);
        bgRect.sizeDelta = new Vector2(360, 50);

        GameObject textGO = new GameObject("Text");
        textGO.transform.SetParent(bg.transform, false);
        Text t = textGO.AddComponent<Text>();
        t.text = "按 F 使用医疗箱";
        t.fontSize = 20;
        t.color = new Color(0.4f, 1f, 0.5f); // 治疗绿：一眼认出"这是好东西"
        t.alignment = TextAnchor.MiddleCenter;
        t.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        RectTransform textRect = t.GetComponent<RectTransform>();
        textRect.anchorMin = Vector2.zero;
        textRect.anchorMax = Vector2.one;
        textRect.offsetMin = Vector2.zero;
        textRect.offsetMax = Vector2.zero;

        promptUI.SetActive(false);
    }

    private void OnDestroy()
    {
        if (promptUI != null) Destroy(promptUI);
    }
}
