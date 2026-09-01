using System.Collections.Generic;
using UnityEngine;

public class Inventory : MonoBehaviour
{
    /// <summary> 物品模板：itemID 对应的图标/描述（读档恢复图标用） </summary>
    [System.Serializable]
    public class ItemTemplate
    {
        public string itemID;       // 和场景里 PickupItem 的 itemID 一致
        public Sprite icon;         // 这个物品的图标
        [TextArea] public string description; // 描述（可选，读档时补）
    }

    private static List<InventoryItem> savedItems = null;
    private static int savedEquipped = -1;

    [Header("背包设置")]
    public int gridColumns = 4;

    [Header("枪数据注册表")]
    [Tooltip("把项目里所有枪的 GunData 拖进来（存档读档时按名字找回枪的引用用）")]
    public GunData[] allGunDatas;

    [Header("物品模板表")]
    [Tooltip("配置每个物品的图标（读档后图标会从 JSON 丢失，靠这里按 itemID 找回）。钥匙/宝石/草药等都配上")]
    public ItemTemplate[] itemTemplates;

    public List<InventoryItem> items = new List<InventoryItem>();
    public int equippedIndex = -1;

    public System.Action onChanged;

    private void Awake()
    {
        InitDefaultRecipes(); // 预填默认合成配方（绿草+红草=红绿草）

        if (savedItems != null)
        {
            items = savedItems;
            equippedIndex = savedEquipped;
            savedItems = null;
            savedEquipped = -1;
        }
    }

    /// <summary>
    /// 清空跨场景静态快照（死亡重开/全新开始时调用）。
    /// 防止"没存档却因为旧快照继承背包物品"的问题。
    /// </summary>
    public static void ResetStaticState()
    {
        savedItems = null;
        savedEquipped = -1;
    }

    private void SaveState()
    {
        savedItems = new List<InventoryItem>(items);
        savedEquipped = equippedIndex;
    }

    /// <summary>
    /// 手动刷新跨场景静态快照（读档恢复背包后调用，防止以后切场景被旧快照覆盖）
    /// </summary>
    public void RefreshSaveState()
    {
        SaveState();
    }

    public void AddItem(PickupItem pickup)
    {
        // 弹药包不占背包格子，直接加入"备用弹药池"（按弹药类型分池子）
        if (pickup.itemType == ItemType.Ammo)
        {
            AddReserveAmmo(pickup.ammoType, pickup.ammoAmount);
            return;
        }

        InventoryItem item = new InventoryItem
        {
            itemID = pickup.itemID,
            itemName = pickup.itemName,
            itemType = pickup.itemType,
            icon = pickup.icon,
            description = pickup.description,
            gunData = pickup.gunData,
            herbType = pickup.herbType,
            healAmount = pickup.healAmount
        };

        // 如果是武器：把 GunData 的上限复制到"运行时"字段，拾取时弹夹是满的
        if (item.gunData != null)
        {
            item.magSize = item.gunData.magSize;
            item.currentAmmo = item.magSize;
            item.maxDurability = item.gunData.maxDurability;
            item.currentDurability = item.maxDurability;
            Debug.Log("[武器] 拾取 " + item.itemName + "，弹夹 " + item.currentAmmo + "/" + item.magSize
                + "，耐久 " + item.currentDurability + "/" + item.maxDurability);
        }

        items.Add(item);
        if (item.itemType == ItemType.Gun)
            equippedIndex = items.Count - 1;
        SaveState();
        onChanged?.Invoke();
    }

    // ======== 备用弹药池（拾取弹药包后存入这里，换弹时转入弹夹） ========
    // 每种弹药一个独立池子：手枪、霰弹、沙漠之鹰的弹药互不通用

    public int reservePistol = 0;   // 手枪备用弹药
    public int reserveShotgun = 0;  // 霰弹枪备用弹药
    public int reserveEagle = 0;    // 沙漠之鹰备用弹药

    /// <summary> 查询某种弹药的备用数量 </summary>
    public int GetReserveAmmo(AmmoType type)
    {
        switch (type)
        {
            case AmmoType.Pistol:  return reservePistol;
            case AmmoType.Shotgun: return reserveShotgun;
            case AmmoType.Eagle:   return reserveEagle;
            default:               return 0;
        }
    }

    /// <summary> 增加（或扣除，传负数）某种备用弹药。数量不会低于 0 </summary>
    public void AddReserveAmmo(AmmoType type, int amount)
    {
        switch (type)
        {
            case AmmoType.Pistol:  reservePistol  = Mathf.Max(0, reservePistol  + amount); break;
            case AmmoType.Shotgun: reserveShotgun = Mathf.Max(0, reserveShotgun + amount); break;
            case AmmoType.Eagle:   reserveEagle   = Mathf.Max(0, reserveEagle   + amount); break;
        }
        Debug.Log("[弹药] " + type + " 备用弹药 → " + GetReserveAmmo(type));
        onChanged?.Invoke();
    }

    /// <summary> 能不能换弹？条件：装备了枪 + 弹夹没满 + 有备用弹药 </summary>
    public bool CanReload()
    {
        InventoryItem equipped = GetEquippedItem();
        if (equipped == null || equipped.gunData == null) return false;
        if (equipped.currentAmmo >= equipped.magSize) return false; // 弹夹已满，不用换
        return GetReserveAmmo(equipped.gunData.ammoType) > 0;       // 有备用弹药才能换
    }

    /// <summary> 执行换弹：从备用弹药池补满弹夹（换弹动画/音效由 Gun.cs 负责） </summary>
    public bool CompleteReload()
    {
        InventoryItem equipped = GetEquippedItem();
        if (equipped == null || equipped.gunData == null) return false;

        AmmoType type = equipped.gunData.ammoType;
        int need = equipped.magSize - equipped.currentAmmo;   // 弹夹还差几发
        int take = Mathf.Min(need, GetReserveAmmo(type));     // 能从备用池取多少
        if (take <= 0) return false;                          // 没得换

        equipped.currentAmmo += take;                         // 弹夹补上
        AddReserveAmmo(type, -take);                          // 备用池扣除
        Debug.Log("[换弹] " + equipped.itemName + " → " + equipped.currentAmmo + "/" + equipped.magSize
            + "（备用剩 " + GetReserveAmmo(type) + "）");
        onChanged?.Invoke();
        return true;
    }

    /// <summary> 获取当前装备的物品条目（可能是枪也可能是别的） </summary>
    public InventoryItem GetEquippedItem()
    {
        if (equippedIndex >= 0 && equippedIndex < items.Count)
            return items[equippedIndex];
        return null;
    }

    /// <summary> 消耗一发子弹。返回 false 表示弹尽，不能开枪 </summary>
    public bool ConsumeAmmo()
    {
        InventoryItem equipped = GetEquippedItem();
        if (equipped == null || equipped.gunData == null) return false;
        if (equipped.currentAmmo <= 0) return false;
        equipped.currentAmmo--;
        onChanged?.Invoke();
        return true;
    }

    /// <summary> 近战武器消耗 1 点耐久。返回 false 表示耐久耗尽，不能攻击。
    /// maxDurability <= 0 表示无限耐久，永远返回 true（不消耗） </summary>
    public bool ConsumeDurability()
    {
        InventoryItem equipped = GetEquippedItem();
        if (equipped == null || equipped.gunData == null) return false;
        if (equipped.maxDurability <= 0) return true;      // 无限耐久，随便打
        if (equipped.currentDurability <= 0) return false; // 耐久耗尽
        equipped.currentDurability--;
        onChanged?.Invoke();
        return true;
    }

    public void EquipAt(int index)
    {
        if (index >= 0 && index < items.Count && items[index].itemType == ItemType.Gun)
        {
            equippedIndex = index;
            SaveState();
            onChanged?.Invoke();
        }
    }

    public GunData GetEquippedGun()
    {
        if (equippedIndex >= 0 && equippedIndex < items.Count)
            return items[equippedIndex].gunData;
        return null;
    }

    public InventoryItem GetItemAt(int index)
    {
        if (index >= 0 && index < items.Count)
            return items[index];
        return null;
    }

    /// <summary> 按 itemID 查找物品模板（读档恢复图标/描述用），找不到返回 null </summary>
    public ItemTemplate GetTemplate(string itemID)
    {
        if (itemTemplates == null || string.IsNullOrEmpty(itemID)) return null;
        foreach (ItemTemplate t in itemTemplates)
        {
            if (t != null && t.itemID == itemID) return t;
        }
        return null;
    }

    // ======== 物品使用（草药回血） ========

    /// <summary>
    /// 使用背包里指定位置的物品。
    /// 返回提示信息（UI 显示用），null 表示"不需要提示"。
    /// </summary>
    public string UseItem(int index)
    {
        InventoryItem item = GetItemAt(index);
        if (item == null) return null;

        // 只有草药类走使用逻辑
        if (item.itemType != ItemType.Herb) return null;

        // 红草不能直接用，引导去合成
        if (item.herbType == HerbType.Red)
            return "红草不能直接使用，需要和绿草混合！";

        // 绿草 / 红绿草：回血
        HealthSystem health = GetComponent<HealthSystem>();
        if (health == null) return "没有血量系统，无法使用！";
        if (health.currentHealth >= health.maxHealth)
            return "生命值已满，不需要使用！";

        health.Heal(item.healAmount);
        RemoveItemAt(index); // 用掉 1 个
        return "使用 " + item.itemName + "，恢复 " + item.healAmount + " 点生命！";
    }

    /// <summary> 移除指定位置的物品（使用/合成消耗时调用）。会修正装备索引 </summary>
    public void RemoveItemAt(int index)
    {
        if (index < 0 || index >= items.Count) return;
        items.RemoveAt(index);

        // 修正装备索引：如果删的是装备中的枪
        if (equippedIndex >= items.Count) equippedIndex = -1;
        else if (equippedIndex > index) equippedIndex--;

        SaveState();
        onChanged?.Invoke();
    }

    // ======== 合成系统（雏形：目前只有 绿草+红草=红绿草） ========

    /// <summary> 配方数据类：材料A + 材料B → 产物 </summary>
    [System.Serializable]
    public class CombineRecipe
    {
        [Tooltip("配方名字（合成面板显示）")]
        public string recipeName = "绿草 + 红草";

        [Header("材料（按 itemID 匹配，场景里的草药 PickupItem 填好 itemID）")]
        public string materialA_ID = "GreenHerb";
        public string materialB_ID = "RedHerb";

        [Header("产物")]
        public string resultID = "MixedHerb";
        public string resultName = "红绿草";
        public Sprite resultIcon;
        [TextArea] public string resultDescription = "混合了两种草药的药剂，恢复效果更强。";
        public HerbType resultHerbType = HerbType.Mixed;
        public int resultHealAmount = 3;
    }

    [Header("合成配方表（Inspector 可加新配方）")]
    public List<CombineRecipe> recipes = new List<CombineRecipe>();

    private void InitDefaultRecipes()
    {
        // 预填默认配方：绿草 + 红草 → 红绿草（Inspector 里也能改数值/加新配方）
        if (recipes.Count == 0)
        {
            recipes.Add(new CombineRecipe
            {
                recipeName = "绿草 + 红草",
                materialA_ID = "GreenHerb",
                materialB_ID = "RedHerb",
                resultID = "MixedHerb",
                resultName = "红绿草",
                resultHerbType = HerbType.Mixed,
                resultHealAmount = 3,
                resultDescription = "混合了两种草药的药剂，恢复效果更强。"
            });
        }
    }

    /// <summary> 返回当前背包材料足够的所有配方（合成面板只显示这些） </summary>
    public List<CombineRecipe> GetAvailableRecipes()
    {
        List<CombineRecipe> available = new List<CombineRecipe>();
        foreach (CombineRecipe r in recipes)
        {
            if (HasItem(r.materialA_ID) && HasItem(r.materialB_ID))
                available.Add(r);
        }
        return available;
    }

    /// <summary> 背包里有没有指定 itemID 的物品 </summary>
    public bool HasItem(string itemID)
    {
        if (string.IsNullOrEmpty(itemID)) return false;
        foreach (InventoryItem it in items)
        {
            if (it.itemID == itemID) return true;
        }
        return false;
    }

    /// <summary> 执行合成：消耗材料，把产物放进背包。返回 true 表示合成成功 </summary>
    public bool TryCombine(CombineRecipe recipe)
    {
        if (recipe == null) return false;

        // 找两种材料在背包里的位置
        int idxA = FindItemIndex(recipe.materialA_ID);
        int idxB = FindItemIndex(recipe.materialB_ID);
        if (idxA < 0 || idxB < 0) return false; // 材料不够

        // 消耗材料（注意先删索引大的，避免删除后索引错位）
        if (idxB > idxA)
        {
            RemoveItemAt(idxB);
            RemoveItemAt(idxA);
        }
        else
        {
            RemoveItemAt(idxA);
            RemoveItemAt(idxB);
        }

        // 生成产物放进背包
        InventoryItem result = new InventoryItem
        {
            itemID = recipe.resultID,
            itemName = recipe.resultName,
            itemType = ItemType.Herb,   // 雏形阶段产物都是草药类
            icon = recipe.resultIcon,
            description = recipe.resultDescription,
            herbType = recipe.resultHerbType,
            healAmount = recipe.resultHealAmount
        };
        items.Add(result);

        Debug.Log("[合成] " + recipe.recipeName + " → 获得 " + recipe.resultName + "！");
        SaveState();
        onChanged?.Invoke();
        return true;
    }

    /// <summary> 在背包里找指定 itemID 的索引，找不到返回 -1 </summary>
    private int FindItemIndex(string itemID)
    {
        for (int i = 0; i < items.Count; i++)
        {
            if (items[i].itemID == itemID) return i;
        }
        return -1;
    }
}
