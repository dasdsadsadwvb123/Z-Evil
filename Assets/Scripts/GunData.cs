using UnityEngine;

[CreateAssetMenu(fileName = "新枪", menuName = "生化/枪数据")]
public class GunData : ScriptableObject
{
    public string gunName = "手枪";
    public int damage = 1;
    public float fireRate = 0.5f;
    public float range = 8f;
    public Sprite icon;

    [Tooltip("这把枪的弹药上限（满弹量）")]
    public int maxAmmo = 15;
}
