using UnityEngine;

/// <summary>
/// 黑屋触发区：门口/房间铺一块透明 BoxCollider2D（Is Trigger），玩家走进来 = 黑暗开始，
/// 走出去 = 恢复明亮。两个房间各摆一块，各自独立控制（都支持，计数制可叠加）。
/// 挂法：黑屋范围盖一个空物体 + BoxCollider2D（勾 Is Trigger）+ 本脚本；
/// 范围 = 触发区大小，用 BoxCollider2D 的 Size 拉到盖住整个黑屋。
/// 会自动生成触发器（忘挂 BoxCollider2D 时补一个 6x6 的，记得自己拉大小）。
/// </summary>
[RequireComponent(typeof(BoxCollider2D))]
public class DarkRoomZone : MonoBehaviour
{
    private DarknessController controller;
    private bool playerInside = false; // 防重复进出（黑屋里触发器重叠时不会报两次）

    private void Start()
    {
        // 触发器保险：忘了勾 Is Trigger 自动补上（不勾的话玩家会被实体墙挡住）
        BoxCollider2D col = GetComponent<BoxCollider2D>();
        col.isTrigger = true;

        controller = DarknessController.FindInScene();
        if (controller == null)
            Debug.LogWarning("[黑屋] 场景里没有 DarknessController！请建一个空物体挂上它，否则本触发区不生效", gameObject);
    }

    private void OnTriggerEnter2D(Collider2D other)
    {
        if (!other.CompareTag("Player") || playerInside) return;
        playerInside = true;
        if (controller != null) controller.EnterDarkZone();
    }

    private void OnTriggerExit2D(Collider2D other)
    {
        if (!other.CompareTag("Player") || !playerInside) return;
        playerInside = false;
        if (controller != null) controller.ExitDarkZone();
    }

    // 调试可视化：选中时 Scene 窗口画绿框 = 黑屋范围
    private void OnDrawGizmosSelected()
    {
        BoxCollider2D col = GetComponent<BoxCollider2D>();
        if (col == null) return;
        Gizmos.color = new Color(0f, 1f, 0.5f, 0.35f);
        Gizmos.DrawCube(transform.position, col.size);
    }
}
