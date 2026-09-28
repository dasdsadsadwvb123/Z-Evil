using UnityEngine;
using UnityEngine.UI;
using System.Collections;

public class CinematicIntro : MonoBehaviour
{
    [Header("引用")]
    public DialogueManager dialogueManager;
    public PixelGridMovement playerMovement;

    [Header("窗口位置（玩家走过去按F的位置）")]
    public Vector2 windowGridPosition = new Vector2(3f, 0f);

    [Header("音效")]
    public AudioSource glassBreakSound;
    public GameObject glassObject;
    public float glassAppearDelay = 1f;
    public AudioSource zombieRoarSound;

    [Header("丧尸生成")]
    public GameObject zombiePrefab;
    public Vector2 zombieSpawnPosition = Vector2.zero;

    [Header("时间设置")]
    public float waitAfterFKey = 2f;
    public float waitAfterRoar = 1.5f;

    [Header("提示文字")]
   public string inspectPrompt = "按 F 查看";
    public Text inspectPromptUI;

   [Header("对话组索引")]
   public int tvDialogueGroup = 0;
    public int glassBreakGroup = 1;

    private bool playerPressedF = false;

    void Start()
    {
        if (dialogueManager == null)
            dialogueManager = GetComponent<DialogueManager>();
        if (playerMovement == null)
            playerMovement = FindObjectOfType<PixelGridMovement>();

        StartCoroutine(PlayIntro());
    }

    private IEnumerator PlayIntro()
    {
        // 默认隐藏提示文字
        if (inspectPromptUI != null)
            inspectPromptUI.gameObject.SetActive(false);

        // === 第1步：冻结玩家，播放电视对话 ===
        if (playerMovement != null)
            playerMovement.frozen = true;

        dialogueManager.PlayGroup(tvDialogueGroup);
        yield return new WaitUntil(() => !dialogueManager.isPlaying);

        // === 第2步：玻璃破碎，播放第二组对话 ===
        // === 第2步：玻璃破碎对话 ===


        dialogueManager.PlayGroup(glassBreakGroup);
        yield return new WaitUntil(() => !dialogueManager.isPlaying);

        // === 第3步：传送玩家到窗口 ===
        if (playerMovement != null)
            playerMovement.TeleportTo(windowGridPosition);
        yield return new WaitForSeconds(0.3f);

        // === 第4步：显示提示，等待按F ===
        if (inspectPromptUI != null)
        {
            inspectPromptUI.text = inspectPrompt;
            inspectPromptUI.gameObject.SetActive(true);
        }
        yield return new WaitUntil(() => WorldInteractionBlocker.GetKeyDown(KeyCode.F));
        if (inspectPromptUI != null)
            inspectPromptUI.gameObject.SetActive(false);


        // 玻璃出现
        yield return new WaitForSeconds(glassAppearDelay);
        if (glassObject != null) glassObject.SetActive(true);
        if (glassBreakSound != null) glassBreakSound.Play();
        // === 第5步：按F后等待几秒 ===
        yield return new WaitForSeconds(waitAfterFKey);

        // === 第6步：丧尸吼叫 ===
        if (zombieRoarSound != null)
            zombieRoarSound.Play();
        yield return new WaitForSeconds(waitAfterRoar);

        // === 第7步：生成丧尸 ===
        if (zombiePrefab != null)
            Instantiate(zombiePrefab, zombieSpawnPosition, Quaternion.identity);

        // === 第8步：解冻玩家，游戏开始 ===
        if (playerMovement != null)
            playerMovement.frozen = false;

        Debug.Log("[开场] 剧情结束，游戏开始！");
    }

    // 供 UI 按钮或别的脚本调用（比如跳过剧情）
    public void SkipIntro()
    {
        StopAllCoroutines();
        if (dialogueManager != null) dialogueManager.Stop();
        if (playerMovement != null) playerMovement.frozen = false;

        if (zombiePrefab != null)
            Instantiate(zombiePrefab, zombieSpawnPosition, Quaternion.identity);
    }
}
