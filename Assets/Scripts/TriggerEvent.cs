using UnityEngine;
using UnityEngine.Events;
using System.Collections;

public class TriggerEvent : MonoBehaviour
{
    [Header("触发设置")]
    [Tooltip("-1 = 无限, 0 = 已用完, 正数 = 可用次数")]
    public int triggerCount = -1;
    public float delaySeconds = 0f;
    public bool freezePlayer = false;
    public bool unfreezeOnComplete = false;

    [Header("对话（可选）")]
    [Tooltip("-1 = 不播放对话")]
    public int dialogueGroupIndex = -1;

    [Header("执行事件")]
    public UnityEvent onTriggered;

    private int usedTimes = 0;
    private PixelGridMovement playerMovement;
    private DialogueManager dialogueManager;

    void Start()
    {
        playerMovement = FindObjectOfType<PixelGridMovement>();
        dialogueManager = FindObjectOfType<DialogueManager>();
    }

    void OnTriggerEnter2D(Collider2D other)
    {
        if (!other.CompareTag("Player")) return;

        if (triggerCount >= 0 && usedTimes >= triggerCount) return;
        usedTimes++;

        StartCoroutine(Execute());
    }

    private IEnumerator Execute()
    {
        if (delaySeconds > 0f)
            yield return new WaitForSeconds(delaySeconds);

        if (freezePlayer && playerMovement != null)
            playerMovement.frozen = true;

        // 先播对话（如果有）
        if (dialogueGroupIndex >= 0 && dialogueManager != null)
        {
            dialogueManager.PlayGroup(dialogueGroupIndex);
            yield return new WaitUntil(() => !dialogueManager.isPlaying);
        }

        // 执行事件
        onTriggered?.Invoke();

        if (unfreezeOnComplete && playerMovement != null)
            playerMovement.frozen = false;
    }
}
