using UnityEngine;
using UnityEngine.SceneManagement;
using System.Collections.Generic;
using System.IO;
using System.Text;

/// <summary>
/// 存档系统：把 场景/玩家位置/血量/背包/弹药 打包成 JSON 字符串，存进 PlayerPrefs。
/// 挂到玩家（Player）物体上（需要和 Inventory、HealthSystem、PixelGridMovement 同物体）。
///
/// 用法：
/// - 调用 SaveGame() 存档（由存档点 SavePoint 触发）
/// - 调用 LoadGame() 读档（会加载存档时所在的场景，然后恢复所有数据）
///
/// 关键知识点：JSON 只能存"文字"，存不了资产引用（GunData/Sprite 等）。
/// 所以存档时记枪的名字（gunName），读档时按名字从 Inventory.allGunDatas 找回引用。
/// </summary>
public class SaveSystem : MonoBehaviour
{
    [Tooltip("存档文件名（存在游戏数据目录里）")]
    public string saveFileName = "ZEvil_Save.json";

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
        public float playerX;             // 玩家网格坐标
        public float playerY;
        public int currentHealth;         // 当前血量
        public int maxHealth;             // 最大血量
        public int equippedIndex;         // 装备的武器索引
        public int reservePistol;         // 备用弹药（三种枪）
        public int reserveShotgun;
        public int reserveEagle;
        public List<SaveItemData> items;  // 背包物品（转换成可存文字的版本）
        public List<string> collectedItems; // 已拾取物品的 ID（场景重载后这些物品不复活）
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
        // 注意：icon 图标存不了（JSON 不能存资产引用），读档后显示名字不显示图标
    }

    // 读档时暂存数据。用 static（静态）跨场景传递：
    // 场景重载会销毁本脚本实例，但 static 字段属于"类"不属于"实例"，销毁了也还在。
    // 新场景的实例会在第一帧检查它并恢复数据。
    private static SaveData pendingData = null;
    private bool restoreChecked = false; // 保证恢复逻辑只跑一次

    // ======== 已拾取物品清单（防复活/防刷物品） ========
    // static：跨场景保留，拾取过的物品在场景重载后检查这里并销毁自己
    private static List<string> collectedItems = new List<string>();

    /// <summary>
    /// 生成物品的"防复活钥匙"：优先用 itemID；没填 ID 就用物体名兜底（带 OBJ_ 前缀）。
    /// 兜底方案能防止"弹药包没填 ID → 读档后无限刷子弹"的问题。
    /// </summary>
    private static string GetPickupKey(string itemID, string fallbackName)
    {
        if (!string.IsNullOrEmpty(itemID)) return itemID;
        return "OBJ_" + fallbackName;
    }

    /// <summary> 记录一个物品已被拾取（去重）。fallbackName 是物品的物体名（itemID 为空时兜底用） </summary>
    public static void CollectItem(string itemID, string fallbackName)
    {
        string key = GetPickupKey(itemID, fallbackName);
        if (!collectedItems.Contains(key))
            collectedItems.Add(key);

        // 没填 itemID 时提醒玩家（最佳实践：每个物品填唯一 ID）
        if (string.IsNullOrEmpty(itemID))
            Debug.LogWarning("[存档] 物品 '" + fallbackName + "' 没填 itemID，用物体名兜底防复活。建议在 Inspector 里给它填一个唯一 ID！");
    }

    /// <summary> 这个物品是否已被拾取过 </summary>
    public static bool IsCollected(string itemID, string fallbackName)
    {
        return collectedItems.Contains(GetPickupKey(itemID, fallbackName));
    }

    // ======== 存档 ========

    /// <summary> 把当前游戏状态打包存进本地（单存档位，覆盖旧的） </summary>
    public void SaveGame()
    {
        SaveData data = new SaveData();
        data.sceneName = SceneManager.GetActiveScene().name;

        // 1. 玩家位置 + 血量
        PixelGridMovement player = FindObjectOfType<PixelGridMovement>();
        if (player != null)
        {
            Vector2 pos = player.GetCurrentGridPosition();
            data.playerX = pos.x;
            data.playerY = pos.y;

            HealthSystem health = player.GetComponent<HealthSystem>();
            if (health != null)
            {
                data.currentHealth = health.currentHealth;
                data.maxHealth = health.maxHealth;
            }
        }

        // 2. 背包 + 弹药（Inventory 应该和玩家同物体）
        Inventory inv = GetComponent<Inventory>();
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

        // 3. 已拾取物品清单（防复活）
        data.collectedItems = new List<string>(collectedItems);

        // 4. 序列化成 JSON 字符串，写成 UTF-8 文件。
        // 注意：不用 PlayerPrefs——它在 Windows 上存注册表，中文会损坏！
        // 文件存储（UTF-8 编码）对中文物品名安全。
        string json = JsonUtility.ToJson(data);
        File.WriteAllText(SaveFilePath, json, Encoding.UTF8);

        Debug.Log("[存档] 已保存到 " + data.sceneName + " (" + (data.items != null ? data.items.Count : 0) + " 件物品)");
    }

    /// <summary> 把背包物品转成"可存文字"的版本（gunData 引用 → 枪名字符串） </summary>
    private SaveItemData ConvertToSaveItem(InventoryItem item)
    {
        SaveItemData s = new SaveItemData();
        s.itemID = item.itemID;
        s.itemName = item.itemName;
        s.itemType = (int)item.itemType;
        s.description = item.description;
        if (item.gunData != null) s.gunName = item.gunData.gunName;
        s.currentAmmo = item.currentAmmo;
        s.magSize = item.magSize;
        s.currentDurability = item.currentDurability;
        s.maxDurability = item.maxDurability;
        s.herbType = (int)item.herbType;
        s.healAmount = item.healAmount;
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
        // 场景加载完成后，新场景里的 SaveSystem 会在第一帧（Update）里自动检查
        // static pendingData 并恢复数据——不再用场景加载事件，避免"调用已销毁实例"的坑。
        SceneManager.LoadScene(pendingData.sceneName);
        return true;
    }

    /// <summary>
    /// 每帧检查一次（第一帧恢复）：
    /// 用 Update 而不是 Start，是因为要等场景里所有物体的 Start 都跑完
    /// （比如 HealthSystem.Start 会把血量重置为满血，我们得在它之后覆盖）。
    /// </summary>
    private void Update()
    {
        if (restoreChecked) return;
        restoreChecked = true;

        if (pendingData != null)
        {
            RestoreFromSave(pendingData);
            pendingData = null;
        }
    }

    /// <summary> 恢复玩家位置/血量/背包/备用弹药 </summary>
    private void RestoreFromSave(SaveData data)
    {
        if (data == null) return;

        // 1. 找到玩家，恢复位置（TeleportTo 会正确落回网格）
        PixelGridMovement player = FindObjectOfType<PixelGridMovement>();
        if (player == null)
        {
            Debug.LogError("[读档] 场景里没有找到玩家！");
            return;
        }
        player.TeleportTo(new Vector2(data.playerX, data.playerY));

        // 2. 恢复血量（此时 HealthSystem.Start 已经跑完，直接覆盖当前值）
        HealthSystem health = player.GetComponent<HealthSystem>();
        if (health != null)
            health.currentHealth = Mathf.Clamp(data.currentHealth, 1, data.maxHealth);

        // 3. 恢复背包（重建物品列表，gunData 按名字找回）
        Inventory inv = player.GetComponent<Inventory>();
        if (inv != null)
        {
            inv.items.Clear();
            if (data.items != null)
            {
                foreach (SaveItemData s in data.items)
                    inv.items.Add(ConvertFromSaveItem(s));
            }
            inv.equippedIndex = data.equippedIndex;
            inv.reservePistol = data.reservePistol;
            inv.reserveShotgun = data.reserveShotgun;
            inv.reserveEagle = data.reserveEagle;
            inv.onChanged?.Invoke(); // 通知 UI 刷新
            inv.RefreshSaveState();  // 同步背包的跨场景静态快照，防止以后切场景时被旧快照覆盖
        }

        // 4. 恢复"已拾取物品清单"：合并（存档记录的 + 当前仍保留的都在）
        // 这样读档后：存档前捡过的物品不复活，存档后捡的也不复活（永久消失，防刷物品）
        if (data.collectedItems != null)
        {
            foreach (string id in data.collectedItems)
                CollectItem(id, ""); // id 已是完整钥匙，fallbackName 传空不影响
        }

        Debug.Log("[读档] 恢复完成！位置 (" + data.playerX + "," + data.playerY + ") 血量 " + data.currentHealth);
    }

    /// <summary> 把存档物品文字还原成背包物品（按 gunName 找回 GunData 引用） </summary>
    private InventoryItem ConvertFromSaveItem(SaveItemData s)
    {
        InventoryItem item = new InventoryItem();
        item.itemID = s.itemID;
        item.itemName = s.itemName;
        item.itemType = (ItemType)s.itemType;
        item.description = s.description;
        item.currentAmmo = s.currentAmmo;
        item.magSize = s.magSize;
        item.currentDurability = s.currentDurability;
        item.maxDurability = s.maxDurability;
        item.herbType = (HerbType)s.herbType;
        item.healAmount = s.healAmount;

        // 从物品模板表恢复图标（JSON 存不了 Sprite，靠 itemID 在模板表里找回）
        Inventory inv = GetComponent<Inventory>();
        if (inv != null)
        {
            Inventory.ItemTemplate template = inv.GetTemplate(s.itemID);
            if (template != null)
            {
                item.icon = template.icon;
                // 描述丢了就补模板里的
                if (string.IsNullOrEmpty(item.description))
                    item.description = template.description;
            }

            // 按枪名找回 GunData 引用（从 Inventory 的注册表里找）
            if (!string.IsNullOrEmpty(s.gunName) && inv.allGunDatas != null)
            {
                foreach (GunData g in inv.allGunDatas)
                {
                    if (g != null && g.gunName == s.gunName)
                    {
                        item.gunData = g;
                        break;
                    }
                }
            }
        }

        return item;
    }
}
