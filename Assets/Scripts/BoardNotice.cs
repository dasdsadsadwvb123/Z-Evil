using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 公告牌（看字交互，挂贴墙的公告牌物体上，零依赖不碰任何现有脚本）：
/// 靠近 → 牌上方显示牌名提示（按 F 查看）；按 F → 公告全文以 World Space 文字显示在牌子下方，
/// 再按 F 收起；走出范围自动收起。文字/牌名/偏移/缩放/音效全部 Inspector 配置，3 块牌各填各的文案。
/// 提示 UI 走 Screen Space 轻量套路（配药板同款）；公告走 World Space Canvas（缩放 0.01 量级匹配场景比例）。
/// 公告文字支持富文本：关键数字用 <color=red>31</color> 标红。
/// 音效走 AudibleAudio.PlayAt 距离听声惯例（不拖 paperClip = 静音翻看）。
/// </summary>
[AddComponentMenu("Board Notice（公告牌）")]
public class BoardNotice : MonoBehaviour
{
    [Header("交互")]
    [Tooltip("玩家离牌子多近能按 F 查看")]
    public float interactRange = 1.5f;

    [Header("公告内容（3 块牌各填各的）")]
    [Tooltip("进圈提示显示的牌名（如：值班表 / 走廊通知 / 停尸房记录）")]
    public string boardTitle = "公告";
    [Tooltip("公告全文（在 Inspector 里直接打字换行；关键数字用 <color=red>31</color> 标红）")]
    [TextArea(4, 10)]
    public string noticeText = "（在 Inspector 里填公告全文）";

    [Header("公告显示位置/外观")]
    [Tooltip("公告文字相对牌子的偏移（默认 0,0 = 直接铺在牌子上，和公告牌同等位置同等大小）")]
    public Vector3 noticeOffset = Vector3.zero;
    [Tooltip("公告文字整体缩放（世界单位；默认 0.01 适配像素场景，字太大改小、太小改大）")]
    public float noticeScale = 0.01f;
    [Tooltip("公告文字宽度（世界画布单位；字太多装不下就加大）")]
    public float noticeWidth = 420f;

    [Header("音效（不拖 = 静音，走距离听声惯例）")]
    [Tooltip("翻看纸张的音效（按 F 展开时播）")]
    public AudioClip paperClip;

    private Transform player;
    private GameObject promptUI;      // 牌名提示（屏幕空间）
    private Text promptTextUI;
    private GameObject noticeCanvas;  // 公告全文（世界空间，牌子下方）
    private bool noticeVisible = false;

    private void Start()
    {
        GameObject p = GameObject.FindGameObjectWithTag("Player");
        if (p != null) player = p.transform;

        CreatePromptUI();
        CreateNoticeCanvas();
    }

    private void Update()
    {
        if (player == null) return;

        bool near = Vector2.Distance(transform.position, player.position) <= interactRange;
        if (promptUI != null) promptUI.SetActive(near);

        // 走出范围自动收起（提示跟着一起消失）
        if (!near)
        {
            if (noticeVisible) HideNotice();
            return;
        }

        // 按 F 切换：没展开 → 展开公告；已展开 → 收起
        if (Input.GetKeyDown(KeyCode.F))
        {
            if (noticeVisible) HideNotice();
            else ShowNotice();
        }
    }

    /// <summary> 展开：显示牌子下方的公告全文 + 翻纸音效 </summary>
    private void ShowNotice()
    {
        noticeVisible = true;
        if (noticeCanvas != null) noticeCanvas.SetActive(true);
        if (paperClip != null) AudibleAudio.PlayAt(paperClip, transform.position);
    }

    /// <summary> 收起 </summary>
    private void HideNotice()
    {
        noticeVisible = false;
        if (noticeCanvas != null) noticeCanvas.SetActive(false);
    }

    // ======== 公告全文（World Space：挂在牌子下方，随场景移动） ========
    private void CreateNoticeCanvas()
    {
        noticeCanvas = new GameObject("BoardNoticeText");
        noticeCanvas.transform.SetParent(transform, false);
        noticeCanvas.transform.localPosition = noticeOffset; // 默认 0,0 = 和公告牌同等位置
        noticeCanvas.transform.localRotation = Quaternion.identity;

        Canvas canvas = noticeCanvas.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.WorldSpace;
        canvas.sortingOrder = 90; // 压在普通贴图之上

        // 尺寸自动适配公告牌 Sprite 的实际大小（Scale 换算回画布单位）
        SpriteRenderer boardSr = GetComponent<SpriteRenderer>();
        Vector2 boardSize = boardSr != null ? (Vector2)boardSr.bounds.size : new Vector2(2f, 2f);
        RectTransform rt = canvas.GetComponent<RectTransform>();
        rt.sizeDelta = new Vector2(boardSize.x / noticeScale, boardSize.y / noticeScale);
        rt.localScale = Vector3.one * noticeScale;

        // 纸片底（暗色半透明，铺满整块牌子，白字读得清）
        GameObject paper = new GameObject("Paper");
        paper.transform.SetParent(noticeCanvas.transform, false);
        Image paperImg = paper.AddComponent<Image>();
        paperImg.color = new Color(0.05f, 0.05f, 0.05f, 0.88f);
        RectTransform paperRect = paperImg.GetComponent<RectTransform>();
        paperRect.anchorMin = Vector2.zero;
        paperRect.anchorMax = Vector2.one;
        paperRect.offsetMin = Vector2.zero;
        paperRect.offsetMax = Vector2.zero;

        // 公告全文（白字，BestFit 自动缩放字号铺满牌子）
        GameObject textGO = new GameObject("NoticeBody");
        textGO.transform.SetParent(paper.transform, false);
        Text body = textGO.AddComponent<Text>();
        body.text = noticeText; // richText 默认开启：<color=red>31</color> 标红直接生效
        body.fontSize = 22;
        body.resizeTextForBestFit = true;  // 字号自动缩放：文字多也不溢出牌子
        body.resizeTextMinSize = 10;
        body.resizeTextMaxSize = 34;
        body.color = Color.white;
        body.alignment = TextAnchor.UpperCenter;
        body.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        RectTransform bodyRect = body.GetComponent<RectTransform>();
        bodyRect.anchorMin = Vector2.zero;
        bodyRect.anchorMax = Vector2.one;
        bodyRect.offsetMin = new Vector2(14f, 10f);
        bodyRect.offsetMax = new Vector2(-14f, -10f);

        noticeCanvas.SetActive(false); // 出生先藏好，按 F 才展开
    }

    // ======== 牌名提示（屏幕空间，配药板同款轻量套路） ========
    private void CreatePromptUI()
    {
        promptUI = new GameObject("BoardNoticePrompt");
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
        bgRect.sizeDelta = new Vector2(320, 50);

        GameObject textGO = new GameObject("Text");
        textGO.transform.SetParent(bg.transform, false);
        promptTextUI = textGO.AddComponent<Text>();
        promptTextUI.text = boardTitle;
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
