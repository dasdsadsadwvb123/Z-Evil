#if UNITY_EDITOR
using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// ID 校验工具（编辑器专用，纯只读，不参与构建）。
///
/// 用途：一键扫描项目里【所有场景】的拾取物配置，把"会导致存档/读档出错"的坑列出来：
///   1. 空 ID            —— PickupItem.itemID 为空
///   2. 重复 ID          —— 跨场景统计，同一个 itemID 出现 2 次以上（已拾取清单是全局的，重名会互相销毁）
///   3. 纸条缺信息        —— 挂了 NotePaper 但标题/正文为空
///   4. 钥匙与卡槽不对应  —— KeyDatabase 卡槽 keyID ↔ 场景里 Key 类道具 itemID 对不上（4b 已排除宝石/纸条等非钥匙道具）
///   5. 枪缺 GunData      —— itemType == Gun 但 gunData 为空（字段污染的来源）
///   6. 箱内物品          —— Container 的 Starter Items：空 itemID / 同箱重复 ID / itemName 为空 / 弹药未配类型数量
///   7. 统计摘要          —— 末尾汇总
///
/// 用法：Unity 顶部菜单 → Tools / Z-Evil / ID 校验 → 看 Console 输出。
/// 只读！不会修改任何东西（不会自动帮小泽改 ID）。
///
/// 实现说明：用 AssetDatabase 找全部 .unity 场景 → EditorSceneManager.OpenScene(Additive) 逐个载入
/// → 遍历根物体上的组件 → 关闭（自己开的才关，已打开的当前场景不动）。
/// 编辑模式下不会触发生命周期方法（Awake 等），所以扫描是安全的、无副作用的。
/// </summary>
public static class IdValidator
{
    // ======== 扫描用的中间记录 ========
    private class PickupRec
    {
        public string scene;
        public string path;
        public string itemID;
        public ItemType type;
        public bool gunMissing;
        public bool hasNote;   // 挂了 NotePaper（纸条，不是钥匙）
    }

    private class NoteRec
    {
        public string scene;
        public string path;
        public bool hasPickup;
        public string title;
        public string text;
    }

    private class SlotRec
    {
        public string scene;
        public string path;
        public int index;
        public string keyID;
        public string keyName;
    }

    [MenuItem("Tools/Z-Evil/ID 校验")]
    public static void Validate()
    {
        // 安全守卫：运行中不能加载/扫描场景
        if (EditorApplication.isPlayingOrWillChangePlaymode)
        {
            Debug.LogWarning("[ID校验] 请先退出运行模式（Play）再执行 ID 校验。");
            return;
        }

        List<PickupRec> pickups = new List<PickupRec>();
        List<NoteRec> notes = new List<NoteRec>();
        List<SlotRec> slots = new List<SlotRec>();

        // 箱内物品扫描缓冲
        int containerCount = 0;
        List<string> cEmptyLines = new List<string>();   // 箱内空 ID
        int cEmptyCount = 0;
        List<string> cDupLines = new List<string>();      // 同一箱子内重复 ID
        int cDupGroups = 0;
        List<string> cNameEmptyLines = new List<string>(); // 箱内 itemName 为空
        List<string> cAmmoBadLines = new List<string>();   // 箱内弹药未配类型/数量（AmmoType=None 或 amount<=0）

        // 全部 GemDoor 的宝石 ID（4b 排除用：宝石是给宝石铁门用的，不是钥匙）
        HashSet<string> gemIDs = new HashSet<string>();

        List<string> scenePaths = CollectScenePaths();

        // ---- 逐个场景扫描 ----
        foreach (string path in scenePaths)
        {
            Scene scene = SceneManager.GetSceneByPath(path);
            bool openedByUs = false;

            if (!scene.IsValid() || !scene.isLoaded)
            {
                try
                {
                    scene = EditorSceneManager.OpenScene(path, OpenSceneMode.Additive);
                    openedByUs = true;
                }
                catch (System.Exception e)
                {
                    Debug.LogWarning("[ID校验] 打不开场景：" + path + "（" + e.Message + "）");
                    continue;
                }
            }

            if (!scene.IsValid()) continue;

            try
            {
                string sceneName = scene.name;

                foreach (GameObject root in scene.GetRootGameObjects())
                {
                    // 1) 所有 PickupItem（含未激活）
                    foreach (PickupItem p in root.GetComponentsInChildren<PickupItem>(true))
                    {
                        pickups.Add(new PickupRec
                        {
                            scene = sceneName,
                            path = HierPath(p.transform),
                            itemID = p.itemID,
                            type = p.itemType,
                            gunMissing = (p.itemType == ItemType.Gun && p.gunData == null),
                            hasNote = (p.GetComponent<NotePaper>() != null)
                        });
                    }

                    // 2) 所有 NotePaper（含未激活）
                    foreach (NotePaper np in root.GetComponentsInChildren<NotePaper>(true))
                    {
                        notes.Add(new NoteRec
                        {
                            scene = sceneName,
                            path = HierPath(np.transform),
                            hasPickup = (np.GetComponent<PickupItem>() != null),
                            title = np.noteTitle,
                            text = np.noteText
                        });
                    }

                    // 3) 所有 KeyDatabase 的卡槽
                    foreach (KeyDatabase kd in root.GetComponentsInChildren<KeyDatabase>(true))
                    {
                        if (kd.keys == null) continue;
                        for (int i = 0; i < kd.keys.Length; i++)
                        {
                            KeyDatabase.KeySlotEntry slot = kd.keys[i];
                            if (slot == null) continue;
                            slots.Add(new SlotRec
                            {
                                scene = sceneName,
                                path = HierPath(kd.transform),
                                index = i,
                                keyID = slot.keyID,
                                keyName = slot.keyName
                            });
                        }
                    }

                    // 4) 所有 Container 的箱内物品（读 Starter Items —— 这是编辑期真正存下来的配置；
                    //    contents 是运行时才由 Start 从 starterItems 生成的，编辑模式下是空的，扫不到）
                    foreach (Container c in root.GetComponentsInChildren<Container>(true))
                    {
                        containerCount++;
                        string cpath = HierPath(c.transform);
                        Container.StarterEntry[] arr = c.starterItems;
                        if (arr == null) continue;

                        Dictionary<string, int> countByID = new Dictionary<string, int>();
                        for (int i = 0; i < arr.Length; i++)
                        {
                            Container.StarterEntry e = arr[i];
                            if (e == null) continue;
                            int n = Mathf.Max(1, e.count);
                            string itemTag = string.IsNullOrEmpty(e.itemID) ? "(空)" : e.itemID;

                            // ① 空 itemID → 存档"已取走"计数会错乱
                            if (string.IsNullOrEmpty(e.itemID))
                            {
                                cEmptyCount += n;
                                cEmptyLines.Add("   · " + sceneName + " / " + cpath + " [" + c.type + "] 第 " + (i + 1)
                                    + " 件（数量 " + n + "，类型 " + e.itemType + "）");
                            }
                            else
                            {
                                if (!countByID.ContainsKey(e.itemID)) countByID[e.itemID] = 0;
                                countByID[e.itemID] += n;
                            }

                            // ③ itemName 为空 → 背包里显示空白
                            if (string.IsNullOrEmpty(e.itemName))
                                cNameEmptyLines.Add("   · " + sceneName + " / " + cpath + " [" + c.type + "] 第 " + (i + 1)
                                    + " 件 itemID[" + itemTag + "]");

                            // ④ 箱内弹药未配类型/数量（Ammo 类型但 Ammo Type=None 或 Amount<=0）
                            //    → 取出时加 0 发、无提示（本次实测踩到的坑）
                            if (e.itemType == ItemType.Ammo
                                && (e.ammoType == AmmoType.None || e.ammoAmount <= 0))
                            {
                                cAmmoBadLines.Add("   · " + sceneName + " / " + cpath + " [" + c.type + "] 第 " + (i + 1)
                                    + " 件 itemID[" + itemTag + "]（AmmoType=" + e.ammoType
                                    + ", AmmoAmount=" + e.ammoAmount + "）");
                            }
                        }

                        // ② 同一箱子内重复 itemID（同一 ID 出现 ≥2 件 → 取走一件会被算成多件）
                        foreach (KeyValuePair<string, int> kv in countByID)
                        {
                            if (kv.Value >= 2)
                            {
                                cDupGroups++;
                                cDupLines.Add("   · " + sceneName + " / " + cpath + " [" + c.type
                                    + "] 内 [" + kv.Key + "] 共 " + kv.Value + " 件");
                            }
                        }
                    }

                    // 5) 所有 GemDoor 的宝石 ID（4b 排除用：宝石给宝石铁门用，不是钥匙）
                    foreach (GemDoor gd in root.GetComponentsInChildren<GemDoor>(true))
                    {
                        if (!string.IsNullOrEmpty(gd.gemOrangeID)) gemIDs.Add(gd.gemOrangeID);
                        if (!string.IsNullOrEmpty(gd.gemPinkID)) gemIDs.Add(gd.gemPinkID);
                        if (!string.IsNullOrEmpty(gd.gemRubyID)) gemIDs.Add(gd.gemRubyID);
                    }
                }
            }
            finally
            {
                // 只关"自己开出来的"场景；用户当前打开的场景绝不动
                if (openedByUs && scene.IsValid())
                    EditorSceneManager.CloseScene(scene, true);
            }
        }

        // ---- 汇总报表 ----
        StringBuilder sb = new StringBuilder();
        sb.AppendLine("════════ Z-Evil ID 校验报表 ════════");
        sb.AppendLine("已扫描场景 " + scenePaths.Count + " 个 / 拾取物 " + pickups.Count
            + " 个 / 纸条 " + notes.Count + " 个 / 钥匙卡槽 " + slots.Count + " 个 / 箱子 " + containerCount + " 个");
        sb.AppendLine();

        int emptyCount = 0, dupGroups = 0, noteBadCount = 0, slotMissing = 0, slotUnusedKeys = 0, gunMissingCount = 0;

        // ① 空 ID
        List<PickupRec> empties = pickups.FindAll(p => string.IsNullOrEmpty(p.itemID));
        emptyCount = empties.Count;
        AppendSection(sb, "① 空 ID（PickupItem.itemID 为空）", empties.Count);
        foreach (PickupRec p in empties)
            sb.AppendLine("   · " + p.scene + " / " + p.path + "   [" + p.type + "]");
        sb.AppendLine();

        // ② 重复 ID（跨全部场景）
        Dictionary<string, List<PickupRec>> byID = new Dictionary<string, List<PickupRec>>();
        foreach (PickupRec p in pickups)
        {
            if (string.IsNullOrEmpty(p.itemID)) continue;
            if (!byID.ContainsKey(p.itemID)) byID[p.itemID] = new List<PickupRec>();
            byID[p.itemID].Add(p);
        }
        List<string> dupIDs = new List<string>();
        foreach (KeyValuePair<string, List<PickupRec>> kv in byID)
            if (kv.Value.Count >= 2) dupIDs.Add(kv.Key);
        dupIDs.Sort();
        dupGroups = dupIDs.Count;
        AppendSection(sb, "② 重复 ID（跨场景统计；同 ID 会导致读档后互相销毁）", dupIDs.Count);
        foreach (string id in dupIDs)
        {
            List<PickupRec> list = byID[id];
            sb.AppendLine("   · [" + id + "] 共 " + list.Count + " 处：");
            foreach (PickupRec p in list)
                sb.AppendLine("        - " + p.scene + " / " + p.path + "   [" + p.type + "]");
        }
        if (dupIDs.Count == 0) sb.AppendLine("   （无）");
        sb.AppendLine();

        // ③ 纸条缺信息
        List<NoteRec> badNotes = notes.FindAll(n =>
            string.IsNullOrEmpty(n.title) || string.IsNullOrEmpty(n.text) || !n.hasPickup);
        noteBadCount = notes.FindAll(n => string.IsNullOrEmpty(n.title) || string.IsNullOrEmpty(n.text)).Count;
        AppendSection(sb, "③ 纸条缺信息（标题/正文为空，或没挂 PickupItem）", badNotes.Count);
        foreach (NoteRec n in badNotes)
        {
            string why = !n.hasPickup ? "没挂 PickupItem"
                       : (string.IsNullOrEmpty(n.title) && string.IsNullOrEmpty(n.text) ? "标题和正文都空"
                       : (string.IsNullOrEmpty(n.title) ? "标题为空" : "正文为空"));
            sb.AppendLine("   · " + n.scene + " / " + n.path + "   → " + why);
        }
        if (badNotes.Count == 0) sb.AppendLine("   （无）");
        sb.AppendLine();

        // ④ 钥匙与卡槽对应
        //   4a：卡槽填了 keyID，但场景里找不到对应 itemID 的道具
        //   4b：场景里有 Key 类道具，但没有任何卡槽用到它的 itemID
        HashSet<string> slotIDs = new HashSet<string>();
        foreach (SlotRec s in slots)
            if (!string.IsNullOrEmpty(s.keyID)) slotIDs.Add(s.keyID);

        List<SlotRec> missingSlots = slots.FindAll(s =>
            !string.IsNullOrEmpty(s.keyID)
            && !byID.ContainsKey(s.keyID)); // 场景里没有 itemID == keyID 的拾取物
        slotMissing = missingSlots.Count;

        List<PickupRec> keyPickups = pickups.FindAll(p => p.type == ItemType.Key && !string.IsNullOrEmpty(p.itemID));
        // 4b 候选：Key 类、有 ID、没被任何卡槽用到；并排除"非开门用途"的三类噪音——
        //   ① 挂了 NotePaper（纸条，走纸条收集表）② itemID 是某 GemDoor 的宝石（给宝石铁门用）③ itemType==Gem
        List<PickupRec> unusedKeys = keyPickups.FindAll(p =>
            !slotIDs.Contains(p.itemID)
            && !p.hasNote
            && p.type != ItemType.Gem
            && !gemIDs.Contains(p.itemID));
        slotUnusedKeys = unusedKeys.Count;

        AppendSection(sb, "④ 钥匙与卡槽对应", missingSlots.Count + unusedKeys.Count);
        sb.AppendLine("   4a) 卡槽填了 ID 但场景里找不到对应道具（" + missingSlots.Count + " 个）：");
        foreach (SlotRec s in missingSlots)
            sb.AppendLine("        · " + s.scene + " / " + s.path + " 卡槽" + s.index
                + " → keyID[" + s.keyID + "]（名：" + s.keyName + "）");
        if (missingSlots.Count == 0) sb.AppendLine("        （无）");
        sb.AppendLine("   4b) 场景里有 Key 类道具但没有任何卡槽用到（" + unusedKeys.Count + " 个）：");
        sb.AppendLine("        （提示：宝石/纸条/蜡烛等非开门用途的道具可忽略；想彻底消除误报可把它们的 Item Type 改成 Gem/Manual）");
        foreach (PickupRec p in unusedKeys)
            sb.AppendLine("        · " + p.scene + " / " + p.path + "   itemID[" + p.itemID + "]");
        if (unusedKeys.Count == 0) sb.AppendLine("        （无）");
        sb.AppendLine();

        // ⑤ 枪缺 GunData
        List<PickupRec> gunBad = pickups.FindAll(p => p.gunMissing);
        gunMissingCount = gunBad.Count;
        AppendSection(sb, "⑤ 枪缺 GunData（itemType=Gun 但 gunData 为空）", gunBad.Count);
        foreach (PickupRec p in gunBad)
            sb.AppendLine("   · " + p.scene + " / " + p.path + "   itemID[" + (string.IsNullOrEmpty(p.itemID) ? "(空)" : p.itemID) + "]");
        if (gunBad.Count == 0) sb.AppendLine("   （无）");
        sb.AppendLine();

        // ⑥ 箱内物品（Container 的 Starter Items）
        AppendSection(sb, "⑥ 箱内物品（储物柜/密码箱/可打碎箱里的 Starter Items）",
            cEmptyLines.Count + cDupGroups + cNameEmptyLines.Count + cAmmoBadLines.Count);
        sb.AppendLine("   6a) 箱内空 itemID（" + cEmptyCount + " 件）——会导致存档'已取走'计数错乱，必须填：");
        foreach (string line in cEmptyLines) sb.AppendLine(line);
        if (cEmptyLines.Count == 0) sb.AppendLine("        （无）");
        sb.AppendLine("   6b) 同一箱子内重复 itemID（" + cDupGroups + " 组）——取走一件会被算成多件：");
        foreach (string line in cDupLines) sb.AppendLine(line);
        if (cDupLines.Count == 0) sb.AppendLine("        （无）");
        sb.AppendLine("   6c) 箱内 itemName 为空（" + cNameEmptyLines.Count + " 件）——背包/箱内显示空白：");
        foreach (string line in cNameEmptyLines) sb.AppendLine(line);
        if (cNameEmptyLines.Count == 0) sb.AppendLine("        （无）");
        sb.AppendLine("   6d) 箱内弹药未配类型/数量（" + cAmmoBadLines.Count + " 件）——取出会加 0 发、无提示，请补 Ammo Type/Ammo Amount：");
        foreach (string line in cAmmoBadLines) sb.AppendLine(line);
        if (cAmmoBadLines.Count == 0) sb.AppendLine("        （无）");
        sb.AppendLine();

        // ⑦ 统计摘要
        sb.AppendLine("──── 统计摘要 ────");
        sb.Append("共扫描 " + pickups.Count + " 个拾取物、");
        sb.Append(containerCount + " 个箱子：");
        sb.Append("空 ID " + emptyCount + " 个");
        sb.Append(" / 重复 ID " + dupGroups + " 组");
        sb.Append(" / 纸条缺信息 " + noteBadCount + " 个");
        sb.Append(" / 卡槽未对应 " + slotMissing + " 个");
        sb.Append(" / 枪缺 GunData " + gunMissingCount + " 个");
        sb.Append(" / 箱内空 ID " + cEmptyCount + " 件");
        sb.Append(" / 箱内重复 " + cDupGroups + " 组");
        sb.Append(" / 箱内名字空 " + cNameEmptyLines.Count + " 件");
        sb.Append(" / 箱内弹药未配置 " + cAmmoBadLines.Count + " 件");
        sb.AppendLine();
        if (emptyCount + dupGroups + noteBadCount + slotMissing + slotUnusedKeys + gunMissingCount
            + cEmptyCount + cDupGroups + cNameEmptyLines.Count + cAmmoBadLines.Count == 0)
            sb.AppendLine("✔ 没发现问题，配置很干净！");
        sb.AppendLine("════════════════════════════════════");

        // 有严重项（空 ID / 重复 ID / 枪缺数据 / 卡槽漏配 / 箱内问题）→ 用 Warning 颜色提醒，否则普通 Log
        string text = sb.ToString();
        if (emptyCount + dupGroups + slotMissing + gunMissingCount + cEmptyCount + cDupGroups + cAmmoBadLines.Count > 0)
            Debug.LogWarning(text);
        else
            Debug.Log(text);
    }

    // ======== 工具 ========

    private static void AppendSection(StringBuilder sb, string title, int count)
    {
        sb.AppendLine("【" + title + "】" + (count > 0 ? "发现 " + count + " 处 ⚠" : "无问题"));
    }

    /// <summary> 收集项目里所有场景路径（AssetDatabase 全量 + Build Settings 里的，去重） </summary>
    private static List<string> CollectScenePaths()
    {
        List<string> paths = new List<string>();

        foreach (string guid in AssetDatabase.FindAssets("t:Scene"))
        {
            string p = AssetDatabase.GUIDToAssetPath(guid);
            if (!string.IsNullOrEmpty(p) && p.EndsWith(".unity") && !paths.Contains(p))
                paths.Add(p);
        }
        foreach (EditorBuildSettingsScene s in EditorBuildSettings.scenes)
        {
            if (s != null && !string.IsNullOrEmpty(s.path) && !paths.Contains(s.path))
                paths.Add(s.path);
        }
        return paths;
    }

    /// <summary> 物体的层级路径（父/子/孙），方便在 Hierarchy 里搜索定位 </summary>
    private static string HierPath(Transform t)
    {
        string path = t.name;
        Transform cur = t.parent;
        while (cur != null)
        {
            path = cur.name + "/" + path;
            cur = cur.parent;
        }
        return path;
    }
}
#endif
