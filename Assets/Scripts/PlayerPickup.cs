using UnityEngine;
using UnityEngine.UI;

public class PlayerPickup : MonoBehaviour
{
    [Header("检测设置")]
    public float pickupRange = 1.5f;
    public LayerMask itemLayer = -1;

    [Header("交互键")]
    public KeyCode interactKey = KeyCode.F;

    [Header("提示文字")]
    public string promptText = "按F拾取";

    [Header("拾取音效")]
    [Tooltip("默认拾取音效（物品自己没配 pickupClip 时用它；不拖 = 静音）")]
    public AudioClip defaultPickupClip;

    private Inventory inventory;
    private PickupItem nearestItem;
    private GameObject promptUI;
    private Text promptTextUI;   // 提示文字引用（用于关键物品变色）
    private PixelGridMovement playerMovement;

   private void Start()
   {
       inventory = GetComponent<Inventory>();
        playerMovement = GetComponent<PixelGridMovement>();
       CreatePromptUI();
    }

    private void CreatePromptUI()
    {
        promptUI = new GameObject("PickupPrompt");
        Canvas canvas = promptUI.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 100;
        UIScale.Setup(promptUI);
        promptUI.AddComponent<GraphicRaycaster>();

        GameObject bg = new GameObject("BG");
        bg.transform.SetParent(promptUI.transform, false);
        Image bgImg = bg.AddComponent<Image>();
        bgImg.color = new Color(0, 0, 0, 0.5f);
        RectTransform bgRect = bg.GetComponent<RectTransform>();
        bgRect.anchorMin = new Vector2(0.5f, 0f);
        bgRect.anchorMax = new Vector2(0.5f, 0f);
        bgRect.pivot = new Vector2(0.5f, 0.5f);
        bgRect.anchoredPosition = new Vector2(0, 50f);
        bgRect.sizeDelta = new Vector2(300, 50);

        GameObject textGO = new GameObject("Text");
        textGO.transform.SetParent(bg.transform, false);
        promptTextUI = textGO.AddComponent<Text>();
        promptTextUI.text = promptText;
        promptTextUI.fontSize = 20;
        promptTextUI.color = Color.white;
        promptTextUI.alignment = TextAnchor.MiddleCenter;
        promptTextUI.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        RectTransform textRect = textGO.GetComponent<RectTransform>();
        textRect.anchorMin = Vector2.zero;
        textRect.anchorMax = Vector2.one;
        textRect.offsetMin = Vector2.zero;
        textRect.offsetMax = Vector2.zero;

        promptUI.SetActive(false);
    }

    private void Update()
    {
        if (inventory == null) return;

        nearestItem = FindNearestItem();

        if (nearestItem != null)
        {
            promptUI.SetActive(true);

            // 关键物品：提示文字变金色，强化"这是重要东西"的引导
            if (promptTextUI != null)
                promptTextUI.color = nearestItem.isKeyItem ? new Color(1f, 0.85f, 0.3f) : Color.white;

            if (Input.GetKeyDown(interactKey))
            {
                // 拾取音效：物品专属音效优先，否则用玩家默认音效（两者都没拖 = 静音，不报错）；走距离听声惯例
                AudioClip clip = nearestItem.pickupClip != null ? nearestItem.pickupClip : defaultPickupClip;
                AudibleAudio.PlayAt(clip, nearestItem.transform.position, nearestItem.pickupVolume);

                // 纸条：不进背包，登记到纸条收集表（按拾取逐条，互不覆盖）——修"只显示最后一张"
                NotePaper note = nearestItem.GetComponent<NotePaper>();
                if (note != null)
                {
                    note.CollectSelf();
                    inventory.OnItemNotice?.Invoke(note.pickupMessage); // 底部提示（默认"获得：一张纸条"）
                }
                // 普通物品：按 addToInventory 决定是否放进 TAB 背包
                else if (nearestItem.addToInventory)
                {
                    inventory.AddItem(nearestItem);
                }

                playerMovement?.PlayFlash();
                if (nearestItem.destroyOnPickup)
                {
                    // 记录"已拾取"，防止场景重载后物品复活（防刷物品）。
                    // 没填 itemID 时用物体名兜底（防止弹药包之类无限刷）
                    SaveSystem.CollectItem(nearestItem.itemID, nearestItem.gameObject.name);
                    Destroy(nearestItem.gameObject);
                }
            }
        }
        else
        {
            promptUI.SetActive(false);
        }
    }

    private PickupItem FindNearestItem()
    {
        PickupItem closest = null;
        float bestDist = float.MaxValue;

        // 按距离检测场景中所有存活的 PickupItem
        PickupItem[] all = FindObjectsOfType<PickupItem>();
        foreach (PickupItem p in all)
        {
            if (!p.gameObject.activeInHierarchy) continue;
            float dist = Vector2.Distance(transform.position, p.transform.position);

            // 初筛：玩家基准范围 与 物品自身范围 取大者（物品把范围调大时，不会被玩家范围提前挡掉）
            float broad = Mathf.Max(pickupRange, p.pickupRange);
            if (dist > broad) continue;
            // 精确判定：以物品自身范围为准（<= 物品 pickupRange 才算在范围内）→ 范围外不提示、不能拾取
            if (dist > p.pickupRange) continue;

            if (dist < bestDist)
            {
                bestDist = dist;
                closest = p;
            }
        }
        return closest;
    }

    private void OnDestroy()
    {
        if (promptUI != null) Destroy(promptUI);
    }
}
