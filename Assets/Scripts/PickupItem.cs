using UnityEngine;

public enum ItemType { Gun, Key, Gem, Manual, Ammo, Herb }

/// <summary> 草药类型：绿草可直接用，红草要混合，红绿草（Mixed）是合成产物 </summary>
public enum HerbType { None, Green, Red, Mixed }

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

    // ======== 草药系统：运行时状态 ========
    public HerbType herbType;     // 草药类型（绿/红/混合）
    public int healAmount;        // 使用后恢复多少生命（从 PickupItem 复制过来）
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

    [Header("高亮引导")]
    [Tooltip("勾选后 = 关键物品：玩家靠近时会金色闪烁提示（需在物体上挂 ItemHighlight 组件）")]
    public bool isKeyItem = false;

    [Header("如果是枪")]
    public GunData gunData;

    [Header("如果是弹药包")]
    [Tooltip("这个弹药包属于哪种弹药（每把枪只能用自己的弹药）")]
    public AmmoType ammoType = AmmoType.Pistol;

    [Tooltip("拾取后加入备用弹药池的数量")]
    public int ammoAmount = 10;

    [Header("如果是草药")]
    [Tooltip("草药类型：绿草可直接使用，红草需要和绿草混合")]
    public HerbType herbType = HerbType.Green;

    [Tooltip("使用后恢复多少生命（Inspector 可调；红草不能直接用）")]
    public int healAmount = 1;

    private void Start()
    {
        // 防复活检查：这个物品以前被拾取过（在已拾取清单里）→ 场景重载后销毁自己
        // 这样读档/切场景回来，捡过的物品不会重新出现（防止反复刷物品）。
        // itemID 为空时用物体名兜底判断（没填 ID 的弹药包也能防住）
        if (SaveSystem.IsCollected(itemID, gameObject.name))
            Destroy(gameObject);
    }
}
