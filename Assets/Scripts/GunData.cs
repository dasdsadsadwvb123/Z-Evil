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
}
