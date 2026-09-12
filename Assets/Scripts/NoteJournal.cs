using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 纸条收集表（数据层，静态、跨场景保留）——修掉"多张纸条只显示最后一张"的 bug。
///
/// 老做法（NotePaper 静态字典 itemID→文本）的坑：复制纸条时 itemID 相同 → 后者覆盖前者 → 只剩最后一条。
/// 新做法：拾取时【逐条登记】到一个列表里，按拾取顺序保存；列表视图直接遍历列表，
/// 不再靠 itemID 取值 → 各纸条互不覆盖。
///
/// 只存纯数据，不含 UI。NotePaper 拾取时调 Collect；InventoryUI 纸条列表视图读 Notes。
/// </summary>
public static class NoteJournal
{
    /// <summary> 一张纸条的数据（itemID + 标题 + 正文） </summary>
    public class NoteEntry
    {
        public string itemID;
        public string title;
        public string text;
    }

    private static readonly List<NoteEntry> notes = new List<NoteEntry>();

    /// <summary> 已收集的纸条（按拾取顺序，只读） </summary>
    public static IReadOnlyList<NoteEntry> Notes { get { return notes; } }

    /// <summary> 有没有收集到纸条 </summary>
    public static bool HasAny { get { return notes.Count > 0; } }

    /// <summary>
    /// 登记一张纸条：
    /// - 完全一样（itemID + 标题 + 正文都相同）= 同一张纸条（存档重载后重拾等）→ 不重复添加；
    /// - 同 itemID 但标题/正文不同 = 小泽复制纸条时忘改 itemID → 仍然各自收录（这正是原 bug 的修复点）；
    /// - 其余 → 追加到末尾，保持拾取顺序。
    /// </summary>
    public static void Collect(string itemID, string title, string text)
    {
        bool warned = false;
        foreach (NoteEntry n in notes)
        {
            // 同一张纸条（三项全同）→ 已收录，不重复
            if (n.itemID == itemID && n.title == title && n.text == text) return;

            // 同 itemID 但内容不同：提示一次（复制纸条忘改 ID），但仍然各收各的
            if (!warned && !string.IsNullOrEmpty(itemID) && n.itemID == itemID)
            {
                warned = true;
                Debug.LogWarning("[纸条] itemID 重复：" + itemID + "，但标题/正文不同 → 已按不同纸条各自收录"
                    + "（建议给每张纸条填不同的 itemID 便于管理）");
            }
        }
        notes.Add(new NoteEntry { itemID = itemID, title = title, text = text });
    }

    /// <summary> 清空收集表（小泽侧检测/重开游戏用；默认不清空，会话内保留） </summary>
    public static void ClearAll()
    {
        notes.Clear();
    }
}
