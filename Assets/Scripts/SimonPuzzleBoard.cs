using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 心跳复律谜题——交互入口（挂在除颤仪物体上，Container/管路板同款交互套路）：
/// 靠近按 F → 打开 SimonPuzzleUI 记忆序列谜题；连对 5 轮 → 除颤成功 → 钥匙卡掉在仪器前
/// （对接现有钥匙-传送器系统：Key Item ID 填进 KeyDatabase 卡槽，传送器勾 Need Key）。
/// 解开一次后永久安静（不重复刷钥匙；读档重置）。
/// 音效：成功"滋"声/错误音是可配槽（不拖 = 用代码生成的替代音，零素材）。
/// </summary>
public class SimonPuzzleBoard : MonoBehaviour
{
    [Header("钥匙卡奖励（成功后掉在仪器前；对接现有钥匙-传送器系统）")]
    [Tooltip("钥匙卡 itemID：小泽把它填进 KeyDatabase 对应卡槽的 Key ID，传送器勾需要钥匙选那个卡槽")]
    public string keyItemID = "DefibKeyCard";
    [Tooltip("钥匙卡显示名（拾取 toast / 背包里显示）")]
    public string keyItemName = "钥匙卡";
    [Tooltip("钥匙卡图标（可选；不拖 = 掉在地上的卡没有图但能捡）")]
    public Sprite keyIcon;
    [Tooltip("钥匙卡掉落位置 = 除颤仪位置 + 这个偏移（默认仪器前偏下）")]
    public Vector2 spawnOffset = new Vector2(0f, -1.2f);

    [Header("音效（可配槽；不拖 = 用代码生成的替代音）")]
    [Tooltip("除颤成功'滋'声（连对 5 轮时播）")]
    public AudioClip successClip;
    [Tooltip("复律错误音（输错时播）")]
    public AudioClip errorClip;

    [Header("交互")]
    public float interactRange = 1.5f;

    /// <summary> 是否已解开（解开一次后永久安静；读档重置） </summary>
    public bool Solved { get; private set; } = false;

    private Transform player;
    private GameObject promptUI;
    private Text promptTextUI;
    private Inventory playerInventory; // 借 OnItemNotice 通道发 toast（钥匙拾取/独白）
    private AudioSource audioSource;   // 成功/错误音播放（代码生成的替代音也从这播）

    private void Start()
    {
        GameObject p = GameObject.FindGameObjectWithTag("Player");
        if (p != null) player = p.transform;

        audioSource = GetComponent<AudioSource>();
        if (audioSource == null) audioSource = gameObject.AddComponent<AudioSource>();
        audioSource.playOnAwake = false;

        CreatePromptUI();
    }

    private void Update()
    {
        if (Solved || player == null) return;
        if (SimonPuzzleUI.IsOpen) { if (promptUI != null) promptUI.SetActive(false); return; }

        bool near = Vector2.Distance(transform.position, player.position) <= interactRange;
        if (promptUI != null)
        {
            promptUI.SetActive(near);
            if (promptTextUI != null) promptTextUI.text = "按F 检查除颤仪";
        }

        if (near && Input.GetKeyDown(KeyCode.F))
        {
            SimonPuzzleUI.Open(this); // 打开谜题（界面自己管暂停/关闭/轮次）
        }
    }

    /// <summary> 播成功音（可配槽；没拖用代码生成的"滋"声） </summary>
    public void PlaySuccess()
    {
        if (successClip != null) audioSource.PlayOneShot(successClip);
        else SimonTones.PlayZap(audioSource); // 代码生成的扫频"滋"
    }

    /// <summary> 播错误音（可配槽；没拖用代码生成的低频错误音） </summary>
    public void PlayError()
    {
        if (errorClip != null) audioSource.PlayOneShot(errorClip);
        else SimonTones.PlayError(audioSource);
    }

    /// <summary> 底部 toast（SimonPuzzleUI 调用：幽灵低音独白等） </summary>
    public void ShowToast(string message)
    {
        if (playerInventory == null)
        {
            GameObject p = GameObject.FindGameObjectWithTag("Player");
            if (p != null) playerInventory = p.GetComponent<Inventory>();
        }
        if (playerInventory != null) playerInventory.OnItemNotice?.Invoke(message);
    }

    /// <summary> 谜题通关（SimonPuzzleUI 回调）：演出音效 + 钥匙卡掉在仪器前 </summary>
    public void SolveSuccess()
    {
        if (Solved) return;
        Solved = true;

        PlaySuccess();
        ShowToast("除颤成功，机器吐出了什么……");

        // 钥匙卡掉在仪器前（代码生成 PickupItem，密码机钥匙/乌鸦掉落同源套路）
        GameObject go = new GameObject("掉落_" + keyItemName);
        go.transform.position = (Vector2)transform.position + spawnOffset;
        SpriteRenderer dropSr = go.AddComponent<SpriteRenderer>();
        dropSr.sprite = keyIcon;
        dropSr.sortingOrder = 10;
        PickupItem drop = go.AddComponent<PickupItem>();
        drop.itemID = keyItemID;
        drop.itemName = keyItemName;
        drop.itemType = ItemType.Key;
        drop.isKeyItem = true; // 关键物品金色闪烁引导（和密码机钥匙同款）
        drop.icon = keyIcon;
        drop.description = "除颤仪吐出来的卡。也许能打开某扇门。";
        drop.destroyOnPickup = true;
        Debug.Log("[心跳复律] 通关！钥匙卡已掉落：" + keyItemID, gameObject);

        if (promptUI != null) promptUI.SetActive(false);
    }

    // ======== 提示 UI（Container 同款轻量套路） ========
    private void CreatePromptUI()
    {
        promptUI = new GameObject("SimonPrompt");
        Canvas canvas = promptUI.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 100;
        promptUI.AddComponent<CanvasScaler>().uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        promptUI.AddComponent<GraphicRaycaster>();

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
        promptTextUI = textGO.AddComponent<Text>();
        promptTextUI.text = "按F 检查除颤仪";
        promptTextUI.fontSize = 20;
        promptTextUI.color = Color.white;
        promptTextUI.alignment = TextAnchor.MiddleCenter;
        promptTextUI.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        RectTransform textRect = promptTextUI.GetComponent<RectTransform>();
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
