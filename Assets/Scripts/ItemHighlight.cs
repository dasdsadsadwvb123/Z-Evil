using UnityEngine;

/// <summary>
/// 关键物品高亮：玩家靠近时，物品会"呼吸式"闪烁（金色脉冲），引导玩家拾取。
/// 玩家走远后自动恢复原样，不打扰远处的探索。
///
/// 用法：把本脚本挂到关键物品上（和 SpriteRenderer 同一个物体），
/// 再勾选同物体上 PickupItem 的 Is Key Item。
///
/// 注意：闪烁是纯代码控制 Sprite 透明度实现的，不涉及 Animator / Animation。
///
/// v2 改进（吸取教训）：
/// 1. 玩家引用只查找一次并缓存，不再每帧 FindObjectOfType（省性能）
/// 2. 防呆：如果误挂到玩家身上，自动停用，不会把自己闪成金色
/// 3. 物体没有 SpriteRenderer 时只警告，不报错、不影响其他逻辑
/// </summary>
public class ItemHighlight : MonoBehaviour
{
    [Header("高亮设置")]
    [Tooltip("玩家靠近到这个距离内开始闪烁（世界单位）")]
    public float highlightRange = 2.5f;

    [Tooltip("闪烁速度：数字越大闪得越快")]
    public float pulseSpeed = 4f;

    [Tooltip("闪烁时最低透明度（0~1）")]
    public float minAlpha = 0.25f;

    [Tooltip("闪烁时最高透明度（0~1）")]
    public float maxAlpha = 1f;

    [Tooltip("闪烁时叠加的颜色（金色 = 关键物品的经典配色）")]
    public Color highlightColor = new Color(1f, 0.85f, 0.3f);

    private SpriteRenderer sprite;
    private Color originalColor;        // 记住物品原本的颜色，走远后恢复
    private bool wasHighlighting = false;
    private PixelGridMovement cachedPlayer; // 缓存的玩家引用，只找一次
    private bool selfDisabled = false;      // 出问题自动停用，不影响游戏

    private void Start()
    {
        // 防呆：如果这个物体自己是玩家（带移动脚本），直接停用，别把自己闪了
        if (GetComponent<PixelGridMovement>() != null)
        {
            Debug.LogWarning("[高亮] ItemHighlight 不能挂在玩家身上！已自动停用。", gameObject);
            selfDisabled = true;
            enabled = false;
            return;
        }

        // 拿到物品身上的精灵渲染器（就是显示图片的那个组件）
        sprite = GetComponent<SpriteRenderer>();
        if (sprite == null)
        {
            Debug.LogWarning("[高亮] 物体上没有 SpriteRenderer，无法高亮！", gameObject);
            selfDisabled = true;
            enabled = false;
            return;
        }
        originalColor = sprite.color;

        // 只查找一次玩家引用并缓存（之前版本每帧查找，浪费性能）
        cachedPlayer = FindObjectOfType<PixelGridMovement>();
    }

    private void Update()
    {
        if (selfDisabled || sprite == null) return;

        // 玩家还没找到（比如出生较晚），偶尔再找一次，找到了就缓存住
        if (cachedPlayer == null)
        {
            cachedPlayer = FindObjectOfType<PixelGridMovement>();
            if (cachedPlayer == null) return;
        }

        bool playerNear = Vector2.Distance(transform.position, cachedPlayer.transform.position) <= highlightRange;

        if (playerNear)
        {
            // 靠近了：开始"呼吸式"闪烁
            // Mathf.Sin 产生 0~1~0 的正弦波 → 透明度跟着上下起伏
            float wave = 0.5f + 0.5f * Mathf.Sin(Time.time * pulseSpeed);
            float alpha = Mathf.Lerp(minAlpha, maxAlpha, wave);

            Color c = highlightColor;
            c.a = alpha;             // 只改透明度，颜色用高亮色
            sprite.color = c;
            wasHighlighting = true;
        }
        else if (wasHighlighting)
        {
            // 走远了：恢复物品原本的颜色，不再闪烁
            sprite.color = originalColor;
            wasHighlighting = false;
        }
    }

    /// <summary> 编辑器辅助：选中时画出高亮范围圈，方便调整 </summary>
    private void OnDrawGizmosSelected()
    {
        Gizmos.color = new Color(1f, 0.85f, 0.3f, 0.35f);
        Gizmos.DrawWireSphere(transform.position, highlightRange);
    }
}
