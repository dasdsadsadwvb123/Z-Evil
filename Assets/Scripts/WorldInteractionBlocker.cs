using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 挂在操作面板的显示根节点上：面板打开时屏蔽场景交互，关闭当帧也不放行。
/// UI 自己继续使用 Input 处理取物、返回等操作；HUD 和交互提示不挂此组件。
/// </summary>
[DisallowMultipleComponent]
public sealed class WorldInteractionBlocker : MonoBehaviour
{
    private static readonly HashSet<WorldInteractionBlocker> activePanels = new HashSet<WorldInteractionBlocker>();
    private static int lastClosedFrame = -1;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetState()
    {
        activePanels.Clear();
        lastClosedFrame = -1;
    }

    public static void Attach(GameObject panel)
    {
        if (panel != null && panel.GetComponent<WorldInteractionBlocker>() == null)
            panel.AddComponent<WorldInteractionBlocker>();
    }

    public static bool IsBlocked { get { return IsBlockedExcept(null); } }

    // 公告牌允许 F 关闭自己的正文，但不能穿过其他面板去操作公告牌。
    public static bool IsBlockedExcept(GameObject ownPanel)
    {
        if (Time.timeScale <= 0f || Time.frameCount == lastClosedFrame) return true;
        foreach (WorldInteractionBlocker panel in activePanels)
            if (panel != null && panel.isActiveAndEnabled && panel.gameObject != ownPanel) return true;
        return false;
    }

    public static bool GetKeyDown(KeyCode key)
    {
        return !IsBlocked && Input.GetKeyDown(key);
    }

    private void OnEnable() { activePanels.Add(this); }

    private void OnDisable()
    {
        if (activePanels.Remove(this)) lastClosedFrame = Time.frameCount;
    }
}
