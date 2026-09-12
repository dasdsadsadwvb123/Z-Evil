using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Events;
using System.Collections;

/// <summary>
/// 门锁系统：玩家持对应钥匙（按 itemID 配对，一把钥匙开一扇门）靠近门按 F
/// → 消耗钥匙 + 开门（渐隐或换开门图）→ 通过；无钥匙 → 提示"锁着"。
/// 挂到门物体上（门需要：SpriteRenderer + BoxCollider2D，且 Is Trigger 不要勾——
/// 门是墙，勾了玩家会穿墙；本脚本用距离检测，不需要触发器）。
/// 消耗钥匙复用 Inventory.RemoveItemAt（公开方法），不修改任何现有脚本。
/// </summary>
public class DoorLock : MonoBehaviour
{
    [Header("钥匙配对（一把钥匙开一扇门）")]
    [Tooltip("需要哪把钥匙：填钥匙 PickupItem 的 itemID，必须一字不差")]
    public string requiredKeyID = "OfficeKey";

    [Header("交互设置")]
    [Tooltip("提示文字（有钥匙时显示）")]
    public string promptText = "按F 开门";
    [Tooltip("无钥匙时的提示文字")]
    public string lockedText = "锁着……需要钥匙";
    [Tooltip("交互键（和 Portal 的 F 交互保持一致）")]
    public KeyCode interactKey = KeyCode.F;
    [Tooltip("离门多远内可以交互")]
    public float interactRange = 1.5f;
    [Tooltip("提示文字大小")]
    public float promptFontSize = 28f;

    [Header("开门动效（二选一，可同时配）")]
    [Tooltip("方案A：勾选后开门时门体渐隐消失")]
    public bool fadeOutSprite = true;
    [Tooltip("渐隐时长（秒）")]
    public float fadeDuration = 0.6f;
    [Tooltip("方案B：小泽自己做好的开门图拖到这里，开门瞬间换图（配了就优先用它，不渐隐）")]
    public Sprite openSprite;

    [Header("音效（可选，没配就不响）")]
    public AudioSource unlockSound;   // 开锁音
    public AudioSource lockedSound;   // 没钥匙时的"咔哒"拒绝音

    [Header("开锁成功后事件（可挂Portal激活等）")]
    public UnityEvent onUnlocked;

    private bool unlocked = false;          // 门是否已开（开过就不再响应）
    private string worldKey;                // 世界进度表钥匙（读档还原"这扇门开过没"）
    private Inventory inventory;            // 玩家背包（自动查找）
    private PixelGridMovement player;       // 玩家（自动查找，算距离用）
    private GameObject promptUI;            // 交互提示 UI（代码生成，同 Portal 套路）
    private Text promptUIText;
    private SpriteRenderer doorRenderer;
    private Collider2D doorCollider;
    private Coroutine lockedHintRoutine;

    private void Start()
    {
        inventory = FindObjectOfType<Inventory>();
        player = FindObjectOfType<PixelGridMovement>();
        doorRenderer = GetComponent<SpriteRenderer>();
        doorCollider = GetComponent<Collider2D>();
        CreatePromptUI();

        // 读档自查：世界进度表记录过"这扇门开过了" → 直接恢复开门结果（不播声、不再触发事件）
        worldKey = WorldState.KeyFor("Door", this);
        if (WorldState.GetBool(worldKey))
            ApplyOpenedState();
    }

    /// <summary> 恢复"已开门"的最终表现（读档用：换图/渐隐透明 + 关碰撞 + 不再响应交互） </summary>
    private void ApplyOpenedState()
    {
        unlocked = true;
        HidePrompt();
        if (doorCollider != null) doorCollider.enabled = false;

        if (openSprite != null && doorRenderer != null)
        {
            doorRenderer.sprite = openSprite;
        }
        else if (fadeOutSprite && doorRenderer != null)
        {
            Color c = doorRenderer.color;
            c.a = 0f;
            doorRenderer.color = c;
            doorRenderer.enabled = false;
        }
    }

    private void Update()
    {
        if (unlocked || player == null) return;

        // 距离检测：玩家走近才显示提示（门自身是实体墙，不能用触发器检测）
        bool inRange = Vector2.Distance(transform.position, player.transform.position) <= interactRange;

        if (inRange)
        {
            bool hasKey = inventory != null && inventory.HasItem(requiredKeyID);
            ShowPrompt(hasKey ? promptText : lockedText, hasKey ? Color.white : new Color(1f, 0.45f, 0.4f));

            if (Input.GetKeyDown(interactKey))
                TryUnlock(hasKey);
        }
        else
        {
            HidePrompt();
        }
    }

    /// <summary> 尝试开锁：有钥匙 → 消耗 + 开门；没钥匙 → 播拒绝音 + 抖提示 </summary>
    private void TryUnlock(bool hasKey)
    {
        if (!hasKey)
        {
            if (lockedSound != null) lockedSound.Play();
            if (lockedHintRoutine == null)
                lockedHintRoutine = StartCoroutine(LockedHintRoutine());
            return;
        }

        // 消耗钥匙：在背包里找到这把钥匙的位置，调公开的 RemoveItemAt 移除
        // （RemoveItemAt 会自动修正装备索引 + 存档快照，不用我们操心）
        if (!ConsumeKey())
        {
            Debug.LogWarning("[门锁] HasItem 说有钥匙但消耗失败，检查 itemID 是否一致：" + requiredKeyID, gameObject);
            return;
        }

        unlocked = true;
        HidePrompt();

        if (unlockSound != null) unlockSound.Play();

        // 世界进度表登记"这扇门开过了"（读档还原用）
        WorldState.Set(worldKey, 1);

        // 开门：先让玩家能穿过去（关掉实体碰撞），再换图/渐隐
        if (doorCollider != null) doorCollider.enabled = false;

        if (openSprite != null && doorRenderer != null)
        {
            // 方案B：小泽提供的开门图，直接换上
            doorRenderer.sprite = openSprite;
        }
        else if (fadeOutSprite && doorRenderer != null)
        {
            // 方案A：渐隐消失（没配开门图时的默认动效）
            StartCoroutine(FadeOutRoutine());
        }

        onUnlocked?.Invoke();
        Debug.Log("[门锁] 已用钥匙 " + requiredKeyID + " 开门", gameObject);
    }

    /// <summary> 在背包里按 itemID 找到钥匙并移除，找到返回 true </summary>
    private bool ConsumeKey()
    {
        if (inventory == null) return false;
        for (int i = 0; i < inventory.items.Count; i++)
        {
            if (inventory.items[i].itemID == requiredKeyID)
            {
                inventory.RemoveItemAt(i); // 公开方法：移除+修装备索引+存档
                return true;
            }
        }
        return false;
    }

    /// <summary> 没钥匙按门：提示文字抖一下强调"进不去" </summary>
    private IEnumerator LockedHintRoutine()
    {
        RectTransform rt = promptUIText != null ? promptUIText.GetComponent<RectTransform>() : null;
        Vector2 originalPos = rt != null ? rt.anchoredPosition : Vector2.zero;
        float elapsed = 0f;
        while (elapsed < 0.3f)
        {
            if (rt != null)
                rt.anchoredPosition = originalPos + new Vector2(Mathf.Sin(elapsed * 60f) * 5f, 0);
            elapsed += Time.deltaTime;
            yield return null;
        }
        if (rt != null) rt.anchoredPosition = originalPos;
        lockedHintRoutine = null;
    }

    /// <summary> 门体渐隐（方案A 动效） </summary>
    private IEnumerator FadeOutRoutine()
    {
        Color c = doorRenderer.color;
        float t = 0f;
        while (t < fadeDuration)
        {
            t += Time.deltaTime;
            c.a = Mathf.Clamp01(1f - t / fadeDuration);
            doorRenderer.color = c;
            yield return null;
        }
        doorRenderer.enabled = false; // 完全透明后连渲染一起关，省性能
    }

    // ======== 提示 UI（纯代码生成，风格和 Portal 的交互提示一致） ========

    private void CreatePromptUI()
    {
        promptUI = new GameObject("DoorPrompt", typeof(RectTransform)); // UI 物体必须带 RectTransform
        Canvas canvas = promptUI.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 100;
        UIScale.Setup(promptUI);
        promptUI.AddComponent<GraphicRaycaster>();

        GameObject bg = new GameObject("BG", typeof(RectTransform));
        bg.transform.SetParent(promptUI.transform, false);
        Image bgImg = bg.AddComponent<Image>();
        bgImg.color = new Color(0, 0, 0, 0.5f);
        RectTransform bgRect = bgImg.GetComponent<RectTransform>();
        bgRect.anchorMin = new Vector2(0.5f, 0f);
        bgRect.anchorMax = new Vector2(0.5f, 0f);
        bgRect.pivot = new Vector2(0.5f, 0.5f);
        bgRect.anchoredPosition = new Vector2(0, 50f);
        bgRect.sizeDelta = new Vector2(320, 60);

        GameObject textGO = new GameObject("Text", typeof(RectTransform));
        textGO.transform.SetParent(bg.transform, false);
        promptUIText = textGO.AddComponent<Text>();
        promptUIText.fontSize = (int)promptFontSize;
        promptUIText.alignment = TextAnchor.MiddleCenter;
        promptUIText.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        promptUIText.supportRichText = false;
        RectTransform textRect = promptUIText.GetComponent<RectTransform>();
        textRect.anchorMin = Vector2.zero;
        textRect.anchorMax = Vector2.one;
        textRect.offsetMin = Vector2.zero;
        textRect.offsetMax = Vector2.zero;

        promptUI.SetActive(false);
    }

    private void ShowPrompt(string text, Color color)
    {
        if (promptUI == null) return;
        if (promptUIText != null)
        {
            promptUIText.text = text;
            promptUIText.color = color;
        }
        promptUI.SetActive(true);
    }

    private void HidePrompt()
    {
        if (promptUI != null) promptUI.SetActive(false);
    }

    private void OnDestroy()
    {
        if (promptUI != null) Destroy(promptUI);
    }
}
