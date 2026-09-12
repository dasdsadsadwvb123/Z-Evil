using UnityEngine;

/// <summary>
/// 纸条（挂在纸条拾取物上，和 PickupItem 同一个物体）。
///
/// 【新设计】拾取纸条 → 不进 TAB 背包，而是把（itemID / 标题 / 正文）登记进 NoteJournal 纸条收集表
/// （按拾取逐条，各纸条互不覆盖）；TAB 打开背包后按 Q 切到【纸条列表】，WASD 选、E 阅读。
/// 登记时机：PlayerPickup 命中 F 时调 CollectSelf()（此时物体还没销毁，数据已进收集表）。
///
/// 旧接口 TryGetText / TryGetTitle 保留（转查 NoteJournal），供"背包里可能还有纸条条目"的旧路径兼容，不再抛错。
///
/// 摆法：SpriteRenderer（纸条图）+ PickupItem（itemID，建议每张纸条填【不同】的 ID）+ NotePaper（填标题/正文）。
/// </summary>
public class NotePaper : MonoBehaviour
{
    [Header("纸条内容（小泽自己编辑）")]
    [Tooltip("纸条标题（纸条列表里显示这一行；默认'纸条'，可改成'撕下的日记页'之类）")]
    public string noteTitle = "纸条";
    [Tooltip("纸条正文（\\n 换行；Inspector 里直接打字）")]
    [TextArea(4, 12)]
    public string noteText = "（在 Inspector 里写纸条内容）";
    [Tooltip("拾取时底部提示文字")]
    public string pickupMessage = "获得：一张纸条";

    private void Start()
    {
        // 一次性就位日志（方便小泽确认纸条配好了；不刷屏）
        PickupItem pickup = GetComponent<PickupItem>();
        Debug.Log("[纸条] 已就位：" + noteTitle + "（itemID: " + GetNoteID(pickup) + "）", gameObject);
    }

    /// <summary> 登记自己到纸条收集表（由 PlayerPickup 拾取时调用）。itemID 为空时用物体名兜底 </summary>
    public void CollectSelf()
    {
        PickupItem pickup = GetComponent<PickupItem>();
        NoteJournal.Collect(GetNoteID(pickup), noteTitle, noteText);
    }

    private string GetNoteID(PickupItem pickup)
    {
        if (pickup != null && !string.IsNullOrEmpty(pickup.itemID)) return pickup.itemID;
        return gameObject.name;
    }

    // ======== 旧接口兼容（转查 NoteJournal；不再做 itemID 覆盖注册） ========

    /// <summary> 查纸条正文（按 itemID；找不到返回 null） </summary>
    public static string TryGetText(string itemID)
    {
        NoteJournal.NoteEntry n = Find(itemID);
        return n != null ? n.text : null;
    }

    /// <summary> 查纸条标题（按 itemID；找不到返回 null） </summary>
    public static string TryGetTitle(string itemID)
    {
        NoteJournal.NoteEntry n = Find(itemID);
        return n != null ? n.title : null;
    }

    private static NoteJournal.NoteEntry Find(string itemID)
    {
        if (string.IsNullOrEmpty(itemID)) return null;
        foreach (NoteJournal.NoteEntry n in NoteJournal.Notes)
            if (n.itemID == itemID) return n;
        return null;
    }
}
