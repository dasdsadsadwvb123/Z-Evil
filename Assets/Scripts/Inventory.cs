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
        // 弹药包不占背包格子，直接给装备中的枪补弹
        if (pickup.itemType == ItemType.Ammo)
        {
            AddAmmoToEquippedGun(pickup.ammoAmount);
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

        // 如果是枪：把 GunData 的满弹量复制到"运行时"字段，初始为满弹
        if (item.gunData != null)
        {
            item.maxAmmo = item.gunData.maxAmmo;
            item.currentAmmo = item.maxAmmo;
            Debug.Log("[弹药] 拾取 " + item.itemName + "，满弹 " + item.currentAmmo + "/" + item.maxAmmo + " 发");
        }

        items.Add(item);
        if (item.itemType == ItemType.Gun)
            equippedIndex = items.Count - 1;
        SaveState();
        onChanged?.Invoke();
    }

    /// <summary>
    /// 给当前装备的枪补充弹药（弹药包拾取时调用）。
    /// 返回 true 表示成功补弹，false 表示没有可补弹的枪。
    /// </summary>
    public bool AddAmmoToEquippedGun(int amount)
    {
        InventoryItem equipped = GetEquippedItem();
        if (equipped == null || equipped.gunData == null)
        {
            Debug.Log("[弹药] 没有装备枪械，无法补充弹药！");
            return false;
        }

        int before = equipped.currentAmmo;
        // 补弹但不超过上限：比如当前 3 发，捡到 10 发，上限 15 → 变成 13 发
        equipped.currentAmmo = Mathf.Min(equipped.maxAmmo, equipped.currentAmmo + amount);
        int gained = equipped.currentAmmo - before;
        Debug.Log("[弹药] " + equipped.itemName + " 补充 " + gained + " 发 → " + equipped.currentAmmo + "/" + equipped.maxAmmo);
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
