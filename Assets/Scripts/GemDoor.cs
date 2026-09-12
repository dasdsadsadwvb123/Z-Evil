using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 宝石铁门（绑定传送器的镶嵌谜题）：
/// 铁门传送器勾"需要机关"（Portal.startLocked = true），玩家靠近按 F 不传送而是弹出背包——
/// 背包里选中宝石按 E 镶嵌：三颗宝石（橙/粉/红）不分顺序，全部镶上 → 门开音效 + 传送器 Unlock → 恢复正常传送。
///
/// 使用规则：宝石必须【靠近铁门】使用才生效（远处按 E 提示"要靠近铁门才能镶嵌"，宝石不消耗）。
/// 镶嵌状态运行时记录、不进存档（读档重镶，和推箱机关锁同语义）。
///
/// 挂载：和传送器（Portal）同一个物体上——自动 GetComponent 拿绑定传送器，全镶完调它的 Unlock()。
/// 音效走 AudibleAudio.PlayAt 距离听声惯例。
/// </summary>
public class GemDoor : MonoBehaviour
{
    [Header("门信息")]
    [Tooltip("进圈提示文字（未镶完时显示，如'最终关前厅——镶嵌 3 颗宝石'）；镶完后恢复正常传送提示")]
    public string doorName = "最终关前厅——镶嵌 3 颗宝石";

    [Header("三颗宝石（itemID 必须和宝石拾取物的 Item ID 一模一样，区分大小写）")]
    public string gemOrangeID = "GemOrange";
    public string gemPinkID = "GemPink";
    public string gemRubyID = "GemRuby";

    [Header("音效（不拖 = 静音；走距离听声惯例）")]
    [Tooltip("镶嵌声（每颗共用）")]
    public AudioClip embedClip;
    [Tooltip("三颗全镶完的门开声")]
    public AudioClip doorOpenClip;

    [Header("交互范围")]
    [Tooltip("玩家离门多近才能镶嵌宝石")]
    public float interactRange = 1.5f;

    // 嵌入状态（运行时记录，不进存档）
    private bool orangeDone = false;
    private bool pinkDone = false;
    private bool rubyDone = false;

    /// <summary> 三颗是否全部镶完（Portal 查它决定按 F 是弹背包还是传送） </summary>
    public bool IsComplete { get { return orangeDone && pinkDone && rubyDone; } }

    // 全场景宝石门注册表：TryEmbed 是静态入口，靠它找到"玩家附近的门"
    private static readonly List<GemDoor> allDoors = new List<GemDoor>();

    private Transform player;

    private void Awake()
    {
        allDoors.Add(this);
    }

    private void Start()
    {
        GameObject p = GameObject.FindGameObjectWithTag("Player");
        if (p != null) player = p.transform;

        // 读档自查：三颗宝石的镶嵌状态从世界进度表整位还原（bit0=橙 bit1=粉 bit2=红）
        int v = WorldState.Get(WorldState.KeyFor("Gem", this), 0);
        orangeDone = (v & 1) != 0;
        pinkDone = (v & 2) != 0;
        rubyDone = (v & 4) != 0;
        if (IsComplete)
            Debug.Log("[宝石门] 读档还原：三颗宝石已镶完", gameObject);
    }

    private void OnDestroy()
    {
        allDoors.Remove(this);
    }

    /// <summary> 玩家是否在交互范围内（Portal 弹背包/提示用） </summary>
    public bool IsPlayerNear()
    {
        return player != null && Vector2.Distance(transform.position, player.position) <= interactRange;
    }

    /// <summary> 这个 itemID 是不是本门的宝石之一 </summary>
    private bool Matches(string itemID)
    {
        return itemID == gemOrangeID || itemID == gemPinkID || itemID == gemRubyID;
    }

    /// <summary> 这颗宝石是不是还没镶上 </summary>
    private bool SlotOpen(string itemID)
    {
        if (itemID == gemOrangeID) return !orangeDone;
        if (itemID == gemPinkID) return !pinkDone;
        if (itemID == gemRubyID) return !rubyDone;
        return false;
    }

    private void MarkEmbedded(string itemID)
    {
        if (itemID == gemOrangeID) orangeDone = true;
        else if (itemID == gemPinkID) pinkDone = true;
        else if (itemID == gemRubyID) rubyDone = true;
        SaveGemState();
    }

    /// <summary> 把三颗镶嵌状态写进世界进度表（读档还原用） </summary>
    private void SaveGemState()
    {
        int v = (orangeDone ? 1 : 0) | (pinkDone ? 2 : 0) | (rubyDone ? 4 : 0);
        WorldState.Set(WorldState.KeyFor("Gem", this), v);
    }

    // ======== 静态入口（InventoryUI 的 E 键 / Portal 判定调用） ========

    /// <summary> 这个 itemID 是不是任何一扇宝石门的宝石（背包里决定"使用"选项显不显示） </summary>
    public static bool IsGemItem(string itemID)
    {
        if (string.IsNullOrEmpty(itemID)) return false;
        foreach (GemDoor door in allDoors)
        {
            if (door.Matches(itemID)) return true;
        }
        return false;
    }

    /// <summary>
    /// 尝试镶嵌宝石（背包里按 E 调用）：
    /// 玩家在任一宝石门附近 + itemID 匹配 + 该颗未镶 → 镶上 + 播声 + 从背包移除 + 返回 true；
    /// 不满足（不在门边/已镶过/不是宝石）→ 返回 false，宝石保留在背包里。
    /// 三颗全镶完 → 播门开声 + 调绑定传送器的 Unlock()（恢复按 F 正常传送）。
    /// </summary>
    public static bool TryEmbed(string itemID)
    {
        // 找玩家背包（从背包里移除宝石用）
        Inventory inventory = null;
        GameObject p = GameObject.FindGameObjectWithTag("Player");
        if (p != null) inventory = p.GetComponent<Inventory>();
        if (inventory == null) return false;

        // 找"玩家附近的、还没镶上这颗的"宝石门——不分顺序，哪扇门近用哪扇
        GemDoor target = null;
        foreach (GemDoor door in allDoors)
        {
            if (door.IsPlayerNear() && door.Matches(itemID) && door.SlotOpen(itemID))
            {
                target = door;
                break;
            }
        }
        if (target == null) return false; // 不在门边 / 已镶过 → 不消耗

        // 从背包移除宝石（RemoveItemAt 会修正装备索引 + 刷新存档快照）
        int index = -1;
        for (int i = 0; i < inventory.items.Count; i++)
        {
            if (inventory.items[i] != null && inventory.items[i].itemID == itemID)
            {
                index = i;
                break;
            }
        }
        if (index < 0) return false;
        inventory.RemoveItemAt(index);

        // 镶上 + 镶嵌声 + toast
        target.MarkEmbedded(itemID);
        AudibleAudio.PlayAt(target.embedClip, target.transform.position);
        inventory.OnItemNotice?.Invoke("已镶嵌宝石");
        Debug.Log("[宝石门] " + target.name + " 镶上了一颗宝石（" + itemID + "）", target.gameObject);

        // 三颗全齐 → 门开！
        if (target.IsComplete)
        {
            AudibleAudio.PlayAt(target.doorOpenClip, target.transform.position);
            Portal boundPortal = target.GetComponent<Portal>(); // 宝石门和传送器同物体
            if (boundPortal != null) boundPortal.Unlock();
            Debug.Log("[宝石门] " + target.name + " 三颗宝石全部镶完，门开了！", target.gameObject);
        }
        return true;
    }
}
