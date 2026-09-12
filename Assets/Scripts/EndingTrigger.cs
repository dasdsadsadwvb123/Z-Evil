using System.Collections;
using UnityEngine;

/// <summary>
/// 【结局触发器】暴君"真死"那一刻的演出调度员。
///
/// 挂到 game3 场景里的一个【空物体】上（比如新建一个空物体命名 EndingTrigger）。
/// 小泽在 Inspector 里把暴君 TyrantAI 的 onTyrantDeath 事件接到本脚本的 TriggerEnding() 方法上。
///
/// 它负责的时序（全程 unscaledTime，不受暂停影响）：
///   暴君真死 → 冻结玩家 + 隐藏 HUD → 等 startDelay 秒（留给倒地）
///   → 播一句独白「难道这就结束了吗？」→ 等 afterDialogueDelay 秒
///   → 渐黑转场 → 黑透瞬间【相机接管看向罐子房】+ 开演（★ 玩家不再出场，留在原地即可）。
///
/// 罐子房有两种做法（useSameSceneRoom 开关）：
///   ✅ 勾选：罐子房就在【同一个场景】里——黑透瞬间激活罐子房演出物体（相机由 EndingScene 接管）。
///   ⬜ 不勾：罐子房是【另一个 Scene】——渐黑后加载 nextSceneName 那个场景。
/// </summary>
public class EndingTrigger : MonoBehaviour
{
    [Header("时序")]
    [Tooltip("暴君死后等这么久再开始演出（留给倒地/尸体停顿）")]
    public float startDelay = 1.2f;

    [Header("演出前处理")]
    [Tooltip("勾 = 演出一开始就冻结玩家（暴君死后玩家不能再乱走）")]
    public bool freezePlayer = true;
    [Tooltip("演出开始时一起隐藏的 HUD 物体（血条/弹药等，可留空；留空就什么都不隐藏）")]
    public GameObject[] hideDuringEnding;

    [Header("对话")]
    [Tooltip("场景里的对话板（带 DialogueManager 组件的物体）")]
    public DialogueManager dialogueManager;
    [Tooltip("结局那句独白在对话板 Dialogue Groups 数组里的编号；-1 = 不播对话，直接进下一阶段（防呆）")]
    public int endingLineGroupIndex = -1;
    [Tooltip("对话播完再等这么久才渐黑（秒）")]
    public float afterDialogueDelay = 0.5f;
    [Tooltip("等对话结束的兜底超时（秒）：对话没配好时不至于永远卡住")]
    public float dialogueTimeout = 20f;

    [Header("罐子房去向")]
    [Tooltip("勾 = 罐子房就在同一个场景里（黑透瞬间激活演出物体，相机由 EndingScene 接管）；不勾 = 罐子房是另一个 Scene（渐黑后加载场景）")]
    public bool useSameSceneRoom = true;
    [Tooltip("同场景模式：罐子房那个演出物体（黑透瞬间 SetActive(true)，并调它身上的 EndingScene.Play()）")]
    public GameObject vatRoomObject;
    [Tooltip("跨场景模式：罐子房所在场景名（小泽填）")]
    public string nextSceneName = "";
    [Tooltip("同场景模式专用：勾 = 走 FadeController 渐黑→黑透瞬间接管镜头并开演→渐亮（亮起时镜头已在罐子房）；不勾 = 不渐黑，当场硬切过去（由 EndingScene 自己的黑幕接管画面）")]
    public bool fadeThroughBlack = true;

    private bool started = false; // 防重入：暴君事件只允许触发一次

    /// <summary>
    /// 【小泽要接线的方法】把暴君的 onTyrantDeath 事件接到这里。
    /// 暴君真正死亡那一刻会被调用一次，之后重复调用会被忽略并打警告。
    /// </summary>
    public void TriggerEnding()
    {
        if (started)
        {
            Debug.LogWarning("[结局] TriggerEnding 被重复调用，已忽略（结局演出只能触发一次）", this);
            return;
        }
        started = true;
        StartCoroutine(EndingRoutine());
    }

    /// <summary> 结局主流程（协程，全程 unscaledTime / WaitForSecondsRealtime） </summary>
    private IEnumerator EndingRoutine()
    {
        // ── 第 0 步：冻结玩家 + 隐藏 HUD（必须放在 startDelay 等待"之前"） ──
        FreezePlayer();

        // ── 第 1 步：等暴君倒地那一小会儿 ──
        yield return new WaitForSecondsRealtime(startDelay);

        // ── 第 2 步：播那句独白（只有配了对话板 + 编号 >= 0 才播） ──
        if (dialogueManager != null && endingLineGroupIndex >= 0)
        {
            bool finished = false;
            // OnGroupFinished 是 System.Action<int>：+= 订阅，回调用 -= 退订（防内存泄漏）
            System.Action<int> onFinished = null;
            onFinished = (idx) => { finished = true; };
            dialogueManager.OnGroupFinished += onFinished;
            dialogueManager.PlayGroup(endingLineGroupIndex);

            // 等回调触发，但设一个超时上限兜底（对话没配好时不会永远卡住）
            float waited = 0f;
            while (!finished && waited < dialogueTimeout)
            {
                waited += Time.unscaledDeltaTime;
                yield return null;
            }
            dialogueManager.OnGroupFinished -= onFinished; // 退订

            if (!finished)
                Debug.LogWarning("[结局] 等待对话结束超时，直接继续演出（请检查 Dialogue Groups 是否配好了该组）", this);
        }
        else if (dialogueManager == null)
        {
            Debug.LogWarning("[结局] 未拖入 DialogueManager，跳过独白，直接进入渐黑阶段", this);
        }

        // ── 第 3 步：对话后停顿 ──
        yield return new WaitForSecondsRealtime(afterDialogueDelay);

        // ── 第 4 步：转场 ──
        if (useSameSceneRoom)
        {
            if (fadeThroughBlack)
            {
                // 渐黑 → 黑透瞬间接管镜头并开演 → 渐亮（亮起时镜头已经在罐子房，玩家不在画面里）
                FadeController.Transition(() =>
                {
                    BeginVatRoom();
                    FreezePlayer(); // 保险：再冻一次
                });
            }
            else
            {
                // 不渐黑：当场硬切，EndingScene 自己的黑幕接管画面
                BeginVatRoom();
                FreezePlayer(); // 保险：再冻一次
            }
        }
        else
        {
            // 跨场景：不写 spawn（让罐子房场景用自己的默认出生点，避免玩家被塞到 (0,0) 撞墙）
            if (!string.IsNullOrEmpty(nextSceneName))
                FadeController.TransitionToScene(nextSceneName);
            else
                Debug.LogWarning("[结局] useSameSceneRoom=false 但 nextSceneName 为空，无法加载罐子房场景", this);
        }
    }

    /// <summary> 冻结玩家 + 隐藏 HUD（frozen 是 PixelGridMovement 的公开字段，直接赋值，不改那个脚本） </summary>
    private void FreezePlayer()
    {
        if (freezePlayer)
        {
            PixelGridMovement pm = FindObjectOfType<PixelGridMovement>();
            if (pm != null) pm.frozen = true;
        }

        if (hideDuringEnding != null)
        {
            for (int i = 0; i < hideDuringEnding.Length; i++)
                if (hideDuringEnding[i] != null) hideDuringEnding[i].SetActive(false); // 判空防呆
        }
    }

    /// <summary> 激活罐子房演出物体并调它的 EndingScene.Play() </summary>
    private void BeginVatRoom()
    {
        if (vatRoomObject != null)
        {
            vatRoomObject.SetActive(true); // 才露出来（相机随后由 EndingScene 接管看向它）
            EndingScene es = vatRoomObject.GetComponent<EndingScene>();
            if (es != null) es.Play();      // 触发罐子房的完整结局演出（含相机接管）
            else Debug.LogWarning("[结局] vatRoomObject 上没有 EndingScene 组件", vatRoomObject);
        }
        else
        {
            Debug.LogWarning("[结局] useSameSceneRoom=true 但没拖 vatRoomObject，罐子房不会被激活", this);
        }
    }
}
