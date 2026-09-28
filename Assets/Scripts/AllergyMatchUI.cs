using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;

/// <summary>
/// 过敏名单——配药板拖线配对界面（静态 Open 懒生成，PasswordPad/除颤仪同款零搭建）：
/// 左列 5 支药剂卡扣、右列 5 张病床名牌（301~305）；鼠标从药剂按下拖到名牌松开生成连线。
/// 一对一（床被占先拆旧线）；重复拖 = 改配；顶部显示配伍禁忌表（排除式线索）+ 306 床灰色暗线。
/// 5 对连满自动判定：全对 → 绿线定格 + 开锁 + 通知板侧掉钥匙卡；有错 → 错线闪红断开（正确线保留，无惩罚）。
/// openedFrame 防同帧自关 + LastClosedFrame 防关闭当帧被重开（柜子双守卫同款）。
/// </summary>
public class AllergyMatchUI : MonoBehaviour
{
    private static AllergyMatchUI instance;

    /// <summary> 谜题界面当前是否开着（配药板防重入） </summary>
    public static bool IsOpen { get; private set; }

    /// <summary> 关闭发生在哪一帧：配药板对关闭当帧的 F 视而不见，杜绝"关了又被同帧重开" </summary>
    public static int LastClosedFrame { get; private set; } = -1;

    private AllergyMatchBoard board;
    private int openedFrame = -1;

    // ---- 配对状态 ----
    private int?[] drugToBed = new int?[5]; // 每支药连到哪张床（下标 = 药剂 0~4；值 = 床位 0~4；null = 未连）
    private int dragDrug = -1;              // 正在拖的是第几支药（-1 = 没在拖）
    private bool judging = false;           // 判定闪烁中（禁拖动/禁关闭，防演出被打断）

    // ---- 连线物体 ----
    private GameObject[] lines = new GameObject[5]; // 每支药最多一条线
    private GameObject dragLine;                    // 拖动中的临时线

    // ---- UI 引用 ----
    private GameObject canvasObj;
    private RectTransform canvasRect;
    private Text statusText;
    private RectTransform[] drugRects = new RectTransform[5];
    private RectTransform[] bedRects = new RectTransform[5];

    private static readonly Color LINE_OK   = new Color(0.35f, 0.9f, 0.45f); // 正确/普通连线
    private static readonly Color LINE_BAD  = new Color(1f, 0.2f, 0.15f);    // 错线闪红
    private static readonly Color LINE_DRAG = new Color(1f, 0.85f, 0.3f);    // 拖动中的线

    /// <summary> 打开配对谜题（AllergyMatchBoard 调用；没有实例就自动生成） </summary>
    public static void Open(AllergyMatchBoard puzzleBoard)
    {
        if (instance == null)
        {
            GameObject go = new GameObject("AllergyMatchUIRoot");
            instance = go.AddComponent<AllergyMatchUI>();
        }
        instance.Show(puzzleBoard);
    }

    private void Show(AllergyMatchBoard puzzleBoard)
    {
        board = puzzleBoard;

        EnsureEventSystem(); // 鼠标拖线需要 EventSystem（场景里没有就自动补）

        IsOpen = true;
        openedFrame = Time.frameCount; // 开界面当帧的 F 不算关闭指令（防同帧自关）
        Time.timeScale = 0f;           // 暂停惯例

        // 重置配对（重开重来；解开一次后板侧永久安静，不会走到这）
        for (int i = 0; i < 5; i++) { drugToBed[i] = null; lines[i] = null; } // 旧连线随旧 Canvas 一起销毁，引用清掉
        dragDrug = -1;
        judging = false;

        CreateUI();
        RedrawLines();
        UpdateStatus();
    }

    /// <summary> 鼠标拖线需要 EventSystem，场景里没有就自动生成一个 </summary>
    private static void EnsureEventSystem()
    {
        if (FindObjectOfType<EventSystem>() != null) return;
        GameObject es = new GameObject("EventSystem");
        es.AddComponent<EventSystem>();
        es.AddComponent<StandaloneInputModule>();
    }

    // ======== 界面构建 ========

    private void CreateUI()
    {
        if (canvasObj != null) Destroy(canvasObj);

        canvasObj = new GameObject("AllergyMatchCanvas", typeof(RectTransform));
        WorldInteractionBlocker.Attach(canvasObj);
        Canvas canvas = canvasObj.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 305; // 密码面板/除颤仪同级（压过柜子 250 / 背包 200）
        CanvasScaler cs = canvasObj.AddComponent<CanvasScaler>(); cs.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize; cs.referenceResolution = new Vector2(1920f, 1080f); cs.matchWidthOrHeight = 1f;
        canvasObj.AddComponent<GraphicRaycaster>();
        canvasRect = canvas.GetComponent<RectTransform>();

        // 全屏暗底
        GameObject bg = new GameObject("DimBG", typeof(RectTransform));
        bg.transform.SetParent(canvasObj.transform, false);
        Image bgImg = bg.AddComponent<Image>();
        bgImg.color = new Color(0f, 0f, 0f, 0.75f);
        RectTransform bgRect = bgImg.GetComponent<RectTransform>();
        bgRect.anchorMin = Vector2.zero;
        bgRect.anchorMax = Vector2.one;
        bgRect.offsetMin = Vector2.zero;
        bgRect.offsetMax = Vector2.zero;

        // 中央面板
        GameObject panel = new GameObject("Panel", typeof(RectTransform));
        panel.transform.SetParent(canvasObj.transform, false);
        Image panelImg = panel.AddComponent<Image>();
        panelImg.color = new Color(0.1f, 0.1f, 0.1f, 0.92f); // 暗色面板惯例
        RectTransform panelRect = panelImg.GetComponent<RectTransform>();
        panelRect.anchorMin = new Vector2(0.5f, 0.5f);
        panelRect.anchorMax = new Vector2(0.5f, 0.5f);
        panelRect.pivot = new Vector2(0.5f, 0.5f);
        panelRect.anchoredPosition = Vector2.zero;
        panelRect.sizeDelta = new Vector2(1000f, 950f);

        // 标题（金黄）
        MakeText(panel, "Title", "=== 配 药 板 ===", 34, Color.yellow, TextAnchor.MiddleCenter,
            new Vector2(0.5f, 1f), new Vector2(0f, -34f), new Vector2(800f, 56f));

        // 配伍禁忌表（白字排除式线索）
        MakeText(panel, "Clues", board.clueText, 24, new Color(0.9f, 0.9f, 0.9f), TextAnchor.UpperLeft,
            new Vector2(0f, 1f), new Vector2(30f, -120f), new Vector2(420f, 240f));

        // 306 床暗线（灰字，纯叙事）
        MakeText(panel, "Secret", board.secretText, 17, new Color(0.55f, 0.55f, 0.55f), TextAnchor.UpperLeft,
            new Vector2(0f, 1f), new Vector2(30f, -370f), new Vector2(420f, 44f));

        // 左列：5 支药剂卡扣
        for (int i = 0; i < 5; i++)
        {
            GameObject card = new GameObject("Drug_" + i, typeof(RectTransform));
            card.transform.SetParent(panel.transform, false);
            Image cardImg = card.AddComponent<Image>();
            cardImg.color = new Color(0.16f, 0.16f, 0.16f, 0.95f);
            RectTransform cardRect = cardImg.GetComponent<RectTransform>();
            cardRect.anchorMin = cardRect.anchorMax = new Vector2(0.5f, 0.5f);
            cardRect.pivot = new Vector2(0.5f, 0.5f);
            cardRect.anchoredPosition = new Vector2(-280f, -20f - 100f * i);
            cardRect.sizeDelta = new Vector2(340f, 68f);
            drugRects[i] = cardRect;

            // 瓶身色块（属性暗示）
            GameObject swatch = new GameObject("Swatch", typeof(RectTransform));
            swatch.transform.SetParent(card.transform, false);
            Image swImg = swatch.AddComponent<Image>();
            swImg.color = board.drugs[i].bottleColor;
            RectTransform swRect = swImg.GetComponent<RectTransform>();
            swRect.anchorMin = swRect.anchorMax = new Vector2(0f, 0.5f);
            swRect.pivot = new Vector2(0.5f, 0.5f);
            swRect.anchoredPosition = new Vector2(44f, 0f);
            swRect.sizeDelta = new Vector2(46f, 46f);

            // 药剂名标签
            MakeText(card, "Label", board.drugs[i].drugName, 20, Color.white, TextAnchor.MiddleCenter,
                new Vector2(0.5f, 0.5f), new Vector2(34f, 0f), new Vector2(250f, 54f));

            card.AddComponent<DrugTag>().Setup(this, i); // 拖线起点标记
        }

        // 右列：5 张病床名牌
        for (int i = 0; i < 5; i++)
        {
            GameObject card = new GameObject("Bed_" + i, typeof(RectTransform));
            card.transform.SetParent(panel.transform, false);
            Image cardImg = card.AddComponent<Image>();
            cardImg.color = new Color(0.22f, 0.2f, 0.16f, 0.95f);
            RectTransform cardRect = cardImg.GetComponent<RectTransform>();
            cardRect.anchorMin = cardRect.anchorMax = new Vector2(0.5f, 0.5f);
            cardRect.pivot = new Vector2(0.5f, 0.5f);
            cardRect.anchoredPosition = new Vector2(280f, -20f - 100f * i);
            cardRect.sizeDelta = new Vector2(240f, 68f);
            bedRects[i] = cardRect;

            MakeText(card, "Label", (301 + i) + " 床", 23, new Color(1f, 0.9f, 0.6f), TextAnchor.MiddleCenter,
                new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(220f, 54f));

            card.AddComponent<BedTag>().Setup(this, i); // 拖线落点标记
        }

        // 底部状态行（红字：进度/判定反馈）
        statusText = MakeText(panel, "Status", "", 19, new Color(1f, 0.45f, 0.4f), TextAnchor.UpperLeft,
            new Vector2(1f, 1f), new Vector2(-30f, -60f), new Vector2(420f, 30f));

        // 操作提示（灰字）
        MakeText(panel, "Hint", "按住药剂拖到病床名牌上 | Esc / F 关闭", 17, Color.gray, TextAnchor.UpperLeft,
            new Vector2(1f, 1f), new Vector2(-30f, -100f), new Vector2(420f, 26f));
    }

    /// <summary> 造一个 Text（暗面板惯例：LegacyRuntime.ttf），返回组件方便改内容 </summary>
    private Text MakeText(GameObject parent, string name, string content, int size, Color color, TextAnchor align, Vector2 anchor, Vector2 pos, Vector2 sizeD)
    {
        GameObject go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent.transform, false);
        Text t = go.AddComponent<Text>();
        t.text = content;
        t.fontSize = size;
        t.color = color;
        t.alignment = align;
        t.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        t.raycastTarget = false; // 文字不挡鼠标（拖线射线只打卡片）
        RectTransform rt = t.GetComponent<RectTransform>();
        rt.anchorMin = rt.anchorMax = anchor;
        rt.pivot = anchor; // pivot 跟随 anchor：顶部锚定的文字从锚点向下排
        rt.anchoredPosition = pos;
        rt.sizeDelta = sizeD;
        return t;
    }

    // ======== 拖线（手动轮询：比 EventSystem 拖拽接口更稳，卡片是运行时生成的） ========

    private void Update()
    {
        if (!IsOpen) return;

        // Esc/F 关闭：开界面当帧的 F 不算（防同帧自关）；判定闪烁中不许关（演出保护）
        bool closePressed = Input.GetKeyDown(KeyCode.Escape)
            || (Input.GetKeyDown(KeyCode.F) && Time.frameCount != openedFrame);
        if (closePressed && !judging)
        {
            Close();
            return;
        }

        if (judging || board == null) return;

        // 按下：射线找药剂卡 → 开始拖
        if (Input.GetMouseButtonDown(0) && dragDrug < 0)
        {
            DrugTag drug = RaycastMouse<DrugTag>();
            if (drug != null)
            {
                dragDrug = drug.index;
                EnsureDragLine();
            }
        }

        // 拖动中：线跟着鼠标走
        if (dragDrug >= 0 && Input.GetMouseButton(0))
        {
            SetLine(dragLine, drugRects[dragDrug].position, Input.mousePosition, LINE_DRAG);
        }

        // 松开：射线找病床名牌 → 生成/改配连线
        if (dragDrug >= 0 && Input.GetMouseButtonUp(0))
        {
            BedTag bed = RaycastMouse<BedTag>();
            if (bed != null) TryConnect(dragDrug, bed.index);
            dragDrug = -1;
            if (dragLine != null) Destroy(dragLine);
            dragLine = null;
        }
    }

    /// <summary> 鼠标位置的 UI 射线（EventSystem.RaycastAll），命中 T 标记就返回 </summary>
    private T RaycastMouse<T>() where T : Component
    {
        if (EventSystem.current == null) return null;
        PointerEventData ped = new PointerEventData(EventSystem.current) { position = Input.mousePosition };
        List<RaycastResult> results = new List<RaycastResult>();
        EventSystem.current.RaycastAll(ped, results);
        foreach (RaycastResult r in results)
        {
            T tag = r.gameObject.GetComponentInParent<T>();
            if (tag != null) return tag;
        }
        return null;
    }

    /// <summary> 连线：drug → bed（一对一：床被别的药连着先拆那条；重复拖 = 改配） </summary>
    private void TryConnect(int drug, int bed)
    {
        for (int i = 0; i < 5; i++)
            if (i != drug && drugToBed[i] == bed) drugToBed[i] = null; // 这床被占 → 拆旧的

        drugToBed[drug] = bed;
        RedrawLines();
        UpdateStatus();

        // 5 对连满 → 自动判定
        bool allLinked = true;
        for (int i = 0; i < 5; i++) if (drugToBed[i] == null) { allLinked = false; break; }
        if (allLinked) StartCoroutine(JudgeRoutine());
    }

    /// <summary> 判定：全对 → 开锁结算；有错 → 错线闪红断开（正确线保留，无惩罚） </summary>
    private IEnumerator JudgeRoutine()
    {
        judging = true;

        yield return new WaitForSecondsRealtime(0.35f); // 让玩家看清最后一条线

        List<int> wrong = new List<int>();
        for (int i = 0; i < 5; i++)
            if (drugToBed[i].Value != board.correctBedIndex[i]) wrong.Add(i);

        if (wrong.Count == 0)
        {
            // ---- 全对：绿线定格 → 结算 ----
            SetStatus("配伍核对通过——配药柜解锁了");
            for (int i = 0; i < 5; i++) SetLineColor(lines[i], LINE_OK);
            yield return new WaitForSecondsRealtime(0.8f);
            if (board != null) board.SolveSuccess(); // 先结算（开锁声+钥匙卡）再关界面
            Close();
            yield break;
        }

        // ---- 有错：错线闪红断开，正确线保留 ----
        SetStatus("配伍禁忌！红线的组合是错的……");
        foreach (int w in wrong) SetLineColor(lines[w], LINE_BAD);
        yield return new WaitForSecondsRealtime(0.7f);

        foreach (int w in wrong) drugToBed[w] = null; // 错线断开
        RedrawLines();
        judging = false;
        UpdateStatus();
    }

    // ======== 连线绘制（UI Image 细长矩形：屏幕坐标 → Canvas 本地坐标） ========

    private void RedrawLines()
    {
        for (int i = 0; i < 5; i++)
        {
            if (drugToBed[i] != null)
            {
                EnsureLine(ref lines[i]);
                SetLine(lines[i], drugRects[i].position, bedRects[drugToBed[i].Value].position, LINE_OK);
            }
            else if (lines[i] != null)
            {
                Destroy(lines[i]);
                lines[i] = null;
            }
        }
    }

    private void EnsureLine(ref GameObject line)
    {
        if (line != null) return;
        line = new GameObject("MatchLine", typeof(RectTransform));
        line.transform.SetParent(canvasObj.transform, false);
        Image img = line.AddComponent<Image>();
        img.raycastTarget = false; // 线不挡射线（不然连上的床就没法再拖了）
        RectTransform rt = img.rectTransform;
        rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);
        rt.localRotation = Quaternion.identity;
    }

    private void EnsureDragLine()
    {
        if (dragLine != null) return;
        EnsureLine(ref dragLine);
    }

    /// <summary> 把一条线摆到 from→to（屏幕坐标输入，内部转 Canvas 本地坐标） </summary>
    private void SetLine(GameObject line, Vector2 screenFrom, Vector2 screenTo, Color color)
    {
        if (line == null) return;
        RectTransformUtility.ScreenPointToLocalPointInRectangle(canvasRect, screenFrom, null, out Vector2 a);
        RectTransformUtility.ScreenPointToLocalPointInRectangle(canvasRect, screenTo, null, out Vector2 b);
        RectTransform rt = line.GetComponent<RectTransform>();
        rt.anchoredPosition = (a + b) * 0.5f;
        rt.sizeDelta = new Vector2(Vector2.Distance(a, b), 4f);
        rt.localRotation = Quaternion.Euler(0f, 0f, Mathf.Atan2(b.y - a.y, b.x - a.x) * Mathf.Rad2Deg);
        line.GetComponent<Image>().color = color;
    }

    private void SetLineColor(GameObject line, Color color)
    {
        if (line != null) line.GetComponent<Image>().color = color;
    }

    private void UpdateStatus()
    {
        int count = 0;
        for (int i = 0; i < 5; i++) if (drugToBed[i] != null) count++;
        SetStatus("按禁忌表把每支药拖到对应病床（" + count + " / 5）——连满自动核对");
    }

    private void SetStatus(string text)
    {
        if (statusText != null) statusText.text = text;
    }

    // ======== 关闭 ========

    private void Close()
    {
        IsOpen = false;
        LastClosedFrame = Time.frameCount;
        Time.timeScale = 1f;
        StopAllCoroutines(); // 判定闪烁协程全停
        judging = false;
        dragDrug = -1;
        if (dragLine != null) Destroy(dragLine);
        dragLine = null;
        if (canvasObj != null) Destroy(canvasObj); // 连线都是 Canvas 子物体，一起销毁
    }

    private void OnDestroy()
    {
        if (IsOpen) { IsOpen = false; Time.timeScale = 1f; }
        if (canvasObj != null) Destroy(canvasObj);
    }
}

// ============================================================
// 拖线标记（挂在药剂卡/病床卡上，告诉 UI"我是谁"）
// ============================================================
public class DrugTag : MonoBehaviour
{
    public AllergyMatchUI ui;
    public int index;
    public void Setup(AllergyMatchUI owner, int i) { ui = owner; index = i; }
}

public class BedTag : MonoBehaviour
{
    public AllergyMatchUI ui;
    public int index;
    public void Setup(AllergyMatchUI owner, int i) { ui = owner; index = i; }
}
