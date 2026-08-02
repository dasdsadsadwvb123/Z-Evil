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

    private void Start()
    {
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
