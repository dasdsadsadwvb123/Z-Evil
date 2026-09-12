using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;

/// <summary>
/// 开关板（推箱子解谜的机关总闸）：自动收集场景里所有压力板（板子 Start 时自己注册，零接线）。
/// 全部板子压上 → 触发 onAllActivated（接开门/掉钥匙）；从全压状态被推开任何一块 → 触发 onDeactivated（门重新关上）。
/// 触发模式 Inspector 可选：一次性（triggerOnce=true，永久开关）/ 可重复（默认，推开再全压上会再触发，接门锁更灵活）。
/// 挂法：建一个空物体（如 PlateSwitch）挂上本脚本即可——不用拖任何引用。
/// </summary>
public class PlateSwitchBoard : MonoBehaviour
{
    [Header("触发模式")]
    [Tooltip("勾上 = 全压上只触发一次（永久开关，比如奖励房）；不勾 = 可重复（推开复位，再全压上再触发，默认）")]
    public bool triggerOnce = false;

    [Header("机关事件（Inspector 里接线）")]
    [Tooltip("全部压力板压下时触发——接开门/掉钥匙/播剧情")]
    public UnityEvent onAllActivated;
    [Tooltip("从'全压上'状态被推开任何一块时触发——接关门/机关复位")]
    public UnityEvent onDeactivated;

    private readonly List<PressurePlate> plates = new List<PressurePlate>(); // 自动注册的板
    private bool allPressedPrev = false; // 上一帧是否全部压上（检测"全压→被推开"的边界）
    private bool hasFired = false;       // 是否已经触发过（triggerOnce 用）
    private string worldKey;             // 世界进度表钥匙（读档还原"机关触发没触发"）
    private bool restoreChecked = false; // 读档自查只跑一次

    private void Awake()
    {
        worldKey = WorldState.KeyFor("Plate", this);
    }

    /// <summary> 压力板 Start 时自动调用注册自己（不用手动拖） </summary>
    public void Register(PressurePlate plate)
    {
        if (plate != null && !plates.Contains(plate))
        {
            plates.Add(plate);
            Debug.Log("[开关板] 注册压力板 " + plate.name + "（当前共 " + plates.Count + " 块）", gameObject);
        }
    }

    private void Update()
    {
        if (plates.Count == 0) return; // 还没板子注册进来

        // 读档自查（只跑一次，等板子都注册完再执行）：
        // 世界进度表记录过"机关已触发" → 补触发一次下游事件（开门/停倒计时等），保证读档后状态一致。
        // 注意：压力板的物理位置不进存档（读档后板子回到初始未压状态），这里只还原"机关的结果"。
        if (!restoreChecked)
        {
            restoreChecked = true;
            if (WorldState.GetBool(worldKey))
            {
                onAllActivated.Invoke();
                hasFired = true;
                Debug.Log("[开关板] 读档还原：机关处于'已触发'状态，已补触发一次下游事件", gameObject);
                return; // 本帧到此为止：allPressedPrev 保持 false，下一帧不会误判"被推开"
            }
        }

        // 全部压上了吗？
        bool all = true;
        foreach (PressurePlate p in plates)
        {
            if (p == null || !p.IsPressed) { all = false; break; }
        }

        // 边界：不是全压 → 全压 = 触发
        if (all && !allPressedPrev)
        {
            if (!hasFired || !triggerOnce)
            {
                onAllActivated.Invoke();
                Debug.Log("[开关板] " + plates.Count + " 块压力板全部压下！机关触发" + (triggerOnce ? "（一次性）" : ""));
            }
            hasFired = true;
            WorldState.Set(worldKey, 1); // 世界进度表登记"已触发"
        }

        // 边界：全压 → 被推开 = 复位
        if (!all && allPressedPrev)
        {
            onDeactivated.Invoke();
            if (!triggerOnce) hasFired = false; // 可重复模式：复位后还能再触发；一次性模式：触发过就不再触发
            WorldState.Set(worldKey, 0); // 世界进度表登记"已复位"
            Debug.Log("[开关板] 有压力板被推开，机关复位");
        }

        allPressedPrev = all;
    }
}
