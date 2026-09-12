using UnityEngine;

/// <summary> 弹药类型：每种枪只能用自己类型的弹药包 </summary>
public enum AmmoType { None, Pistol, Shotgun, Eagle }

[CreateAssetMenu(fileName = "新枪", menuName = "生化/枪数据")]
public class GunData : ScriptableObject
{
    public string gunName = "手枪";
    public int damage = 1;
    public float fireRate = 0.5f;
    public float range = 8f;
    public Sprite icon;

    [Tooltip("这把枪使用的弹药类型（弹药包按此区分，每把枪只能用自己的弹药）")]
    public AmmoType ammoType = AmmoType.Pistol;

    [Tooltip("弹夹容量：一次能装几发子弹，打空后需要按 R 换弹")]
    public int magSize = 15;

    [Tooltip("近战武器的耐久上限；设为 0 表示无限耐久（远程枪不用管这个）")]
    public int maxDurability = 0;

    [Header("近战破坏：可打破箱子（Breakable Container）")]
    [Tooltip("用刀砍'可打破箱子'时消耗多少刀耐久（默认 15；填 0 也按 15 兜底）。普通目标/敌人每下仍只扣 1")]
    public int meleeBoxDurabilityCost = 15;

    [Header("散射设置（散弹枪用；手枪/沙鹰保持默认 = 单发直射，行为和以前完全一样）")]
    [Tooltip("一次开火打出几发弹丸（1 = 单发直射，手枪/沙鹰不用动）")]
    public int pelletCount = 1;

    [Tooltip("散射总角度（度）：多发弹丸均匀铺在这个扇形里。0 = 全部打正中一点")]
    public float spreadAngle = 0f;

    [Tooltip("穿透总开关：勾上才允许穿透（具体穿几个看 penetrateLimit；手枪/沙鹰不勾 = 单发直射）")]
    public bool penetrate = false;

    [Tooltip("穿透上限：额外打穿几个目标。0 = 不穿透；1 = 穿 1 个（散弹现状）；-1 = 无限穿透（沙鹰用）。只在 penetrate 勾选时生效")]
    public int penetrateLimit = 1;

    [Tooltip("穿透伤害衰减：每穿一个目标伤害乘一次（1 = 不衰减，沙鹰用；0.5 = 每穿一个减半）。第 1 个目标永远吃全额")]
    public float penetrateDamageFalloff = 1f;

    [Header("音效")]
    [Tooltip("开火音效（每把枪拖各自的；不拖 = 静音开枪，不会报错。近战小刀不会播音效）")]
    public AudioClip fireClip;
    [Tooltip("打空音效（弹夹+备用弹全空时按 J 的'咔嗒'声；每把枪拖各自的，不拖 = 静音不报错）")]
    public AudioClip dryFireClip;
}
