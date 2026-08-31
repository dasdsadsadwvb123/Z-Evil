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

    private Inventory inventory;
    private PickupItem nearestItem;
    private GameObject promptUI;
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
        promptUI.AddComponent<CanvasScaler>().uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
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
        Text t = textGO.AddComponent<Text>();
        t.text = promptText;
        t.fontSize = 20;
        t.color = Color.white;
        t.alignment = TextAnchor.MiddleCenter;
        t.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
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

            if (Input.GetKeyDown(interactKey))
            {
               inventory.AddItem(nearestItem);
                playerMovement?.PlayFlash();
               if (nearestItem.destroyOnPickup)
                    Destroy(nearestItem.gameObject);
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
        float minDist = pickupRange;

        // 按距离检测场景中所有存活的 PickupItem
        PickupItem[] all = FindObjectsOfType<PickupItem>();
        foreach (PickupItem p in all)
        {
            if (!p.gameObject.activeInHierarchy) continue;
            float dist = Vector2.Distance(transform.position, p.transform.position);
            if (dist < minDist)
            {
                minDist = dist;
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
