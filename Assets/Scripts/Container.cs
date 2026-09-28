using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 箱子系统（一个脚本两种类型，Inspector 下拉区分）：
/// - Cabinet（衣柜式）：无血量不会坏，靠近按 F 打开取物界面，点击箱内物品取出到背包。= 安全储物柜。
/// - Breakable（可破坏式）：挂 HealthSystem（没有自动补）——刀/枪/舔食者都能打，血量归零破裂：
///   换破损贴图 + 破裂音效 + 内容物全部掉在地上变拾取物（参考密码机钥匙/乌鸦掉落套路），
///   碰撞体禁用（废墟可穿行）。没有 F 开盖交互——里面的东西只能打碎后捡。= 只能砸的杂物箱。
///
/// ⚠️ 语义："往箱子里塞东西"是开发者行为——在 Inspector 的 Starter Items 里预填；
/// 玩家只能取出（柜子里点走 / 打碎捡走），没有"放入"功能。
///
/// 界面由 ContainerUI 负责（运行时自动生成，不用手动挂）——和玩家 TAB 是两套独立界面。
/// 音效走 AudibleAudio.PlayAt 距离听声惯例（走近才听得见）。
///
/// 挂载：箱子物体上放 SpriteRenderer（小泽画的箱子图）+ 本脚本；
/// Breakable 类型会自动补 HealthSystem + BoxCollider2D（实体，能被打）。
/// </summary>
public class Container : MonoBehaviour
{
    public enum ContainerType { Cabinet, Breakable }

    /// <summary> 初始物品条目：开局箱子里就有的东西（小泽 Inspector 里填） </summary>
    [System.Serializable]
    public class StarterEntry
    {
        [Tooltip("物品 ID（和背包/模板表对账用，如 GreenHerb）")]
        public string itemID = "";
        [Tooltip("物品显示名（如 绿草）")]
        public string itemName = "";
        [Tooltip("物品类型")]
        public ItemType itemType = ItemType.Key;
        [Tooltip("图标（背包/箱子里显示）")]
        public Sprite icon;
        [TextArea][Tooltip("描述（背包里选中时显示）")]
        public string description = "";
        [Tooltip("放几个（每个占背包一格，不堆叠——和玩家背包规则一致）")]
        public int count = 1;
        [Tooltip("如果是枪：拖 GunData（会带满弹夹满耐久）")]
        public GunData gunData;
        [Tooltip("如果是草药：绿/红")]
        public HerbType herbType = HerbType.Green;
        [Tooltip("草药回血量")]
        public int healAmount = 1;

        [Header("如果是弹药（itemType 选 Ammo 时才用；取出直接进备用弹药池，不占背包格）")]
        [Tooltip("这包子弹属于哪种弹药（每把枪只能用自己的弹药）")]
        public AmmoType ammoType = AmmoType.Pistol;
        [Tooltip("取出后加入备用弹药池的数量")]
        public int ammoAmount = 10;
    }

    [Header("箱子类型")]
    [Tooltip("Cabinet = 衣柜式（纯储物）；Breakable = 可破坏式（能被打碎掉落）")]
    public ContainerType type = ContainerType.Cabinet;

    [Header("初始物品（开发者在这里预填箱内物品；玩家只能取出不能放入）")]
    public StarterEntry[] starterItems;

    [Header("贴图")]
    [Tooltip("正常态箱子图（不拖就用 SpriteRenderer 上现成的图）")]
    public Sprite normalSprite;
    [Tooltip("破裂后的废墟图（只有 Breakable 用；不拖就保持原图）")]
    public Sprite brokenSprite;

    [Header("交互")]
    [Tooltip("玩家离箱子多近能按 F 打开")]
    public float interactRange = 1.5f;

    [Header("密码锁（勾选后按 F 不开柜，先弹密码面板；输对一次永记——读档重置）")]
    [Tooltip("需要密码：勾上后按 F 弹出数字密码面板（0-9 任意位），输对才打开柜子")]
    public bool requirePassword = false;
    [Tooltip("正确密码（如 1962）：数字键输入，位数不限，支持前导零")]
    public string password = "";
    [Tooltip("输错音效（可选；不拖 = 只有红闪没声）")]
    public AudioClip wrongClip;
    [Tooltip("密码输入正确的音效（可选）")]
    public AudioClip successClip;

    [Header("音效（不拖 = 静音；走距离听声惯例）")]
    public AudioClip openClip;
    public AudioClip takeClip;   // 取出物品的声音
    public AudioClip breakClip;

    [Header("可破坏式专属")]
    [Tooltip("血量（只有 Breakable 且身上没挂 HealthSystem 时，自动补的这个值才生效）")]
    public int breakableHealth = 4;
    [Tooltip("只能被刀砸开（枪打不掉血）。只对 Breakable 类型生效；Cabinet/密码箱完全不受影响")]
    public bool meleeOnlyBreak = true;

    /// <summary> 箱子当前内容物（运行时列表；存的是完整物品条目，弹药/耐久状态原样保留） </summary>
    [HideInInspector] public List<InventoryItem> contents = new List<InventoryItem>();

    public bool IsBroken { get; private set; } = false;

    /// <summary> 密码是否已解开（运行时标记：解开本次游戏内永记，读档重置） </summary>
    private bool unlocked = false;

    private SpriteRenderer sr;
    private Transform player;
    private HealthSystem myHealth;
    private GameObject promptUI;
    private Text promptTextUI;         // 提示文字引用（密码锁动态切"输入密码/打开"文案）
    private string baseKey;            // 世界进度表钥匙前缀（读档还原容器状态用）

    private void Start()
    {
        sr = GetComponent<SpriteRenderer>();
        baseKey = WorldState.KeyFor("Container", this);

        // 初始物品 → 内容物（count 个 = count 条，和玩家背包"每格一件"规则一致）
        // 读档还原：每种 itemID 记录过"取走几个"，就从初始内容里扣掉几个（先取先扣）
        if (starterItems != null)
        {
            foreach (StarterEntry e in starterItems)
            {
                if (e == null || string.IsNullOrEmpty(e.itemID)) continue;
                int n = Mathf.Max(1, e.count);
                int taken = WorldState.Get(baseKey + "_Taken_" + e.itemID, 0);
                for (int i = 0; i < n; i++)
                {
                    if (taken > 0) { taken--; continue; } // 这个已经取走过，不再生成
                    contents.Add(CreateItem(e));
                }
            }
        }

        // 密码已解过 → 恢复"本次游戏内不用再输密码"
        if (requirePassword && WorldState.GetBool(baseKey + "_Pw"))
            unlocked = true;

        // 找玩家（提示 UI + 距离检测用）
        GameObject p = GameObject.FindGameObjectWithTag("Player");
        if (p != null) player = p.transform;
        else Debug.LogWarning("[箱子] 场景里没有 Player 标签的物体，没人能打开我", gameObject);

        if (type == ContainerType.Breakable) SetupBreakable();
        else CreatePromptUI(); // 只有 Cabinet（衣柜式）需要"按F打开"提示；Breakable 没有开盖交互

        // 已破坏 → 恢复废墟状态（放最后：SetupBreakable 已把 HealthSystem/碰撞体备好，这里再改）
        if (WorldState.GetBool(baseKey + "_Broken"))
            ApplyBrokenState();
    }

    /// <summary> 恢复"已打碎"的废墟状态（读档用：换废墟图 + 清空内容 + 关碰撞 + 不再响应） </summary>
    private void ApplyBrokenState()
    {
        IsBroken = true;
        contents.Clear();
        if (sr != null && brokenSprite != null) sr.sprite = brokenSprite;
        BoxCollider2D col = GetComponent<BoxCollider2D>();
        if (col != null) col.enabled = false;
        if (promptUI != null) promptUI.SetActive(false);
    }

    /// <summary> 把 Inspector 条目转成运行时物品条目（枪会自动满弹满耐久，和拾取规则一致） </summary>
    private InventoryItem CreateItem(StarterEntry e)
    {
        InventoryItem item = new InventoryItem
        {
            itemID = e.itemID,
            itemName = e.itemName,
            itemType = e.itemType,
            icon = e.icon,
            description = e.description,
            gunData = e.gunData,
            herbType = e.herbType,
            healAmount = e.healAmount,
            ammoType = e.ammoType,      // 弹药类型（箱内取子弹时入备用弹药池用）
            ammoAmount = e.ammoAmount   // 弹药数量
        };
        if (item.gunData != null)
        {
            item.magSize = item.gunData.magSize;
            item.currentAmmo = item.magSize;
            item.maxDurability = item.gunData.maxDurability;
            item.currentDurability = item.maxDurability;
        }
        return item;
    }

    /// <summary> 可破坏式：确保有血量系统（复用现有受伤链路——枪/刀/舔食者都能打它） </summary>
    private void SetupBreakable()
    {
        myHealth = GetComponent<HealthSystem>();
        if (myHealth == null)
        {
            myHealth = gameObject.AddComponent<HealthSystem>();
            myHealth.maxHealth = breakableHealth;
        }
        myHealth.currentHealth = myHealth.maxHealth;
        myHealth.destroyOnDeath = false; // 死亡生命周期归本脚本管（不销毁，换废墟图）
        myHealth.deathTriggerName = "";  // 箱子没有动画
        myHealth.meleeOnly = meleeOnlyBreak; // 只能被刀砸开（枪打不掉血）；勾选状态直写，取消勾选也能恢复
        myHealth.OnDeath += Break;

        // 破坏式箱子必须是实体碰撞体（子弹/刀的判定要能碰到它）
        BoxCollider2D col = GetComponent<BoxCollider2D>();
        if (col == null) col = gameObject.AddComponent<BoxCollider2D>();
        col.isTrigger = false;
    }

    private void Update()
    {
        if (IsBroken || player == null) return;

        // 可破坏式：没有 F 开盖交互（设计定稿：Breakable = 只能砸的杂物箱，里面的东西打碎后捡）。
        // 靠近不显示提示、按 F 无反应——只有 Cabinet（衣柜式）能开。
        if (type == ContainerType.Breakable) return;

        // 界面开着不响应；【关闭当帧】的 F 也无视（ContainerUI 同帧已 Close，再读 F 会立刻重开 = "只能开一次"的根因）
        if (ContainerUI.IsOpen || Time.frameCount == ContainerUI.LastClosedFrame)
        {
            if (promptUI != null) promptUI.SetActive(false);
            return;
        }

        bool near = Vector2.Distance(transform.position, player.position) <= interactRange;
        if (promptUI != null)
        {
            promptUI.SetActive(near);
            // 提示文案动态切换：密码没解开 = "输入密码"；解开/普通柜 = "打开"
            if (promptTextUI != null)
                promptTextUI.text = (requirePassword && !unlocked)
                    ? "按F输入密码"
                    : (type == ContainerType.Cabinet ? "按F打开柜子" : "按F打开箱子");
        }

        if (near && WorldInteractionBlocker.GetKeyDown(KeyCode.F))
        {
            // 密码锁：没解开时按 F 不开柜，弹密码面板（输对一次永记，之后走正常开柜）
            if (requirePassword && !unlocked)
            {
                if (PasswordPad.IsOpen) return; // 面板开着时 F 不重复弹（防重置输入）
                AudibleAudio.PlayAt(openClip, transform.position);
                PasswordPad.Open(password, OnPasswordSuccess, OnPasswordWrong, null);
                return;
            }

            AudibleAudio.PlayAt(openClip, transform.position);
            ContainerUI.Open(this); // 打开独立取物界面（自动暂停游戏）
        }
    }

    /// <summary> 密码输对（PasswordPad 回调）：永记解锁状态 → 打开柜子取东西 </summary>
    private void OnPasswordSuccess()
    {
        unlocked = true;
        WorldState.Set(baseKey + "_Pw", 1); // 世界进度表登记"密码已解"
        Debug.Log("[箱子] " + name + " 密码正确，已解锁！本次游戏内不再需要输密码", gameObject);
        if (successClip != null) AudibleAudio.PlayAt(successClip, transform.position); // 密码正确音效
        AudibleAudio.PlayAt(openClip, transform.position);
        ContainerUI.Open(this);
    }

    /// <summary> 密码输错（PasswordPad 回调）：红闪由面板自带，这里播音效；次数不限 </summary>
    private void OnPasswordWrong()
    {
        AudibleAudio.PlayAt(wrongClip, transform.position);
    }

    // ======== 内容物取出（ContainerUI 调用；玩家只能取不能放） ========

    /// <summary> 从箱子里取出第 index 件（箱子侧移除，交给背包） </summary>
    public InventoryItem TakeOut(int index)
    {
        if (index < 0 || index >= contents.Count) return null;
        InventoryItem item = contents[index];
        contents.RemoveAt(index);
        // 世界进度表登记"这种物品取走了 1 个"（读档还原容器内容用）
        if (!string.IsNullOrEmpty(item.itemID))
        {
            string k = baseKey + "_Taken_" + item.itemID;
            WorldState.Set(k, WorldState.Get(k, 0) + 1);
        }
        AudibleAudio.PlayAt(takeClip, transform.position);
        return item;
    }

    // ======== 破裂（HealthSystem 血量归零触发） ========

    private void Break()
    {
        if (IsBroken) return;
        IsBroken = true;
        WorldState.Set(baseKey + "_Broken", 1); // 世界进度表登记"已打碎"

        Debug.Log("[箱子] " + name + " 被打碎了！掉落 " + contents.Count + " 件物品", gameObject);

        // 废墟贴图（没拖就保持原图）
        if (sr != null && brokenSprite != null) sr.sprite = brokenSprite;

        // 破裂音效（距离听声）
        AudibleAudio.PlayAt(breakClip, transform.position);

        // 内容物全部掉在地上变拾取物（散落在箱子周围，参考密码机钥匙套路）
        for (int i = 0; i < contents.Count; i++)
        {
            Vector2 offset = Random.insideUnitCircle.normalized * Random.Range(0.5f, 1.2f);
            SpawnDrop(contents[i], (Vector2)transform.position + offset);
        }
        contents.Clear();

        // 关掉提示 UI，不能再打开
        if (promptUI != null) promptUI.SetActive(false);

        // 碰撞体彻底禁用：玩家可以直接走过废墟（破损贴图保留在地上当装饰）
        BoxCollider2D col = GetComponent<BoxCollider2D>();
        if (col != null) col.enabled = false;
    }

    /// <summary> 把一件物品生成掉落拾取物（字段全量回填，弹药/耐久状态原样保留） </summary>
    private void SpawnDrop(InventoryItem item, Vector2 pos)
    {
        GameObject go = new GameObject("掉落_" + item.itemName);
        go.transform.position = pos;

        SpriteRenderer dropSr = go.AddComponent<SpriteRenderer>();
        dropSr.sprite = item.icon;   // 没图标的物品看不见但能捡（模板表配了 icon 的都有图）
        dropSr.sortingOrder = 10;

        PickupItem drop = go.AddComponent<PickupItem>();
        drop.itemID = item.itemID;
        drop.itemName = item.itemName;
        drop.itemType = item.itemType;
        drop.icon = item.icon;
        drop.description = item.description;
        drop.gunData = item.gunData;
        drop.herbType = item.herbType;
        drop.healAmount = item.healAmount;
        drop.ammoType = item.ammoType;     // 弹药类型（打碎箱子掉出来的子弹也要能正确入池）
        drop.ammoAmount = item.ammoAmount; // 弹药数量
        drop.destroyOnPickup = true; // 捡走就消失（防反复刷）
    }

    // ======== 提示 UI（照 PlayerPickup 的轻量提示套路） ========

    private void CreatePromptUI()
    {
        promptUI = new GameObject("ContainerPrompt");
        Canvas canvas = promptUI.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 100;

        GameObject bg = new GameObject("BG");
        bg.transform.SetParent(promptUI.transform, false);
        Image bgImg = bg.AddComponent<Image>();
        bgImg.color = new Color(0, 0, 0, 0.5f);
        RectTransform bgRect = bgImg.GetComponent<RectTransform>();
        bgRect.anchorMin = new Vector2(0.5f, 0f);
        bgRect.anchorMax = new Vector2(0.5f, 0f);
        bgRect.pivot = new Vector2(0.5f, 0.5f);
        bgRect.anchoredPosition = new Vector2(0, 50f);
        bgRect.sizeDelta = new Vector2(300, 50);

        GameObject textGO = new GameObject("Text");
        textGO.transform.SetParent(bg.transform, false);
        promptTextUI = textGO.AddComponent<Text>();
        promptTextUI.text = (type == ContainerType.Cabinet ? "按F打开柜子" : "按F打开箱子");
        promptTextUI.fontSize = 20;
        promptTextUI.color = Color.white;
        promptTextUI.alignment = TextAnchor.MiddleCenter;
        promptTextUI.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        RectTransform textRect = promptTextUI.GetComponent<RectTransform>();
        textRect.anchorMin = Vector2.zero;
        textRect.anchorMax = Vector2.one;
        textRect.offsetMin = Vector2.zero;
        textRect.offsetMax = Vector2.zero;

        promptUI.SetActive(false);
    }

    private void OnDestroy()
    {
        if (myHealth != null) myHealth.OnDeath -= Break;
        if (promptUI != null) Destroy(promptUI);
    }
}
