using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 箱子系统（一个脚本两种类型，Inspector 下拉区分）：
/// - Cabinet（衣柜式）：无血量不会坏，靠近按 F 打开取物界面，点击箱内物品取出到背包。
/// - Breakable（可破坏式）：挂 HealthSystem（没有自动补）——刀/枪/舔食者都能打，血量归零破裂：
///   换破损贴图 + 破裂音效 + 内容物全部掉在地上变拾取物（参考密码机钥匙/乌鸦掉落套路）；
///   破裂前也能按 F 打开取物；破裂后不能再打开（碎了就是一地东西 + 废墟贴图）。
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

    [Header("音效（不拖 = 静音；走距离听声惯例）")]
    public AudioClip openClip;
    public AudioClip takeClip;   // 取出物品的声音
    public AudioClip breakClip;

    [Header("可破坏式专属")]
    [Tooltip("血量（只有 Breakable 且身上没挂 HealthSystem 时，自动补的这个值才生效）")]
    public int breakableHealth = 4;

    /// <summary> 箱子当前内容物（运行时列表；存的是完整物品条目，弹药/耐久状态原样保留） </summary>
    [HideInInspector] public List<InventoryItem> contents = new List<InventoryItem>();

    public bool IsBroken { get; private set; } = false;

    private SpriteRenderer sr;
    private Transform player;
    private HealthSystem myHealth;
    private GameObject promptUI;
    private Text promptTextUI;

    private void Start()
    {
        sr = GetComponent<SpriteRenderer>();

        // 初始物品 → 内容物（count 个 = count 条，和玩家背包"每格一件"规则一致）
        if (starterItems != null)
        {
            foreach (StarterEntry e in starterItems)
            {
                if (e == null || string.IsNullOrEmpty(e.itemID)) continue;
                int n = Mathf.Max(1, e.count);
                for (int i = 0; i < n; i++) contents.Add(CreateItem(e));
            }
        }

        // 找玩家（提示 UI + 距离检测用）
        GameObject p = GameObject.FindGameObjectWithTag("Player");
        if (p != null) player = p.transform;
        else Debug.LogWarning("[箱子] 场景里没有 Player 标签的物体，没人能打开我", gameObject);

        if (type == ContainerType.Breakable) SetupBreakable();
        CreatePromptUI();
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
            healAmount = e.healAmount
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
        myHealth.OnDeath += Break;

        // 破坏式箱子必须是实体碰撞体（子弹/刀的判定要能碰到它）
        BoxCollider2D col = GetComponent<BoxCollider2D>();
        if (col == null) col = gameObject.AddComponent<BoxCollider2D>();
        col.isTrigger = false;
    }

    private void Update()
    {
        if (IsBroken || player == null) return;
        if (ContainerUI.IsOpen) { if (promptUI != null) promptUI.SetActive(false); return; } // 界面开着时不重复响应 F（防"关闭同帧被重开"）

        bool near = Vector2.Distance(transform.position, player.position) <= interactRange;
        if (promptUI != null) promptUI.SetActive(near);

        if (near && Input.GetKeyDown(KeyCode.F))
        {
            AudibleAudio.PlayAt(openClip, transform.position);
            ContainerUI.Open(this); // 打开独立存取界面（自动暂停游戏）
        }
    }

    // ======== 内容物取出（ContainerUI 调用；玩家只能取不能放） ========

    /// <summary> 从箱子里取出第 index 件（箱子侧移除，交给背包） </summary>
    public InventoryItem TakeOut(int index)
    {
        if (index < 0 || index >= contents.Count) return null;
        InventoryItem item = contents[index];
        contents.RemoveAt(index);
        AudibleAudio.PlayAt(takeClip, transform.position);
        return item;
    }

    // ======== 破裂（HealthSystem 血量归零触发） ========

    private void Break()
    {
        if (IsBroken) return;
        IsBroken = true;

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

        // 碰撞体改 Trigger：废墟不挡路不挡子弹（可被打中会反复触发 TakeDamage？不会——isDead 后 HealthSystem 直接 return）
        BoxCollider2D col = GetComponent<BoxCollider2D>();
        if (col != null) col.isTrigger = true;
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
