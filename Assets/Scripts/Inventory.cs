using System.Collections.Generic;
using UnityEngine;

public class Inventory : MonoBehaviour
{
    private static List<InventoryItem> savedItems = null;
    private static int savedEquipped = -1;

    [Header("背包设置")]
    public int gridColumns = 4;

    public List<InventoryItem> items = new List<InventoryItem>();
    public int equippedIndex = -1;

    public System.Action onChanged;

    private void Awake()
    {
        if (savedItems != null)
        {
            items = savedItems;
            equippedIndex = savedEquipped;
            savedItems = null;
            savedEquipped = -1;
        }
    }

    private void SaveState()
    {
        savedItems = new List<InventoryItem>(items);
        savedEquipped = equippedIndex;
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
            gunData = pickup.gunData
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
}
