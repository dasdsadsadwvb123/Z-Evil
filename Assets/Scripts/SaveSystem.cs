using UnityEngine;
using UnityEngine.SceneManagement;
using System.Collections.Generic;
using System.IO;
using System.Text;

/// <summary>
/// 存档系统：把 场景/存档点/玩家血量/背包/弹药/已拾取物品/世界进度表 打包成 JSON，存成 UTF-8 文件。
/// 挂到玩家（Player）物体上（需要和 Inventory、HealthSystem、PixelGridMovement 同物体）。
///
/// 单存档位：存档点按 F 保存即覆盖；读档 = 精确回到存档那一刻（生化 2 式）。
/// - 玩家位置 = 存档点坐标（不是存档瞬间的站位）
/// - 所有世界状态（门/密码箱/宝石/开关/倒计时/怪死没死……）存进 WorldState 表，读档整表还原
/// - 已拾取物品清单读档"覆盖"（不是合并），保证"存档那一刻之后捡的东西也不会复活"
///
/// 关键知识点：JSON 只能存"文字"，存不了资产引用（GunData/Sprite 等）。
/// 所以存档时记枪的名字（gunName），读档时按名字从 Inventory.allGunDatas 找回引用。
/// </summary>
public class SaveSystem : MonoBehaviour
{
    [Tooltip("存档文件名（存在游戏数据目录里）")]
    public string saveFileName = "ZEvil_Save.json";

    [Tooltip("没有存档、死亡后要“回关卡开头”时加载哪个场景；留空 = 加载 Build Settings 里第 0 个场景")]
    public string restartSceneName = "";

    /// <summary> 存档文件完整路径（游戏数据目录，UTF-8 文件存储，中文安全） </summary>
    private string SaveFilePath
    {
        get { return Path.Combine(Application.persistentDataPath, saveFileName); }
    }

    // 单存档位：存档数据（要能被 JsonUtility 序列化，必须 [System.Serializable]）
    [System.Serializable]
    public class SaveData
    {
        public string sceneName;          // 存档时的场景名
        public string savePointId;        // 触发存档的存档点 ID（读档时按它把玩家放回存档点）
        public float playerX;             // 玩家网格坐标（兜底：旧档没有存档点 ID 时用它）
        public float playerY;
        public int currentHealth;         // 当前血量
        public int maxHealth;             // 最大血量
        public int equippedIndex;         // 装备的武器索引
        public int reservePistol;         // 备用弹药（三种枪）
        public int reserveShotgun;
        public int reserveEagle;
        public List<SaveItemData> items;  // 背包物品（转换成可存文字的版本）
        public List<string> collectedItems; // 已拾取物品的 ID（场景重载后这些物品不复活）
        public List<WorldState.Entry> worldState; // 世界进度表（门/机关/怪死亡……）
    }

    [System.Serializable]
    public class SaveItemData
    {
        public string itemID;
        public string itemName;
        public int itemType;              // 枚举存成 int
        public string description;
        public string gunName;            // 枪的名字（读档时找回 GunData 引用）
        public int currentAmmo;           // 弹夹内子弹
        public int magSize;               // 弹夹容量
        public int currentDurability;     // 当前耐久
        public int maxDurability;         // 耐久上限
        public int herbType;              // 草药类型
        public int healAmount;            // 草药恢复量
        // 注意：icon 图标存不了（JSON 不能存资产引用），读档后靠 Inventory.itemTemplates 按 itemID 找回
    }

    // 读档时暂存数据。用 static（静态）跨场景传递：
    // 场景重载会销毁本脚本实例，但 static 字段属于"类"不属于"实例"，销毁了也还在。
    // 新场景的实例会在 Awake 里先恢复 WorldState/已拾取清单，Update 第一帧再恢复玩家数据。
    private static SaveData pendingData = null;
    private bool restoreChecked = false; // 保证恢复逻辑只跑一次

    // ======== 已拾取物品清单（防复活/防刷物品） ========
    // static：跨场景保留，拾取过的物品在场景重载后检查这里并销毁自己
    private static List<string> collectedItems = new List<string>();

    // 编辑器友好：每次进 Play 自动清空读档暂存，杜绝串档
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStaticsOnPlay()
    {
        pendingData = null;
        collectedItems = new List<string>();
    }

    /// <summary>
    /// 生成物品的"防复活钥匙"：优先用 itemID；没填 ID 就用「场景名_物体名」兜底。
    /// 兜底方案能防止"弹药包没填 ID → 读档后无限刷子弹"的问题。
    /// </summary>
    private static string GetPickupKey(string itemID, string fallbackName)
    {
        if (!string.IsNullOrEmpty(itemID)) return itemID;
        string scene = SceneManager.GetActiveScene().name;
        return scene + "_" + fallbackName;
    }

    /// <summary> 记录一个物品已被拾取（去重）。fallbackName 是物品的物体名（itemID 为空时兜底用） </summary>
    public static void CollectItem(string itemID, string fallbackName)
    {
        string key = GetPickupKey(itemID, fallbackName);
        if (!collectedItems.Contains(key))
            collectedItems.Add(key);

        // 没填 itemID 时提醒玩家（最佳实践：每个物品填唯一 ID）
        if (string.IsNullOrEmpty(itemID))
            Debug.LogWarning("[存档] 物品 '" + fallbackName + "' 没填 itemID，已用「场景名_物体名」兜底防复活。建议在 Inspector 里给它填一个唯一 ID！");
    }

    /// <summary> 这个物品是否已被拾取过（同时兼容旧档的 OBJ_ 前缀钥匙） </summary>
    public static bool IsCollected(string itemID, string fallbackName)
    {
        if (collectedItems.Contains(GetPickupKey(itemID, fallbackName))) return true;
        // 旧档兼容：老版本用 "OBJ_物体名" 兜底，这里也认，避免旧档里捡过的物品复活
        return collectedItems.Contains("OBJ_" + fallbackName);
    }

    /// <summary> 清空"已拾取清单"（回关卡开头重来时调） </summary>
    public static void ClearCollectedItems()
    {
        collectedItems.Clear();
    }

    /// <summary> 有没有存档文件（主菜单"继续游戏"判断用） </summary>
    public bool HasSaveFile()
    {
        return File.Exists(SaveFilePath);
    }

    // ======== 首帧恢复：Awake 先恢复"世界进度表 + 已拾取清单" ========
    // Awake 一定早于所有物体的 Start：
    //  - 各世界系统的 Start 自查能读到正确的 WorldState；
    //  - PickupItem.Start 的"防复活销毁"能读到正确的已拾取清单。
    private void Awake()
    {
        if (pendingData == null) return;

        WorldState.Restore(pendingData.worldState);

        // 已拾取清单：读档"覆盖"（不是合并）——精确回到存档那一刻
        collectedItems = pendingData.collectedItems != null
            ? new List<string>(pendingData.collectedItems)
            : new List<string>();
    }

    // ======== 存档 ========

    /// <summary> 把当前游戏状态打包存进本地（单存档位，覆盖旧的）。fromPoint = 触发存档的存档点 </summary>
    public void SaveGame(SavePoint fromPoint = null)
    {
        SaveData data = new SaveData();
        data.sceneName = SceneManager.GetActiveScene().name;

        PixelGridMovement player = FindObjectOfType<PixelGridMovement>();

        // 1. 玩家位置 = 存档点坐标（读档回到存档那一刻的存档点）；没有存档点时兜底用玩家站位
        if (fromPoint != null)
        {
            data.savePointId = fromPoint.savePointId;
            data.playerX = fromPoint.transform.position.x;
            data.playerY = fromPoint.transform.position.y;
            if (string.IsNullOrEmpty(fromPoint.savePointId))
                Debug.LogWarning("[存档] 存档点 '" + fromPoint.name + "' 的 savePointId 是空的！读档会退回用玩家站位。建议给它填个唯一 ID（如 Save_大厅）。");
        }
        else if (player != null)
        {
            Vector2 pos = player.GetCurrentGridPosition();
            data.playerX = pos.x;
            data.playerY = pos.y;
        }

        // 2. 血量
        if (player != null)
        {
            HealthSystem health = player.GetComponent<HealthSystem>();
            if (health != null)
            {
                data.currentHealth = health.currentHealth;
                data.maxHealth = health.maxHealth;
            }
        }

        // 3. 背包 + 弹药（Inventory 应该和玩家同物体）
        Inventory inv = GetComponent<Inventory>();
        if (inv == null) inv = player != null ? player.GetComponent<Inventory>() : null;
        if (inv != null)
        {
            data.equippedIndex = inv.equippedIndex;
            data.reservePistol = inv.reservePistol;
            data.reserveShotgun = inv.reserveShotgun;
            data.reserveEagle = inv.reserveEagle;

            data.items = new List<SaveItemData>();
            foreach (InventoryItem item in inv.items)
                data.items.Add(ConvertToSaveItem(item));
        }

        // 4. 已拾取物品清单（防复活）
        data.collectedItems = new List<string>(collectedItems);

        // 5. 世界进度表（门/机关/宝石/倒计时/怪死亡……整张表打包）
        data.worldState = WorldState.Snapshot();

        // 6. 序列化成 JSON 字符串，写成 UTF-8 文件。
        // 注意：不用 PlayerPrefs——它在 Windows 上存注册表，中文会损坏！
        string json = JsonUtility.ToJson(data);
        File.WriteAllText(SaveFilePath, json, Encoding.UTF8);

        Debug.Log("[存档] 已保存到 " + data.sceneName
            + " 存档点[" + (string.IsNullOrEmpty(data.savePointId) ? "无" : data.savePointId) + "]"
            + " (" + (data.items != null ? data.items.Count : 0) + " 件物品, "
            + (data.worldState != null ? data.worldState.Count : 0) + " 条世界进度)");
    }

    /// <summary> 把背包物品转成"可存文字"的版本（按类型只复制该类型的字段，修"字段污染"） </summary>
    private SaveItemData ConvertToSaveItem(InventoryItem item)
    {
        SaveItemData s = new SaveItemData();
        s.itemID = item.itemID;
        s.itemName = item.itemName;
        s.itemType = (int)item.itemType;
        s.description = item.description;

        // 只存"该类型"的字段：枪存枪的、草存草的，其它一律不写（避免把 herbType 之类的默认值
        // 写进钥匙/宝石里，造成字段污染）
        switch (item.itemType)
        {
            case ItemType.Gun:
                if (item.gunData != null) s.gunName = item.gunData.gunName;
                s.currentAmmo = item.currentAmmo;
                s.magSize = item.magSize;
                s.currentDurability = item.currentDurability;
                s.maxDurability = item.maxDurability;
                break;
            case ItemType.Herb:
                s.herbType = (int)item.herbType;
                s.healAmount = item.healAmount;
                break;
            default:
                break; // Key / Gem / Manual / Ammo：无附加字段
        }
        return s;
    }

    // ======== 读档 ========

    /// <summary>
    /// 读取存档：加载存档时的场景，等场景加载完再恢复所有数据。
    /// 返回 true 表示读档成功，false 表示没有存档。
    /// </summary>
    public bool LoadGame()
    {
        // 从 UTF-8 文件读存档（中文安全）
        if (!File.Exists(SaveFilePath))
        {
            Debug.Log("[读档] 没有找到存档文件：" + SaveFilePath);
            return false;
        }
        string json = File.ReadAllText(SaveFilePath, Encoding.UTF8);
        if (string.IsNullOrEmpty(json))
        {
            Debug.Log("[读档] 存档文件是空的！");
            return false;
        }

        // 恢复时间流速（防止之前背包暂停/死亡界面等把 timeScale 改了，读档后世界卡住）
        Time.timeScale = 1f;

        pendingData = JsonUtility.FromJson<SaveData>(json);
        if (pendingData == null) return false;

        // 直接加载存档时的场景。
        // 场景加载完成后，新场景里的 SaveSystem 会在 Awake（世界进度表）+ Update 第一帧
        // （玩家血量背包）里自动恢复数据——不再用场景加载事件，避免"调用已销毁实例"的坑。
        SceneManager.LoadScene(pendingData.sceneName);
        return true;
    }

    /// <summary>
    /// 没有存档时"回关卡开头"：清空所有进度（世界表 + 已拾取清单 + 背包静态快照），加载起始场景。
    /// </summary>
    public void RestartFromBeginning()
    {
        Time.timeScale = 1f;
        WorldState.Clear();
        collectedItems.Clear();
        Inventory.ResetStaticState();
        pendingData = null;

        if (!string.IsNullOrEmpty(restartSceneName))
        {
            Debug.Log("[重开] 没有存档，从场景 '" + restartSceneName + "' 重新开始");
            SceneManager.LoadScene(restartSceneName);
        }
        else
        {
            int idx = SceneManager.sceneCountInBuildSettings > 0 ? 0 : 0;
            Debug.Log("[重开] 没有存档，从 Build 第 0 个场景重新开始");
            SceneManager.LoadScene(idx);
        }
    }

    /// <summary>
    /// 每帧检查一次（第一帧恢复）：恢复 玩家位置/血量/背包/备用弹药。
    /// 用 Update 而不是 Start，是要等场景里所有物体的 Start 都跑完
    /// （比如 HealthSystem.Start 会把血量重置为满血，Orb 覆盖要在它之后）。
    /// </summary>
    private void Update()
    {
        if (restoreChecked) return;
        restoreChecked = true;

        if (pendingData != null)
        {
            SaveData data = pendingData; // 先取局部引用：RestoreFromSave 里可能又触发 LoadScene
            pendingData = null;
            RestoreFromSave(data);
        }
    }

    /// <summary> 恢复玩家位置/血量/背包/备用弹药（WorldState 和已拾取清单已在 Awake 恢复） </summary>
    private void RestoreFromSave(SaveData data)
    {
        if (data == null) return;

        // 1. 找到玩家
        PixelGridMovement player = FindObjectOfType<PixelGridMovement>();
        if (player == null)
        {
            Debug.LogError("[读档] 场景里没有找到玩家！");
            return;
        }

        // 2. 玩家位置 = 存档点坐标（按 savePointId 在场景里找存档点）；找不到退回旧档存的坐标
        bool placed = false;
        if (!string.IsNullOrEmpty(data.savePointId))
        {
            SavePoint[] points = FindObjectsOfType<SavePoint>();
            foreach (SavePoint sp in points)
            {
                if (sp != null && sp.savePointId == data.savePointId)
                {
                    player.TeleportTo(sp.transform.position);
                    placed = true;
                    break;
                }
            }
            if (!placed)
                Debug.LogWarning("[读档] 场景里没找到 ID 为 '" + data.savePointId + "' 的存档点，退回用存档里的坐标");
        }
        if (!placed)
            player.TeleportTo(new Vector2(data.playerX, data.playerY));

        // 3. 恢复血量（此时 HealthSystem.Start 已经跑完，直接覆盖当前值）
        HealthSystem health = player.GetComponent<HealthSystem>();
        if (health != null)
            health.currentHealth = Mathf.Clamp(data.currentHealth, 1, data.maxHealth);

        // 4. 恢复背包（重建物品列表，gunData 按名字找回）
        Inventory inv = player.GetComponent<Inventory>();
        if (inv == null) inv = GetComponent<Inventory>();
        if (inv != null)
        {
            inv.items.Clear();
            if (data.items != null)
            {
                foreach (SaveItemData s in data.items)
                {
                    InventoryItem it = ConvertFromSaveItem(s);
                    if (it != null) inv.items.Add(it);
                }
            }
            inv.equippedIndex = data.equippedIndex;
            inv.reservePistol = data.reservePistol;
            inv.reserveShotgun = data.reserveShotgun;
            inv.reserveEagle = data.reserveEagle;
            inv.onChanged?.Invoke(); // 通知 UI 刷新
            inv.RefreshSaveState();  // 同步背包的跨场景静态快照，防止以后切场景时被旧快照覆盖
        }

        // 5. 已拾取清单：已在 Awake「覆盖」恢复（不是合并），这里不再处理。

        Debug.Log("[读档] 恢复完成！场景 " + data.sceneName + "，存档点[" + data.savePointId + "]，血量 " + data.currentHealth);
    }

    /// <summary> 把存档物品文字还原成背包物品（按类型还原字段 + 按 gunName 找回 GunData 引用 + 名字兜底） </summary>
    private InventoryItem ConvertFromSaveItem(SaveItemData s)
    {
        if (s == null) return null;

        InventoryItem item = new InventoryItem();
        item.itemID = s.itemID;
        item.itemType = (ItemType)s.itemType;

        // 名字兜底（修"空名字"bug）：空 → itemID → "未知道具"
        item.itemName = !string.IsNullOrEmpty(s.itemName) ? s.itemName
                       : (!string.IsNullOrEmpty(s.itemID) ? s.itemID : "未知道具");

        item.description = s.description;

        Inventory inv = GetComponent<Inventory>();
        // 从物品模板表恢复图标/描述（JSON 存不了 Sprite，靠 itemID 在模板表里找回）
        Inventory.ItemTemplate template = inv != null ? inv.GetTemplate(s.itemID) : null;
        if (template != null)
        {
            item.icon = template.icon;
            if (string.IsNullOrEmpty(item.description))
                item.description = template.description;
        }

        // 按类型还原：只读该类型该有的字段（旧档里被污染的其他字段直接忽略）
        switch (item.itemType)
        {
            case ItemType.Gun:
                if (!string.IsNullOrEmpty(s.gunName) && inv != null && inv.allGunDatas != null)
                {
                    foreach (GunData g in inv.allGunDatas)
                    {
                        if (g != null && g.gunName == s.gunName) { item.gunData = g; break; }
                    }
                }
                item.currentAmmo = s.currentAmmo;
                item.magSize = s.magSize;
                item.currentDurability = s.currentDurability;
                item.maxDurability = s.maxDurability;
                if (item.gunData == null)
                    Debug.LogWarning("[读档] 枪 '" + item.itemName + "' 的 gunName='" + s.gunName
                        + "' 在 Inventory.allGunDatas 里没找到（旧档兼容：先还原成无枪数据的条目，请把该 GunData 拖进 allGunDatas）");
                break;
            case ItemType.Herb:
                item.herbType = (HerbType)s.herbType;
                item.healAmount = s.healAmount > 0 ? s.healAmount : 1;
                break;
            default:
                break; // Key / Gem / Manual / Ammo：无附加字段
        }

        return item;
    }
}
