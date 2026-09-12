using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 存档点：场景里摆一个物体挂上它（再放个 Sprite 当外观，比如打字机/路牌）。
/// 玩家靠近时显示提示，按 F 存档、按 R 读档。
/// 用法：空物体 → Add Component → Save Point → 调整交互范围。
/// </summary>
public class SavePoint : MonoBehaviour
{
    [Header("存档点 ID（读档时靠它把玩家放回这个存档点；每个存档点唯一）")]
    [Tooltip("唯一 ID，如 Save_大厅 / Save_档案室。留空会自动用「场景名_物体名」兜底")]
    public string savePointId = "";

    [Header("交互设置")]
    [Tooltip("玩家靠近到这个距离内显示提示")]
    public float interactRange = 1.5f;

    [Header("按键")]
    public KeyCode saveKey = KeyCode.F;
    public KeyCode loadKey = KeyCode.R;

    [Header("提示文字")]
    public string savePrompt = "按 F 存档";
    public string loadPrompt = "按 R 读档";

    private SaveSystem saveSystem;
    private PixelGridMovement cachedPlayer;
    private GameObject promptUI;
    private Text promptText;

    private void Start()
    {
        // 存档系统挂在玩家身上，用查找的方式拿引用
        saveSystem = FindObjectOfType<SaveSystem>();
        if (saveSystem == null)
            Debug.LogWarning("[存档点] 场景里没有 SaveSystem，请把它挂到玩家身上！", gameObject);

        // savePointId 留空 → 自动用「场景名_物体名」兜底（让新手不用手填也能跑）
        if (string.IsNullOrEmpty(savePointId))
        {
            savePointId = gameObject.scene.name + "_" + gameObject.name;
            Debug.LogWarning("[存档点] '" + name + "' 的 savePointId 没填，自动兜底为 '" + savePointId
                + "'。建议在 Inspector 里手填一个唯一 ID（如 Save_大厅），以后重命名物体也不会串档。", gameObject);
        }

        CreatePromptUI();
    }

    /// <summary> 创建"按F存档"提示 UI（和 PlayerPickup 的拾取提示同风格） </summary>
    private void CreatePromptUI()
    {
        promptUI = new GameObject("SavePointPrompt");
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
        bgRect.sizeDelta = new Vector2(340, 55);

        GameObject textGO = new GameObject("Text");
        textGO.transform.SetParent(bg.transform, false);
        promptText = textGO.AddComponent<Text>();
        promptText.fontSize = 20;
        promptText.color = Color.yellow;
        promptText.alignment = TextAnchor.MiddleCenter;
        promptText.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        RectTransform textRect = textGO.GetComponent<RectTransform>();
        textRect.anchorMin = Vector2.zero;
        textRect.anchorMax = Vector2.one;
        textRect.offsetMin = Vector2.zero;
        textRect.offsetMax = Vector2.zero;

        promptUI.SetActive(false);
    }

    private void Update()
    {
        if (saveSystem == null) return;

        // 缓存玩家引用（找不到就偶尔再找一次）
        if (cachedPlayer == null)
            cachedPlayer = FindObjectOfType<PixelGridMovement>();
        if (cachedPlayer == null) return;

        bool playerNear = Vector2.Distance(transform.position, cachedPlayer.transform.position) <= interactRange;

        if (playerNear)
        {
            // 靠近存档点：显示提示
            promptUI.SetActive(true);
            promptText.text = savePrompt + "    " + loadPrompt;

            // F 存档
            if (Input.GetKeyDown(saveKey))
            {
                saveSystem.SaveGame(this); // 传入自己：读档时玩家会回到这个存档点的坐标
                ShowSavedFeedback();
            }

            // R 读档
            if (Input.GetKeyDown(loadKey))
                saveSystem.LoadGame();
        }
        else
        {
            promptUI.SetActive(false);
        }
    }

    /// <summary> 存档成功的短暂反馈（右上角文字，2 秒消失） </summary>
    private void ShowSavedFeedback()
    {
        GameObject feedback = new GameObject("SaveFeedback");
        Canvas canvas = feedback.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 300;
        UIScale.Setup(feedback);
        feedback.AddComponent<GraphicRaycaster>();

        Text t = feedback.AddComponent<Text>();
        t.text = "✓ 已存档";
        t.fontSize = 28;
        t.color = Color.green;
        t.alignment = TextAnchor.MiddleCenter;
        t.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        RectTransform rect = t.GetComponent<RectTransform>();
        rect.anchorMin = new Vector2(0.5f, 0.85f);
        rect.anchorMax = new Vector2(0.5f, 0.85f);
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.anchoredPosition = Vector2.zero;
        rect.sizeDelta = new Vector2(400, 60);

        // 2 秒后自动消失
        Destroy(feedback, 2f);
    }

    private void OnDestroy()
    {
        if (promptUI != null) Destroy(promptUI);
    }

    /// <summary> 编辑器辅助：画出交互范围 </summary>
    private void OnDrawGizmosSelected()
    {
        Gizmos.color = new Color(0.3f, 1f, 0.3f, 0.35f);
        Gizmos.DrawWireSphere(transform.position, interactRange);
    }
}
