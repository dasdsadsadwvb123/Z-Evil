using UnityEngine;

/// <summary>
/// 障碍查询统一工具（项目惯例）——所有"这格能不能走"的物理检测都走这里，自动排除自身/子物体。
///
/// 背景（暴君踩过的坑）：AI 走位判定用 Physics2D.OverlapBox / Raycast 查障碍层，如果不排除自己，
/// 当 Boss 碰撞体比一格大、或它本身就在障碍层里时，每次查相邻格都会命中自己 → 四个方向全判定被堵 →
/// 完全站桩不动（位置一动不动，日志无报错，极难查）。
///
/// 用法：把 AI 脚本里的 Physics2D.OverlapBox(pos, size, 0f, obstacleLayer) != null
///       换成 ObstacleQuery.BlockedAt(pos, size, transform, obstacleLayer)（self = 本物体 transform）。
/// 语义与原来完全一致，只是不再"自己挡自己"；真墙/其他物体照挡。
/// </summary>
public static class ObstacleQuery
{
    /// <summary> 该碰撞体是否属于本物体（自身或子物体） </summary>
    public static bool IsSelf(Collider2D c, Transform self)
    {
        if (c == null || self == null) return false;
        return c.transform == self || c.transform.IsChildOf(self);
    }

    /// <summary> pos 处（size 大小的盒）是否被"非自身"的障碍占据 </summary>
    public static bool BlockedAt(Vector2 pos, Vector2 size, Transform self, LayerMask mask)
    {
        Collider2D[] hits = Physics2D.OverlapBoxAll(pos, size, 0f, mask);
        foreach (Collider2D h in hits)
        {
            if (!IsSelf(h, self)) return true;
        }
        return false;
    }

    /// <summary> 从 from 沿 dir 射线 dist 距离，是否被"非自身"的障碍挡住 </summary>
    public static bool RaycastBlocked(Vector2 from, Vector2 dir, float dist, Transform self, LayerMask mask)
    {
        RaycastHit2D[] hits = Physics2D.RaycastAll(from, dir, dist, mask);
        foreach (RaycastHit2D h in hits)
        {
            if (h.collider != null && !IsSelf(h.collider, self)) return true;
        }
        return false;
    }

    /// <summary> pos 处半径 radius 的圆内是否有"非自身"的障碍 </summary>
    public static bool BlockedCircle(Vector2 pos, float radius, Transform self, LayerMask mask)
    {
        Collider2D[] hits = Physics2D.OverlapCircleAll(pos, radius, mask);
        foreach (Collider2D h in hits)
        {
            if (!IsSelf(h, self)) return true;
        }
        return false;
    }
}
