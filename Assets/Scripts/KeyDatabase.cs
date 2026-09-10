using UnityEngine;

/// <summary>
/// 钥匙总库（全场景唯一）：预置 8 个钥匙卡槽，小泽在这里填 8 把钥匙的"ID + 显示名"。
/// 传送门（Portal）勾上"需要钥匙"并选卡槽号 → 按传送门查这里拿 keyID 去背包核对、拿 keyName 显示提示。
///
/// 用法（小泽视角）：
/// 1. Hierarchy 建一个空物体，命名 KeyDatabase，挂本脚本（每个场景放一个即可；没放也能跑，Console 会警告）
/// 2. Inspector 里 8 个卡槽默认预填占位（Key1~Key8 / 钥匙 1~8），改成正式名：
///    - Key ID = 对应钥匙拾取物（PickupItem）的 Item ID，两边必须一模一样（区分大小写！）
///    - Key Name = 玩家看到的钥匙名（如"院长办公室钥匙"，缺钥匙提示和背包里显示的都是它）
/// 3. 钥匙拾取物：PickupItem 的 Item Type 选 Key、Item ID 填对应卡槽的 Key ID
/// </summary>
public class KeyDatabase : MonoBehaviour
{
    /// <summary> 全场景唯一实例（Portal 静态查询用） </summary>
    public static KeyDatabase Instance { get; private set; }

    /// <summary> 一个卡槽 = 一把钥匙：ID 用来和背包/PickupItem 对账，Name 用来给玩家看 </summary>
    [System.Serializable]
    public class KeySlotEntry
    {
        [Tooltip("钥匙 ID：必须和钥匙拾取物（PickupItem）的 Item ID 一模一样（区分大小写）")]
        public string keyID;
        [Tooltip("钥匙显示名：缺钥匙提示 / 背包里看到的都是这个名字")]
        public string keyName;
    }

    [Header("8 个钥匙卡槽（小泽自己填正式名；默认占位 Key1~Key8）")]
    public KeySlotEntry[] keys = new KeySlotEntry[8];

    private void Awake()
    {
        // 全场景唯一：重复挂了就警告并让先来的生效（防小泽不小心摆两个查错库）
        if (Instance != null && Instance != this)
        {
            Debug.LogWarning("[钥匙库] 场景里有多个 KeyDatabase！只有第一个（" + Instance.name + "）生效，请删掉多余的", gameObject);
            return;
        }
        Instance = this;

        // 8 个卡槽补齐 + 空槽预填占位（小泽后续在 Inspector 里改成正式名即可）
        if (keys == null || keys.Length != 8) keys = new KeySlotEntry[8];
        for (int i = 0; i < 8; i++)
        {
            if (keys[i] == null) keys[i] = new KeySlotEntry();
            if (string.IsNullOrEmpty(keys[i].keyID)) keys[i].keyID = "Key" + (i + 1);
            if (string.IsNullOrEmpty(keys[i].keyName)) keys[i].keyName = "钥匙 " + (i + 1);
        }
    }

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    /// <summary> 取某卡槽的钥匙 ID（查不到/没库 = 返回空串，Portal 会按"没钥匙"处理） </summary>
    public static string GetKeyID(int slot)
    {
        if (Instance == null)
        {
            Debug.LogWarning("[钥匙库] 场景里没有 KeyDatabase（建个空物体挂上它）——按没钥匙处理");
            return "";
        }
        if (slot < 0 || slot >= Instance.keys.Length || Instance.keys[slot] == null) return "";
        return Instance.keys[slot].keyID;
    }

    /// <summary> 取某卡槽的钥匙显示名（提示文案用） </summary>
    public static string GetKeyName(int slot)
    {
        if (Instance == null) return "？？？";
        if (slot < 0 || slot >= Instance.keys.Length || Instance.keys[slot] == null) return "钥匙 " + (slot + 1);
        return Instance.keys[slot].keyName;
    }
}
