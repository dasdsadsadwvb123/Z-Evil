using UnityEngine;
using UnityEngine.Events;

/// <summary>
/// 可推箱子（推箱子解谜）：玩家朝箱子方向走顶到它 → 箱子向同方向平滑滑一格。
/// 推不动的情况（箱子原地不动、玩家也被挡）：目标格有墙 / 有其他箱子。
/// 经典 Sokoban 规则：只能推不能拉——推进角落就卡死，这是解谜难度的一部分。
/// 箱子压上压力板后可以再推走（板要"压上触发、推走取消"才有解谜感）。
/// 玩家检测完全在箱子侧做（输入驱动：按住方向键 + 贴近箱子 + 方向指向箱子），不碰 PixelGridMovement 一行。
/// 挡玩家的原理：箱子放 PushableBox 层，并把该层勾进玩家 obstacleLayer → 玩家自然走不进箱子格。
/// 挂到箱子上（需要：SpriteRenderer + BoxCollider2D(不勾 Is Trigger) + Rigidbody2D(Kinematic，没挂自动补) + 本脚本）。
/// </summary>
public class PushableBox : MonoBehaviour
{
    [Header("推动设置")]
    [Tooltip("一格多大（必须和玩家 PixelGridMovement 的 Tile Size 一致，都是 1）")]
    public float gridSize = 1f;
    [Tooltip("箱子滑动速度（格/秒）：慢一点有重物感")]
    public float pushSpeed = 3f;
    [Tooltip("连续推动的最小间隔（秒）：顶住不放时箱子一格一格挪的节奏")]
    public float pushInterval = 0.3f;
    [Tooltip("推箱时玩家减速倍率（0.5 = 慢一半）")]
    [Range(0.1f, 1f)]
    public float pushSlowMultiplier = 0.5f;
    [Tooltip("减速持续时间（秒）= 箱子滑一格的时长 + 缓冲；连续推会不断刷新")]
    public float pushSlowBuffer = 0.4f;

    [Header("检测范围（自适应箱子尺寸；想再放大就调大数值）")]
    [Tooltip("感应半径 = 箱子碰撞体对角线半径（含 Scale，多大都兜得住）+ 这个缓冲（格）")]
    public float detectBuffer = 0.75f;
    [Tooltip("侧偏容差下限（格）：实际容差 = max(这个值, 箱子侧向半宽×0.9)——大箱子沿边任意站位都算正后方")]
    public float crossToleranceMin = 0.6f;
    [Tooltip("阻挡检测 Layer：勾上【墙的层 + PushableBox 层】——这样撞墙/撞其他箱子都推不动")]
    public LayerMask obstacleLayer;

    [Header("音效（不拖 = 静音）")]
    [Tooltip("推动摩擦声（箱子开始滑动那一刻播）")]
    public AudioClip pushClip;

    [Header("推动事件（钩子）")]
    [Tooltip("箱子被推动时触发（Inspector 里可接音效/震屏等；将来接'玩家推箱减速'钩子用）")]
    public UnityEvent onPushed;

    /// <summary> 全局推动广播：将来做"推箱时玩家减速 50%"时，减速脚本订阅这个事件即可（现在无人订阅，不报错） </summary>
    public static event System.Action<PushableBox> OnAnyBoxPushed;

    private Rigidbody2D rb;
    private BoxCollider2D boxCol;    // 箱子碰撞体（检测范围按它的实际尺寸算，Scale 已含在内）
    private Transform player;
    private PixelGridMovement playerMovement;
    private Vector2 boxPos;          // 箱子当前格中心
    private bool isMoving = false;
    private float nextPushTime = 0f;

    // ⚠️ 排查用临时日志（修好后可整段删除：搜"[推箱诊断]"删 diagTimer/诊断代码块两处）
    private float diagTimer = 0f;
    private bool playerNullWarned = false; // player 为 null 只警告一次（防刷屏）

    private void Start()
    {
        rb = GetComponent<Rigidbody2D>();
        if (rb == null) rb = gameObject.AddComponent<Rigidbody2D>();
        rb.bodyType = RigidbodyType2D.Kinematic; // 箱子滑动由代码控制，不吃物理推挤
        rb.freezeRotation = true;

        // 箱子必须是实体碰撞体（不勾 Is Trigger）：玩家走不进箱子格
        boxCol = GetComponent<BoxCollider2D>();
        if (boxCol == null)
        {
            boxCol = gameObject.AddComponent<BoxCollider2D>();
            boxCol.size = new Vector2(0.9f, 0.9f);
        }
        boxCol.isTrigger = false;

        GameObject playerObj = GameObject.FindGameObjectWithTag("Player");
        if (playerObj != null)
        {
            player = playerObj.transform;
            playerMovement = playerObj.GetComponent<PixelGridMovement>();
        }
        else
        {
            Debug.LogWarning("[箱子] 场景里没有 Player 标签的物体，没人能推我", gameObject);
        }

        // 开局自动吸附整数格（和玩家 PixelGridMovement 的 SnapToGrid 同规则：X/Y 各 Round 最近整数，Z 不动）
        // 箱子摆歪半格也会自动归位——贴身/朝向判定从此不会因错位失灵
        Vector2 snapped = Snap(rb.position);
        if ((snapped - (Vector2)transform.position).sqrMagnitude > 0.0001f)
            Debug.Log("[箱子] 摆放位置没对齐格子，已自动吸附到最近整数格 " + snapped + "（选中物体看黄十字 = 格中心）", gameObject);
        boxPos = snapped;
        rb.position = boxPos;

        // ⚠️ 排查用无条件启动日志（关键）：Console 看到这条 = 脚本活着；
        // 看不到 = 脚本没挂上/没编译成功，后面所有诊断都无从谈起
        Debug.Log("[推箱诊断] PushableBox 已启动：位置=" + (Vector2)transform.position
            + (player != null
                ? "，玩家查找=成功（Tag=Player）"
                : "，玩家查找=失败 ⚠️ 场景里没有 Tag=Player 的物体（最常见的静默死因）"), gameObject);
    }

    private void Update()
    {
        // ⚠️ 排查用一次性警告：player 查找失败 = 脚本休眠，根因立即锁定（只警告一次防刷屏）
        if (player == null && !playerNullWarned)
        {
            playerNullWarned = true;
            Debug.LogWarning("[推箱诊断] player 为 null，脚本休眠中（场景里没有 Tag=Player 的物体）", gameObject);
        }

        if (isMoving || player == null) return;

        // ---- 1. 玩家当前按住的方向（输入驱动：按住 = 想推；不按键永远不推，天然防挂机） ----
        // 之前用"位移窗口"判定玩家在走——但推箱时玩家被箱子挡住、按住键位移恰好归零，
        // 最想推的时刻反而判不成立，整段废弃。直接读输入轴才是玩家意图的可靠来源。
        float hx = Input.GetAxisRaw("Horizontal");
        float vy = Input.GetAxisRaw("Vertical");
        Vector2 heldDir = Vector2.zero;
        if (Mathf.Abs(hx) >= Mathf.Abs(vy))
        {
            if (Mathf.Abs(hx) > 0.01f) heldDir = new Vector2(Mathf.Sign(hx), 0);
        }
        else
        {
            if (Mathf.Abs(vy) > 0.01f) heldDir = new Vector2(0, Mathf.Sign(vy));
        }

        // ---- 2. 玩家相对箱子的位置：主导轴定方向 + 自适应侧偏容差 ----
        // 玩家 0.5 步长会停在半格位置，不再要求严格 0.05 轴对齐（那就是之前"永远错位"的第二个坑）
        Vector2 toBox = boxPos - (Vector2)player.position;
        float dist = toBox.magnitude;

        // 检测范围按箱子碰撞体实际尺寸自适应：col.bounds 已含 Scale——
        // 对角线半径 + 缓冲，箱子拉多大感应范围就跟着多大（不再用固定公式猜）
        float detectRadius = (boxCol != null ? boxCol.bounds.extents.magnitude : gridSize) + gridSize * detectBuffer;

        Vector2 dirToBox;  // 从玩家指向箱子的方向（沿主导轴）
        float crossAxis;   // 另一轴的偏移量（侧偏）
        if (Mathf.Abs(toBox.x) >= Mathf.Abs(toBox.y))
        {
            dirToBox = new Vector2(Mathf.Sign(toBox.x), 0);
            crossAxis = Mathf.Abs(toBox.y);
        }
        else
        {
            dirToBox = new Vector2(0, Mathf.Sign(toBox.y));
            crossAxis = Mathf.Abs(toBox.x);
        }

        // 侧偏容差随箱子侧向半宽自适应：横向判定用 Y 半高、垂直判定用 X 半宽（×0.9，下限 crossToleranceMin）
        // 大箱子沿边任意站位都算"正后方"，不再被固定 0.6 卡死
        float crossTolerance = crossToleranceMin;
        if (boxCol != null)
        {
            float sideHalf = (dirToBox.y == 0f) ? boxCol.bounds.extents.y : boxCol.bounds.extents.x;
            crossTolerance = Mathf.Max(crossToleranceMin, sideHalf * 0.9f);
        }

        // ---- ⚠️ 排查用临时日志：检测半径内每 0.5 秒一条（修好后整段删掉） ----
        diagTimer += Time.deltaTime;
        if (diagTimer >= 0.5f)
        {
            diagTimer = 0f;
            if (dist <= detectRadius)
            {
                string verdict = (heldDir == dirToBox && crossAxis <= crossTolerance && dist <= detectRadius)
                    ? (Time.time < nextPushTime ? "可推（冷却中）" : "可推")
                    : "不推";
                Debug.Log("[推箱诊断] 距离=" + dist.ToString("F2")
                    + " | 检测半径=" + detectRadius.ToString("F2")
                    + " | 主导轴=" + (Mathf.Abs(toBox.x) >= Mathf.Abs(toBox.y) ? "水平" : "垂直")
                    + " | 指向箱子=" + dirToBox
                    + " | 侧偏=" + crossAxis.ToString("F2") + "/" + crossTolerance.ToString("F2")
                    + " | 按住方向=" + heldDir
                    + " | 判定=" + verdict, gameObject);
            }
        }

        // ---- 3. 三道判定：距离上限（随箱子尺寸自适应）/ 侧偏容差（随箱子半宽自适应）/ 按住方向指向箱子 ----
        if (dist > detectRadius) return;            // 离得太远
        if (crossAxis > crossTolerance) return;     // 斜站超过容差
        if (heldDir == Vector2.zero) return;        // 没按方向键
        if (heldDir != dirToBox) return;            // 按的方向不是指向箱子（比如背对箱子按键 = 想走不是想推）
        if (Time.time < nextPushTime) return;       // 推动间隔没到

        TryPush(dirToBox);
    }

    /// <summary> 尝试朝 dir 推一格：目标格有墙/有【其他】箱子 = 推不动（玩家自然也被实体碰撞挡住） </summary>
    private void TryPush(Vector2 dir)
    {
        Vector2 target = boxPos + dir * gridSize;

        // 目标格被墙/障碍/其他箱子占着 → 推不动。
        // ⚠️ 必须用 OverlapBoxAll + 排除自身：箱子 Scale 放大后自己的巨型碰撞体会盖住相邻目标格，
        // 用旧的单发 OverlapBox 查到的"障碍"就是它自己 → 自己把自己当墙，判定=可推却永远推不动。
        Collider2D[] hits = Physics2D.OverlapBoxAll(target, Vector2.one * gridSize * 0.9f, 0f, obstacleLayer);
        foreach (Collider2D hit in hits)
        {
            // 自己（含自己身上的子碰撞体）不算障碍
            if (hit.transform == transform || hit.transform.IsChildOf(transform)) continue;
            return; // 真有墙/其他箱子才挡
        }

        StartCoroutine(MoveRoutine(target));
    }

    /// <summary> 平滑滑到目标格（匀速，重物感），到位后结算事件/音效/冷却 </summary>
    private System.Collections.IEnumerator MoveRoutine(Vector2 target)
    {
        isMoving = true;

        // 推箱减速钩子：减速时长 = 滑一格的时长 + 缓冲，连续推会不断刷新（松手后缓冲耗尽自动恢复满速）
        float slideTime = gridSize / pushSpeed;
        if (playerMovement != null) playerMovement.ApplySlow(slideTime + pushSlowBuffer, pushSlowMultiplier);

        Vector2 start = boxPos;

        while ((Vector2)rb.position != target)
        {
            rb.position = Vector2.MoveTowards(rb.position, target, pushSpeed * Time.deltaTime);
            yield return null;
        }

        boxPos = target;
        rb.position = target;
        isMoving = false;
        nextPushTime = Time.time + pushInterval;

        if (pushClip != null) AudibleAudio.PlayAt(pushClip, transform.position); // 距离听声：走近才听得见箱子摩擦声
        onPushed.Invoke();
        OnAnyBoxPushed?.Invoke(this);
    }

    /// <summary> 把坐标吸附到格中心 </summary>
    private Vector2 Snap(Vector2 pos)
    {
        pos.x = Mathf.Round(pos.x / gridSize) * gridSize;
        pos.y = Mathf.Round(pos.y / gridSize) * gridSize;
        return pos;
    }

    // 调试可视化：绿框 = 箱子吸附后占的格，黄十字 = 格中心（编辑器里摆歪半格也一眼看出会被吸到哪）
    private void OnDrawGizmosSelected()
    {
        // 运行中用已吸附的 boxPos；编辑器里现场算吸附落点（同一套 Snap 规则）
        Vector2 cell = Application.isPlaying ? boxPos : Snap(transform.position);

        Gizmos.color = Color.green;
        Gizmos.DrawWireCube(cell, Vector3.one * gridSize * 0.95f); // 箱子该在的格

        Gizmos.color = Color.yellow;                               // 格中心十字
        float r = gridSize * 0.2f;
        Gizmos.DrawLine(cell + new Vector2(-r, 0f), cell + new Vector2(r, 0f));
        Gizmos.DrawLine(cell + new Vector2(0f, -r), cell + new Vector2(0f, r));
    }
}
