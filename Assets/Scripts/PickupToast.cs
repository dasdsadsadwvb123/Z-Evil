using UnityEngine;
using UnityEngine.UI;
using System.Collections;

/// <summary>
/// 获得物品提示：捡到物品/弹药/合成成功时，屏幕下方中央弹出提示条。
/// 挂到玩家（Player）物体上（和 Inventory 同物体）。
///
/// 原理：订阅 Inventory.OnItemNotice 事件（数据层广播），收到就显示提示。
/// 淡入 → 停留 → 淡出，纯代码 UI，不需要额外素材。
/// </summary>
public class PickupToast : MonoBehaviour
{
    [Header("提示设置")]
    [Tooltip("提示停留时间（秒）")]
    public float displayDuration = 2f;

    [Tooltip("淡入时间（秒）")]
    public float fadeInTime = 0.15f;

    [Tooltip("淡出时间（秒）")]
    public float fadeOutTime = 0.4f;

    [Tooltip("文字颜色（默认白字黑底）")]
    public Color textColor = Color.white;

    [Tooltip("文字大小")]
    public int fontSize = 26;

    private Inventory inventory;
    private GameObject toastGO;
    private Image bgImage;
    private Text toastText;
    private Coroutine toastRoutine;

    private void Start()
    {
        inventory = GetComponent<Inventory>();
        if (inventory != null)
            inventory.OnItemNotice += ShowToast; // 订阅广播
        else
            Debug.LogWarning("[提示] 玩家身上没有 Inventory，获得物品提示不会工作！", gameObject);

        CreateToastUI();
    }

    private void OnDestroy()
    {
        // 退订事件，防止内存泄漏
        if (inventory != null)
            inventory.OnItemNotice -= ShowToast;
    }

    /// <summary> 收到广播：显示提示（如果上一条还没播完，打断换成最新） </summary>
    private void ShowToast(string message)
    {
        if (toastRoutine != null)
            StopCoroutine(toastRoutine);
        toastRoutine = StartCoroutine(ToastRoutine(message));
    }

    /// <summary> 提示流程：淡入 → 停留 → 淡出 </summary>
    private IEnumerator ToastRoutine(string message)
    {
        if (toastGO == null) yield break;

        toastText.text = message;
        toastGO.SetActive(true);

        // 1. 淡入
        float t = 0f;
        while (t < fadeInTime)
        {
            t += Time.unscaledDeltaTime;
            SetAlpha(Mathf.Clamp01(t / fadeInTime));
            yield return null;
        }
        SetAlpha(1f);

        // 2. 停留（用 Realtime，暂停时也照常计时）
        yield return new WaitForSecondsRealtime(displayDuration);

        // 3. 淡出
        t = 0f;
        while (t < fadeOutTime)
        {
            t += Time.unscaledDeltaTime;
            SetAlpha(Mathf.Clamp01(1f - t / fadeOutTime));
            yield return null;
        }
        SetAlpha(0f);
        toastGO.SetActive(false);
    }

    /// <summary> 统一控制提示条的透明度（背景和文字一起淡入淡出） </summary>
    private void SetAlpha(float alpha)
    {
        if (bgImage != null)
        {
            Color c = bgImage.color;
            c.a = alpha * 0.7f; // 背景本身是半透黑
            bgImage.color = c;
        }
        if (toastText != null)
        {
            Color c = toastText.color;
            c.a = alpha;
            toastText.color = c;
        }
    }

    // ======== UI 创建 ========

    private void CreateToastUI()
    {
        toastGO = new GameObject("ItemToast");
        Canvas canvas = toastGO.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 260; // 在背包(200)之上，死亡界面(400)之下
        UIScale.Setup(toastGO);
        toastGO.AddComponent<GraphicRaycaster>();

        // 黑色半透明底条（屏幕下方中央）
        GameObject bg = new GameObject("BG");
        bg.transform.SetParent(toastGO.transform, false);
        bgImage = bg.AddComponent<Image>();
        bgImage.color = new Color(0f, 0f, 0f, 0.7f);
        RectTransform bgRect = bgImage.GetComponent<RectTransform>();
        bgRect.anchorMin = new Vector2(0.5f, 0f); // 下方
        bgRect.anchorMax = new Vector2(0.5f, 0f);
        bgRect.pivot = new Vector2(0.5f, 0.5f);
        bgRect.anchoredPosition = new Vector2(0f, 90f);
        bgRect.sizeDelta = new Vector2(520f, 64f);

        // 白色文字
        GameObject txt = new GameObject("Text");
        txt.transform.SetParent(bg.transform, false);
        toastText = txt.AddComponent<Text>();
        toastText.fontSize = fontSize;
        toastText.color = textColor;
        toastText.alignment = TextAnchor.MiddleCenter;
        toastText.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        RectTransform txtRect = toastText.GetComponent<RectTransform>();
        txtRect.anchorMin = Vector2.zero;
        txtRect.anchorMax = Vector2.one;
        txtRect.offsetMin = Vector2.zero;
        txtRect.offsetMax = Vector2.zero;

        toastGO.SetActive(false); // 初始隐藏
    }
}
