using UnityEngine;
using UnityEngine.Rendering.Universal;

/// <summary>
/// 黑暗总管（黑夜系统核心）：管理全场景的 Global Light 2D 亮度。
/// 黑屋触发区（DarkRoomZone）玩家进去就报数 +1、出来 -1；只要有 ≥1 个黑屋在罩着玩家，场景就调暗。
/// 场景里没有 Global Light 2D 时会自动生成一个（零配置也能跑）；已有就自动认领（不用拖引用）。
/// 挂法：Hierarchy 建一个空物体（如 Darkness），把本脚本挂上去即可（每个场景挂一个）。
/// 亮度用平滑过渡：进出黑屋时灯光是"渐暗/渐亮"，不是啪一下切换，更有电影感。
/// </summary>
public class DarknessController : MonoBehaviour
{
    [Header("亮度设置")]
    [Tooltip("正常亮度（明亮房间/室外的 Global Light 强度，1 = 正常白天）")]
    public float brightIntensity = 1f;
    [Tooltip("黑暗亮度（黑屋里的 Global Light 强度，0.05 = 几乎全黑；想留一点轮廓感调 0.1~0.15）")]
    public float darkIntensity = 0.05f;
    [Tooltip("明暗过渡速度：越大切换越快（3 = 大约半秒从亮渐到黑）")]
    public float transitionSpeed = 3f;

    private Light2D globalLight;
    private int darkZoneCount = 0; // 玩家当前在几个黑屋触发区里（0 = 明亮）

    /// <summary>
    /// 全局压暗系数（剧情演出用，如暴君狂化）：目标亮度 = 原目标 × 这个值。
    /// 静态字段：任何脚本直接 DarknessController.globalDimFactor = 0.7f 即可压暗全场景；
    /// 演出结束记得恢复 1f。黑屋明暗逻辑照常，只是整体再乘一层。
    /// </summary>
    public static float globalDimFactor = 1f;

    private void Start()
    {
        // 自动认领场景里现成的 Global Light 2D（小泽场景里有的话直接用，不用拖）
        foreach (Light2D light in FindObjectsOfType<Light2D>())
        {
            if (light.lightType == Light2D.LightType.Global)
            {
                globalLight = light;
                break;
            }
        }

        // 场景里一个 Global Light 都没有 → 自动生成一个（零配置可跑）
        if (globalLight == null)
        {
            GameObject go = new GameObject("GlobalLight2D (DarknessController 自动生成)");
            globalLight = go.AddComponent<Light2D>();
            globalLight.lightType = Light2D.LightType.Global;
            globalLight.intensity = brightIntensity;
            Debug.Log("[黑暗系统] 场景里没有 Global Light 2D，已自动生成一个", go);
        }
    }

    private void Update()
    {
        if (globalLight == null) return;

        // 目标亮度：有任何黑屋罩着玩家 → 暗；否则 → 亮。再乘全局压暗系数（剧情演出用）。平滑过渡靠 Lerp。
        float target = (IsDark() ? darkIntensity : brightIntensity) * globalDimFactor;
        globalLight.intensity = Mathf.Lerp(globalLight.intensity, target, transitionSpeed * Time.deltaTime);
    }

    /// <summary> 玩家现在是否处于黑暗中（有黑屋触发区罩着） </summary>
    public bool IsDark()
    {
        return darkZoneCount > 0;
    }

    // ======== 给 DarkRoomZone 调用的进出接口（计数制：两个黑屋可以叠加，全出来才回亮） ========

    public void EnterDarkZone()
    {
        darkZoneCount++;
        Debug.Log("[黑暗系统] 进入黑屋（当前黑屋数 " + darkZoneCount + "）");
    }

    public void ExitDarkZone()
    {
        darkZoneCount = Mathf.Max(0, darkZoneCount - 1);
        Debug.Log("[黑暗系统] 离开黑屋（当前黑屋数 " + darkZoneCount + "）");
    }

    /// <summary> 场景级查找（DarkRoomZone 用这个找总管） </summary>
    public static DarknessController FindInScene()
    {
        return FindObjectOfType<DarknessController>();
    }
}
