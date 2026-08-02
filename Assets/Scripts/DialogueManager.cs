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

    private int currentGroupIndex = -1;
    private Coroutine playRoutine;

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
        if (dialoguePanel != null) dialoguePanel.SetActive(false);
    }

    private IEnumerator PlayGroupRoutine(int groupIndex)
    {
        isPlaying = true;
        DialogueGroup group = dialogueGroups[groupIndex];

        if (dialoguePanel != null) dialoguePanel.SetActive(true);

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

            yield return new WaitForSeconds(line.displayDuration);
        }

        isPlaying = false;
        if (dialoguePanel != null) dialoguePanel.SetActive(false);

        OnGroupFinished?.Invoke(groupIndex);
    }
}
