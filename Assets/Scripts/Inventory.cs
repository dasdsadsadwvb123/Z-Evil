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
        InventoryItem item = new InventoryItem
        {
            itemID = pickup.itemID,
            itemName = pickup.itemName,
            itemType = pickup.itemType,
            icon = pickup.icon,
            description = pickup.description,
            gunData = pickup.gunData
        };
        items.Add(item);
        if (item.itemType == ItemType.Gun)
            equippedIndex = items.Count - 1;
        SaveState();
        onChanged?.Invoke();
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
