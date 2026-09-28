using UnityEngine;
using UnityEngine.UI;
using System.Collections;

[System.Serializable]
public class DialogueLine
{
    [Header("说话人")]
    public string speakerName = "???";
    public Sprite speakerPortrait;

    [Header("对话内容")]
    [TextArea(2, 5)]
    public string text = "";

    [Header("显示时长（秒）")]
    public float displayDuration = 3f;
}

[System.Serializable]
public class DialogueGroup
{
    public DialogueLine[] lines;
}

public class DialogueManager : MonoBehaviour
{
    [Header("UI 引用（拖入你的 UI 组件）")]
    public GameObject dialoguePanel;
    public Image portraitImage;
    public Text speakerNameText;
    public Text dialogueText;
    public Image progressBar;

    [Header("对话数据")]
    public DialogueGroup[] dialogueGroups;

    [HideInInspector] public bool isPlaying = false;

    [Header("跳过设置")]
    [Tooltip("按此键跳过当前句的剩余等待")]
    public KeyCode skipKey = KeyCode.Space;

    private int currentGroupIndex = -1;
    private Coroutine playRoutine;
    private Text skipHint;        // 右下角"空格跳过"呼吸提示（纯代码生成，无需拖引用）
    private float hintTimer = 0f;

    void Start()
    {
        WorldInteractionBlocker.Attach(dialoguePanel);
        CreateSkipHint();
    }

    void Update()
    {
        if (skipHint == null) return;

        // 可见性完全由 isPlaying 决定，每帧校验，杜绝任何漏显示的时机问题
        if (skipHint.gameObject.activeSelf != isPlaying)
        {
            skipHint.gameObject.SetActive(isPlaying);
            if (isPlaying) Debug.Log("[对话] SkipHint 显示", skipHint);
        }

        // 对话播放时，右下角提示做呼吸闪烁
        if (isPlaying)
        {
            hintTimer += Time.unscaledDeltaTime;
            Color c = Color.white;
            c.a = 0.7f + 0.3f * Mathf.Sin(hintTimer * 3f); // 透明度 0.4 ~ 1.0 之间缓慢呼吸
            skipHint.color = c;
        }
    }

    /// <summary> 播完一组对话后触发，参数是组索引 </summary>
    public System.Action<int> OnGroupFinished;

    public void PlayGroup(int groupIndex)
    {
        if (groupIndex < 0 || groupIndex >= dialogueGroups.Length) return;
        if (playRoutine != null) StopCoroutine(playRoutine);
        currentGroupIndex = groupIndex;
        playRoutine = StartCoroutine(PlayGroupRoutine(groupIndex));
    }

    public void Stop()
    {
        if (playRoutine != null)
        {
            StopCoroutine(playRoutine);
            playRoutine = null;
        }
        isPlaying = false;
        if (skipHint != null) skipHint.gameObject.SetActive(false);
        if (dialoguePanel != null) dialoguePanel.SetActive(false);
    }

    /// <summary> 纯代码生成右下角"空格跳过"提示，挂在对话板下面跟随显示/隐藏 </summary>
    private void CreateSkipHint()
    {
        if (dialoguePanel == null) return; // 没有对话板就没地方挂提示
        if (skipHint != null) return;

        GameObject hintObj = new GameObject("SkipHint", typeof(RectTransform)); // ⚠️ UI 物体必须带 RectTransform
        // 挂到 Canvas 根节点（而不是对话板），直接钉在屏幕右下角，不受对话板矩形/裁剪影响
        Canvas targetCanvas = dialoguePanel.GetComponentInParent<Canvas>();
        Transform parent = targetCanvas != null ? targetCanvas.transform : dialoguePanel.transform;
        hintObj.transform.SetParent(parent, false);
        Debug.Log($"[对话] SkipHint 已创建，父物体 = {parent.name}", hintObj);
        skipHint = hintObj.AddComponent<Text>();
        skipHint.text = "空格跳过";
        skipHint.fontSize = 48;
        skipHint.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        skipHint.color = Color.white;
        skipHint.alignment = TextAnchor.LowerRight;
        skipHint.raycastTarget = false;

        // 黑色描边 + 投影：保证白字在任何背景上都看得见
        Outline outline = hintObj.AddComponent<Outline>();
        outline.effectColor = Color.black;
        outline.effectDistance = new Vector2(1.5f, -1.5f);
        Shadow shadow = hintObj.AddComponent<Shadow>();
        shadow.effectColor = new Color(0f, 0f, 0f, 0.8f);
        shadow.effectDistance = new Vector2(2f, -2f);

        RectTransform rt = hintObj.GetComponent<RectTransform>();
        rt.anchorMin = new Vector2(1f, 0f);   // 锚定屏幕右下角
        rt.anchorMax = new Vector2(1f, 0f);
        rt.pivot = new Vector2(1f, 0f);
        rt.anchoredPosition = new Vector2(-36f, 42f);
        rt.sizeDelta = new Vector2(440f, 60f);

        hintObj.SetActive(false); // 初始隐藏，播放对话时才显示
    }

    private IEnumerator PlayGroupRoutine(int groupIndex)
    {
        WorldInteractionBlocker.Attach(dialoguePanel);
        isPlaying = true;
        DialogueGroup group = dialogueGroups[groupIndex];

        if (dialoguePanel != null) dialoguePanel.SetActive(true);
        if (skipHint != null) skipHint.gameObject.SetActive(true);

        for (int i = 0; i < group.lines.Length; i++)
        {
            DialogueLine line = group.lines[i];

            // 更新 UI
            if (portraitImage != null && line.speakerPortrait != null)
                portraitImage.sprite = line.speakerPortrait;
            if (speakerNameText != null)
                speakerNameText.text = line.speakerName;
            if (dialogueText != null)
                dialogueText.text = line.text;

            // 更新进度条
            if (progressBar != null && group.lines.Length > 1)
                progressBar.fillAmount = (float)(i + 1) / group.lines.Length;

            // 等待显示时长，期间按空格可立即跳过本句
            float elapsed = 0f;
            while (elapsed < line.displayDuration)
            {
                if (Input.GetKeyDown(skipKey)) break;
                elapsed += Time.deltaTime;
                yield return null;
            }
            yield return null; // 消耗本帧，防止同一次按键连跳多句
        }

        isPlaying = false;
        if (skipHint != null) skipHint.gameObject.SetActive(false);
        if (dialoguePanel != null) dialoguePanel.SetActive(false);

        OnGroupFinished?.Invoke(groupIndex);
    }
}
