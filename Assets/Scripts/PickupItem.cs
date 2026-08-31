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
    // 注意！这些字段存在"背包条目"上，而不是 GunData 资产上。
    // 因为弹药/耐久是每一把武器实例独立的、会变化的数；
    // 如果存进 ScriptableObject，所有同型号武器会共享数值，
    // 而且退出游戏后数值会被写回磁盘资产，把武器数据搞坏。
    public int currentAmmo;       // 当前弹夹里的子弹数（远程武器用）
    public int magSize;           // 弹夹容量：最多能装几发（从 GunData 复制过来）
    public int currentDurability; // 当前耐久（近战武器用）
    public int maxDurability;     // 耐久上限（从 GunData 复制过来，0 = 无限耐久）
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
    [Tooltip("这个弹药包属于哪种弹药（每把枪只能用自己的弹药）")]
    public AmmoType ammoType = AmmoType.Pistol;

    [Tooltip("拾取后加入备用弹药池的数量")]
    public int ammoAmount = 10;
}
