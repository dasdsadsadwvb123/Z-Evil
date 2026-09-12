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
    [Tooltip("房间名：进圈提示优先显示这行字（所有传送器通用；不填走系统默认提示链）")]
    public string roomName = "";
    [Tooltip("按 F 后显示的文字：填了就覆盖系统默认提示——卡住/缺钥匙/机关锁时显示它、传送时以 toast 弹出；不填走系统默认")]
    public string fMessage = "";

    [Header("宝石铁门（可选：拖入后按 F 不传送，改为弹背包镶嵌宝石；不拖 = 普通传送门零漂移）")]
    [Tooltip("绑定同物体或门位置的 GemDoor 组件：三颗宝石没镶完时按 F 只弹背包")]
    public GemDoor gemDoor;

    [Header("卡住的门（优先级最高：进圈统一显示'门被卡住了'，按 F 无效——其他一切锁都开不了它）")]
    [Tooltip("门被卡住：勾上后无论钥匙/机关/宝石怎么解都不开（剧情上'永久卡死'的门）。以后想'撞开'就调 Unstick()")]
    public bool stuck = false;

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
    private string worldKey;           // 世界进度表钥匙（读档还原"解锁没解锁/卡没卡住"）

    private void Start()
    {
        isLocked = startLocked; // 默认 false：没接机关的旧传送门行为完全不变

        // 读档自查：世界进度表记录过解锁/卡住状态 → 还原（无记录则维持 Inspector 设置）
        worldKey = WorldState.KeyFor("Portal", this);
        if (WorldState.Has(worldKey))
            isLocked = WorldState.Get(worldKey) == 0; // 1=已解锁 0=锁定
        if (WorldState.Has(worldKey + "_Stuck"))
            stuck = WorldState.Get(worldKey + "_Stuck") != 0;

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
            // 卡住的门：优先级最高的守卫——按 F 永远无效（抖动反馈"按了也没用"），压过板锁/宝石门/钥匙锁/传送
            if (stuck)
            {
                Debug.Log("[传送门] " + name + " 被卡住了，按不开");
                UpdatePromptText(string.IsNullOrEmpty(fMessage) ? "门被卡住了" : fMessage); // 自定义 F 文字优先
                StartCoroutine(ShakeText());
                return;
            }

            // 锁定守卫：机关没激活时按 F 不传送——自定义 F 文字优先，没填用锁定文案 + 抖动反馈
            if (isLocked)
            {
                Debug.Log("[传送门] 机关未激活，出口锁定中");
                UpdatePromptText(string.IsNullOrEmpty(fMessage) ? lockedPromptText : fMessage);
                StartCoroutine(ShakeText());
                return;
            }

            // 宝石铁门：三颗宝石没镶完时按 F 不传送——弹出玩家背包（在里面选中宝石按 E 镶嵌）。
            // 镶完后 gemDoor.IsComplete = true → 走到下面正常传送判定。
            if (gemDoor != null && !gemDoor.IsComplete)
            {
                InventoryUI bag = FindObjectOfType<InventoryUI>();
                if (bag != null) bag.OpenExternal();
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
                    // 自定义 F 文字优先：填了 fMessage 就显示它，没填显示"需要：XX钥匙"
                    UpdatePromptText(string.IsNullOrEmpty(fMessage) ? "需要：" + keyName : fMessage);
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

            // 传送防重入：一次转场（渐黑/加载/渐亮）还没结束时，忽略新的传送请求。
            // 根因说明：连按两下 F 时，第二次会落在第一次的转场窗口里——旧逻辑的"转场中立即执行"会让场景被重复加载，
            // 把 spawn 数据吃空 → 玩家落到地图初始出生点。这里直接丢弃第二次，只保证第一次正常生效。
            if (TeleportManager.IsTeleporting)
            {
                Debug.Log("[传送门] 传送进行中，忽略本次重复请求");
                return;
            }

           // 自定义 F 文字：填了就以 toast 弹出（传送瞬间玩家出圈会关掉提示框，toast 恒显 2 秒更稳），没填静默传送
           if (!string.IsNullOrEmpty(fMessage))
           {
               if (playerInventory == null)
               {
                   GameObject pp = GameObject.FindGameObjectWithTag("Player");
                   if (pp != null) playerInventory = pp.GetComponent<Inventory>();
               }
               playerInventory?.OnItemNotice?.Invoke(fMessage);
           }

           Debug.Log("玩家按下交互键，传送至：" + targetSceneName);
           TeleportTracker.CountTeleport();

            if (teleportInSameScene)
            {
                TeleportManager.Instance.SetSpawnPosition(spawnPosition);
                PixelGridMovement pm = FindObjectOfType<PixelGridMovement>();
                // 同场景传送：渐黑 → 瞬移（黑透瞬间执行，玩家看不到"人闪现"）→ 渐亮
                FadeController.Transition(() => { if (pm != null) pm.TeleportTo(spawnPosition); });
            }
            else
            {
                TeleportManager.Instance.SetSpawnPosition(spawnPosition);
                // 跨场景传送：渐黑 0.6s → 加载新场景 → 渐亮 0.8s
                FadeController.TransitionToScene(targetSceneName);
            }
        }
    }

    // ======== 锁定/解锁（给 PlateSwitchBoard 的 UnityEvent 接线用） ========

    /// <summary> 解除卡住状态（预留：以后剧情"门被撞开"之类接线用，本期不接） </summary>
    public void Unstick()
    {
        stuck = false;
        WorldState.Set(worldKey + "_Stuck", 0); // 世界进度表登记"不再卡住"
        Debug.Log("[传送门] " + name + " 不再卡住了");
        if (playerInRange && promptUIText != null) UpdatePromptText(GetEnterPromptText());
    }

    /// <summary> 锁定传送门（机关复位时调）：按 F 不传送，提示换锁定文案 </summary>
    public void Lock()
    {
        isLocked = true;
        WorldState.Set(worldKey, 0); // 世界进度表登记"锁定"
        Debug.Log("[传送门] " + name + " 已锁定");
        // 卡住的门保持"门被卡住了"文案（GetEnterPromptText 里 stuck 优先级最高）
        if (playerInRange && promptUIText != null) UpdatePromptText(GetEnterPromptText());
    }

    /// <summary> 解锁传送门（全部压力板压下时调）：按 F 正常传送 </summary>
    public void Unlock()
    {
        isLocked = false;
        WorldState.Set(worldKey, 1); // 世界进度表登记"已解锁"
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

    /// <summary> 进圈提示：roomName 自定义最优先（填了就显示它）；为空走系统默认链——板锁 = 锁定文案；宝石门未镶完 = doorName；requireKey = 原提示；普通 = 原提示。
    /// （"门被卡住了"不在进圈提示里——它只在按 F 时出现，见 Update 的 stuck 守卫） </summary>
    private string GetEnterPromptText()
    {
        if (!string.IsNullOrEmpty(roomName)) return roomName;
        if (isLocked) return lockedPromptText;
        if (gemDoor != null && !gemDoor.IsComplete) return gemDoor.doorName;
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
