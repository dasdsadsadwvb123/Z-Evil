using UnityEngine;

public enum ItemType { Gun, Key, Gem, Manual, Ammo }

[System.Serializable]
public class InventoryItem
{
    public string itemID;
    public string itemName;
    public ItemType itemType;
    public Sprite icon;
    public string description;
    public GunData gunData;

    // ======== 弹药系统：运行时状态 ========
    // 注意！这两个字段存在"背包条目"上，而不是 GunData 资产上。
    // 因为弹药是每一把枪实例独立的、会变化的数；
    // 如果存进 ScriptableObject，所有同型号枪会共享弹药，
    // 而且退出游戏后数值会被写回磁盘资产，把枪数据搞坏。
    public int currentAmmo;  // 当前剩余子弹
    public int maxAmmo;      // 这把枪的弹药上限（从 GunData 复制过来）
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

    [Header("如果是弹药包")]
    [Tooltip("拾取后给装备的枪补充的子弹数量")]
    public int ammoAmount = 10;
}
