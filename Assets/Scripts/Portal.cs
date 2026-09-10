using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

public class Portal : MonoBehaviour
{
    [Header("目标场景")]
   [SerializeField] private string targetSceneName = "Out of hospital";
    [Header("传送设置")]
    [SerializeField] private bool teleportInSameScene = true;

   [Header("目标出生位置")]
    [SerializeField] private Vector2 spawnPosition = Vector2.zero;

    [Header("交互设置")]
    [SerializeField] private string promptText = "按F进行交互";
   [SerializeField] private KeyCode interactKey = KeyCode.F;
   [SerializeField] private float promptFontSize = 28f;
   [SerializeField] private Color promptColor = Color.white;

    [Header("锁定设置（推箱子机关用；不接机关就别管，默认不锁 = 旧场景零漂移）")]
    [SerializeField] private bool startLocked = false;
    [SerializeField] private string lockedPromptText = "机关未激活，出口被锁住了……";

    [Header("钥匙设置（勾选'需要钥匙'后：按 F 必须持有对应卡槽的钥匙才能传送）")]
    [Tooltip("需要钥匙：勾上后按 F 会先查背包有没有对应钥匙，没有就提示需要哪把")]
    public bool requireKey = false;
    [Tooltip("用 KeyDatabase 里的哪个卡槽（0~7）：卡槽里填 keyID + 钥匙显示名")]
    [Range(0, 7)]
    public int keySlot = 0;
    [Tooltip("房间名：进圈提示显示'XX室——按 F 进入'（不填就用上面的普通提示文字）")]
    public string roomName = "";

    [Header("破门设置")]
    [SerializeField] private bool needBreach = false;
    [SerializeField] private int breachCount = 4;
    [SerializeField] private AudioSource breachSound;
    [SerializeField] private string[] breachTexts;
    [SerializeField] private AudioSource breachFinishSound;

   private bool playerInRange = false;
   private GameObject promptUI;
    private int currentBreachHits = 0;
    private Text promptUIText;
    private bool isLocked = false; // 锁定中 = 按 F 不传送（推箱子机关控制，Lock/Unlock 由开关板调）
    private Inventory playerInventory; // 玩家背包（钥匙判定用；按 F 时懒查找，不依赖加载顺序）

    private void Start()
    {
        isLocked = startLocked; // 默认 false：没接机关的旧传送门行为完全不变
        CreatePromptUI();
    }

    private void CreatePromptUI()
    {
        promptUI = new GameObject("PortalPrompt");
        Canvas canvas = promptUI.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 100;

        CanvasScaler scaler = promptUI.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);

        promptUI.AddComponent<GraphicRaycaster>();

        GameObject panelGO = new GameObject("Background");
        panelGO.transform.SetParent(promptUI.transform, false);
        Image panelImage = panelGO.AddComponent<Image>();
        panelImage.color = new Color(0, 0, 0, 0.5f);
        RectTransform panelRect = panelGO.GetComponent<RectTransform>();
        panelRect.anchorMin = new Vector2(0.5f, 0f);
        panelRect.anchorMax = new Vector2(0.5f, 0f);
        panelRect.pivot = new Vector2(0.5f, 0.5f);
        panelRect.anchoredPosition = new Vector2(0, 50f);
        panelRect.sizeDelta = new Vector2(320, 60);

        GameObject textGO = new GameObject("PromptText");
        textGO.transform.SetParent(panelGO.transform, false);
        Text promptTextComponent = textGO.AddComponent<Text>();
       promptTextComponent.text = promptText;
       promptTextComponent.fontSize = (int)promptFontSize;
        promptUIText = promptTextComponent;
       promptTextComponent.color = promptColor;
        promptTextComponent.alignment = TextAnchor.MiddleCenter;
        promptTextComponent.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        promptTextComponent.supportRichText = false;

        RectTransform textRect = textGO.GetComponent<RectTransform>();
        textRect.anchorMin = Vector2.zero;
        textRect.anchorMax = Vector2.one;
        textRect.pivot = new Vector2(0.5f, 0.5f);
        textRect.offsetMin = Vector2.zero;
        textRect.offsetMax = Vector2.zero;

        promptUI.SetActive(false);
    }

    private void OnTriggerEnter2D(Collider2D other)
    {
        if (other.CompareTag("Player"))
        {
            playerInRange = true;
       if (promptUI != null)
           promptUI.SetActive(true);
        currentBreachHits = 0;
        // 进圈提示按状态显示：板锁 = 锁定文案；要钥匙 = "房间名——按 F 进入"；普通 = 原提示
        if (promptUIText != null)
            UpdatePromptText(GetEnterPromptText());
   }
    }

    private void OnTriggerExit2D(Collider2D other)
    {
        if (other.CompareTag("Player"))
        {
            playerInRange = false;
            if (promptUI != null)
                promptUI.SetActive(false);
        }
    }

    private void Update()
    {
       if (playerInRange && Input.GetKeyDown(interactKey))
       {
            // 锁定守卫：机关没激活时按 F 不传送——提示换锁定文案 + 抖一下（复用现有反馈，玩家知道"按了也没用"）
            if (isLocked)
            {
                Debug.Log("[传送门] 机关未激活，出口锁定中");
                UpdatePromptText(lockedPromptText);
                StartCoroutine(ShakeText());
                return;
            }

            // 钥匙守卫：勾了"需要钥匙"但背包里没有对应卡槽的钥匙 → 不传送，提示需要哪把 + 抖动反馈
            // （与板锁 isLocked 独立判定，两套锁任一锁着都不能传）
            if (requireKey)
            {
                string keyID = KeyDatabase.GetKeyID(keySlot);
                if (playerInventory == null)
                {
                    GameObject p = GameObject.FindGameObjectWithTag("Player");
                    if (p != null) playerInventory = p.GetComponent<Inventory>();
                }
                if (playerInventory == null || !playerInventory.HasItem(keyID))
                {
                    string keyName = KeyDatabase.GetKeyName(keySlot);
                    Debug.Log("[传送门] " + name + " 需要【" + keyName + "】（卡槽 " + keySlot + "，ID=" + keyID + "），背包里没有，禁止传送");
                    UpdatePromptText("需要：" + keyName);
                    StartCoroutine(ShakeText());
                    return;
                }
            }

            if (needBreach && currentBreachHits < breachCount)
            {
                currentBreachHits++;
                if (breachSound != null) breachSound.Play();
                if (currentBreachHits < breachCount)
                {
                    int idx = breachTexts != null ? Mathf.Min(currentBreachHits - 1, breachTexts.Length - 1) : 0;
                    UpdatePromptText(breachTexts != null && breachTexts.Length > idx ? breachTexts[idx] : "撞击中...");
                    StartCoroutine(ShakeText());
                }
                else
                {
                    if (breachFinishSound != null) breachFinishSound.Play();
                    UpdatePromptText(promptText);
                }
                return;
            }

           Debug.Log("玩家按下交互键，传送至：" + targetSceneName);
           TeleportTracker.CountTeleport();

            if (teleportInSameScene)
            {
                TeleportManager.Instance.SetSpawnPosition(spawnPosition);
                PixelGridMovement pm = FindObjectOfType<PixelGridMovement>();
                if (pm != null) pm.TeleportTo(spawnPosition);
            }
            else
            {
                TeleportManager.Instance.SetSpawnPosition(spawnPosition);
                SceneManager.LoadScene(targetSceneName);
            }
        }
    }

    // ======== 锁定/解锁（给 PlateSwitchBoard 的 UnityEvent 接线用） ========

    /// <summary> 锁定传送门（机关复位时调）：按 F 不传送，提示换锁定文案 </summary>
    public void Lock()
    {
        isLocked = true;
        Debug.Log("[传送门] " + name + " 已锁定");
        if (playerInRange && promptUIText != null) UpdatePromptText(lockedPromptText);
    }

    /// <summary> 解锁传送门（全部压力板压下时调）：按 F 正常传送 </summary>
    public void Unlock()
    {
        isLocked = false;
        Debug.Log("[传送门] " + name + " 已解锁，可以按 F 出去了！");
        if (playerInRange && promptUIText != null) UpdatePromptText(GetEnterPromptText());
    }

    private void OnDestroy()
    {
       if (promptUI != null)
           Destroy(promptUI);
    }
    private void UpdatePromptText(string text)
    {
        if (promptUIText != null)
            promptUIText.text = text;
    }

    /// <summary> 进圈提示：板锁 = 锁定文案；要钥匙 = "房间名——按 F 进入"（没填房间名用普通提示）；普通 = 原提示 </summary>
    private string GetEnterPromptText()
    {
        if (isLocked) return lockedPromptText;
        if (requireKey && !string.IsNullOrEmpty(roomName))
            return roomName + "——按 F 进入";
        return promptText;
    }

    private System.Collections.IEnumerator ShakeText()
    {
        if (promptUIText == null) yield break;
        RectTransform rt = promptUIText.GetComponent<RectTransform>();
        Vector2 originalPos = rt.anchoredPosition;
        float elapsed = 0f;
        while (elapsed < 0.15f)
        {
            float offset = Mathf.Sin(elapsed * 60f) * 5f;
            rt.anchoredPosition = originalPos + new Vector2(offset, 0);
            elapsed += Time.deltaTime;
            yield return null;
        }
        rt.anchoredPosition = originalPos;
    }
}
