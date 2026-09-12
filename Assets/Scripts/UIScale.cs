using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// UI 画布缩放统一工具（项目惯例）。
///
/// 背景：代码里动态创建的 Screen Space Overlay Canvas，如果只设了 uiScaleMode = ScaleWithScreenSize
/// 而没设参考分辨率，Unity 会用默认的 800×600 —— 玩家在大屏（1920/2560）上界面会被放大约 2 倍以上，
/// 面板跑飞 / 文字出屏。这个坑我们已经复发过好几次（管路/过敏名单/纸条）。
///
/// 约定：以后任何动态创建的 UI Canvas，一律用 UIScale.Setup(canvasGO) 加 CanvasScaler，
/// 固定 1920×1080 参考分辨率 + matchWidthOrHeight = 1，别再手写 AddComponent<CanvasScaler>()。
/// </summary>
public static class UIScale
{
    public const float RefWidth = 1920f;
    public const float RefHeight = 1080f;

    /// <summary>
    /// 给 Canvas 物体添加并配置 CanvasScaler（1920×1080 + 按高度匹配），返回该组件便于进一步调整。
    /// </summary>
    public static CanvasScaler Setup(GameObject canvasGO)
    {
        CanvasScaler cs = canvasGO.AddComponent<CanvasScaler>();
        cs.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        cs.referenceResolution = new Vector2(RefWidth, RefHeight);
        cs.matchWidthOrHeight = 1f; // 以高度为基准匹配（横版为主，保证纵向布局稳定）
        return cs;
    }
}
