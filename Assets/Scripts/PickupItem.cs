using UnityEngine;

public enum ItemType { Gun, Key, Gem, Manual }

[System.Serializable]
public class InventoryItem
{
    public string itemID;
    public string itemName;
    public ItemType itemType;
    public Sprite icon;
    public string description;
    public GunData gunData;
}

public class PickupItem : MonoBehaviour
{
    [Header("物品信息")]
    public string itemID;
    public string itemName;
    public ItemType itemType;
    public Sprite icon;
    [TextArea] public string description = "";
    public bool destroyOnPickup = true;

    [Header("如果是枪")]
    public GunData gunData;
}
