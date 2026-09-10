using UnityEngine;

/// <summary>
/// 压力板（推箱子解谜）：每帧查自己格子上有没有箱子（PushableBox 层的实体碰撞体）压着。
/// 有箱子 = 压下（变色/换图 + 音效）；箱子推走 = 弹起取消——"压上触发、推走取消"才有解谜感。
/// 玩家踩上去不算：除了只检测箱子层，命中物还必须带 PushableBox 组件——boxLayer 误勾 Default 也不会误触（防呆兜底）。
/// 和开关板的关联零接线：板子 Start 时自动把自己注册给场景里的 PlateSwitchBoard（最不容易配错）。
/// 挂到压力板上（需要：SpriteRenderer + BoxCollider2D(勾 Is Trigger，不挡路) + 本脚本）。
/// </summary>
public class PressurePlate : MonoBehaviour
{
    [Header("检测设置")]
    [Tooltip("箱子所在 Layer：只勾 PushableBox 层（别勾 Default，不然板子会检测到自己/玩家）")]
    public LayerMask boxLayer;

    [Header("外观反馈")]
    [Tooltip("未压下颜色")]
    public Color idleColor = Color.white;
    [Tooltip("压下颜色（占位反馈：整体变暗表示被压住）")]
    public Color pressedColor = new Color(0.55f, 0.55f, 0.55f);
    [Tooltip("压下时切换的贴图（可选换图口子：拖了'压下状态图'就优先换图，没拖就只变色）")]
    public Sprite pressedSprite;

    [Header("音效（不拖 = 静音）")]
    [Tooltip("压下一瞬间的咔哒声")]
    public AudioClip pressClip;
    [Tooltip("弹起时的声音")]
    public AudioClip releaseClip;

    /// <summary> 当前是否被箱子压着（PlateSwitchBoard 每帧读这个判断"是否全部压上"） </summary>
    public bool IsPressed { get; private set; }

    private BoxCollider2D col;
    private SpriteRenderer sr;
    private Sprite idleSprite; // 开场记下原图，弹起时换回

    private void Awake()
    {
        // 碰撞体在 Awake 就补齐（比 Start 更早，杜绝 Update 先跑/缓存被外部清掉的时机问题）
        col = GetComponent<BoxCollider2D>();
        if (col == null)
        {
            // 防呆：误装了 3D BoxCollider（用 3D Cube 拼场景常见手误）——2D 游戏用不了，
            // 且它存在时 2D 版 AddComponent 会冲突失败返回 null（就是那次 NRE 的根因）。
            // 自动移除 3D 版再补 2D 版。
            BoxCollider box3d = GetComponent<BoxCollider>();
            if (box3d != null)
            {
                Debug.LogWarning("[压力板] 发现 3D BoxCollider（2D 游戏用不了），已自动移除并补 2D 碰撞体", gameObject);
                DestroyImmediate(box3d); // 用 DestroyImmediate：运行时也立即生效，才能同帧补上 2D 版
            }
            col = GetComponent<BoxCollider2D>();
            if (col == null) col = gameObject.AddComponent<BoxCollider2D>();
        }
        if (col != null) col.isTrigger = true; // null 守卫：万一仍加不成（理论到不了）→ 交给 Update 兜底，这里不 NRE

        // 音效不再用本地 AudioSource：统一走 AudibleAudio.PlayAt（距离听声，见类头注释的项目惯例）

        // ⚠️ 排查用轻量日志：出现 = 脚本 Awake 活着（物体激活、脚本挂上了、没编译报错）
        Debug.Log("[压力板] Awake 完成：" + name, gameObject);
    }

    private void Start()
    {
        // ⚠️ 排查用无条件启动日志：几块板就该有几条；没有 = 板子不在本场景/没挂脚本/物体未激活
        // boxLayer 直接打数值：0 = 一个层都没勾（最常见的漏配，检测永远查不到箱子）
        Debug.Log("[压力板] 已启动：name=" + name
            + " 位置=" + (Vector2)transform.position
            + " boxLayer=" + (int)boxLayer.value, gameObject);

        sr = GetComponent<SpriteRenderer>();
        idleSprite = sr != null ? sr.sprite : null;

        // 自动注册给场景里的开关板（零接线；没有开关板也能变色/响，只是不触发机关）
        PlateSwitchBoard board = FindObjectOfType<PlateSwitchBoard>();
        if (board != null)
        {
            board.Register(this);
        }
        else
        {
            Debug.LogWarning("[压力板] 场景里没有【激活的】PlateSwitchBoard（注意：物体没勾选激活也找不到），我这块板压了也不会触发机关（建一个空物体挂上它）", gameObject);
        }
    }

    private void Update()
    {
        // 空引用兜底：Awake 理论已补齐，万一碰撞体被外部删了/缓存失效 → 现场再补一次；补不成安静跳过不炸
        if (col == null)
        {
            col = GetComponent<BoxCollider2D>();
            if (col == null) col = gameObject.AddComponent<BoxCollider2D>();
            if (col != null) col.isTrigger = true;
        }
        if (col == null) return; // 理论到不了（AddComponent 不太会失败），到不了白到不了，炸不了

        // 以自身碰撞体范围检测：有没有【真正的箱子】压在板上。
        // ⚠️ 组件验证防呆：命中物身上必须有 PushableBox 组件才算——就算 boxLayer 误勾了 Default
        //（玩家/板子自己/其他杂物），也不会被当成箱子误触发（就是之前"传送时板子咔哒响"的根因）。
        Vector2 center = (Vector2)transform.position + (Vector2)col.offset;
        bool pressed = false;
        Collider2D[] hits = Physics2D.OverlapBoxAll(center, col.size, 0f, boxLayer);
        foreach (Collider2D hit in hits)
        {
            if (hit.GetComponentInParent<PushableBox>() != null) // 用 GetComponentInParent：箱子碰撞体挂子物体也能认出
            {
                pressed = true;
                break;
            }
        }

        if (pressed == IsPressed) return; // 状态没变就每帧安静轮询

        IsPressed = pressed;
        ApplyVisual();

        if (pressed && pressClip != null) AudibleAudio.PlayAt(pressClip, transform.position);      // 距离听声：走近才听得见
        if (!pressed && releaseClip != null) AudibleAudio.PlayAt(releaseClip, transform.position); // 同上（弹起声）
        Debug.Log("[压力板] " + name + (pressed ? " 被箱子压下！" : " 弹起复位"));
    }

    /// <summary> 换图口子：配了压下图就优先换图，没配就只变色（占位方案） </summary>
    private void ApplyVisual()
    {
        if (sr == null) return;
        sr.sprite = (IsPressed && pressedSprite != null) ? pressedSprite : idleSprite;
        sr.color = IsPressed ? pressedColor : idleColor;
    }

    // 调试可视化：黄框 = 检测范围
    private void OnDrawGizmosSelected()
    {
        BoxCollider2D c = GetComponent<BoxCollider2D>();
        if (c == null) return;
        Gizmos.color = Color.yellow;
        Gizmos.DrawWireCube(transform.position + (Vector3)c.offset, c.size);
    }
}
