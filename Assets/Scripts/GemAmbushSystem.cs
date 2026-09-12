using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// 宝石伏击系统（追猎者锯子哥）：小泽确认的完整机制——
/// 1. 每捡一颗宝石 → 锯子哥刷新到"那颗宝石所在房间"的坐标（Inspector 填）+ 激活追击；
/// 2. 玩家使用任意传送阵 → 锯子哥刷新到大厅固定坐标 + 随机游走（不追）；
/// 3. 血量小泽在预制体上调（系统不管无敌）；被打死 → 后续所有触发永久失效；
/// 4. 单例铁律：全程有且只有一只锯子（刷新 = 移动同一实例，绝不生成第二只）；
/// 5. 跨场景：锯子 DontDestroyOnLoad 跨场景存活，玩家再传送时它会出现在大厅。
///
/// 首次伏击前锯子不存在（场景里看不见），第一次宝石拾取才实例化出现。
/// 宝石监听：轮询玩家背包 HasItem（从无到有的边沿触发，最可靠——拾取/读档/快照恢复全兼容）。
/// 传送监听：TeleportManager.OnAnyTeleport 静态事件（同场景/跨场景传送都广播）。
/// </summary>
public class GemAmbushSystem : MonoBehaviour
{
    public static GemAmbushSystem Instance { get; private set; }

    [Header("锯子哥实例")]
    [Tooltip("锯子哥预制体（把配好 BossSawAI/HealthSystem/Animator 的物体做成 Prefab 拖这里）；首次宝石被捡时实例化，之后永远移动同一只")]
    public BossSawAI sawPrefab;

    [Header("刷新坐标（每颗宝石各绑一个 + 大厅一个）")]
    [Tooltip("红宝石被捡时锯子刷新点")]
    public Vector2 rubyPos;
    [Tooltip("粉宝石被捡时锯子刷新点")]
    public Vector2 pinkPos;
    [Tooltip("橙宝石被捡时锯子刷新点")]
    public Vector2 orangePos;
    [Tooltip("大厅固定游荡点（玩家使用任意传送阵后锯子刷到这里随机游走）")]
    public Vector2 lobbyPos;

    [Header("宝石 itemID（必须和宝石拾取物/GemDoor 的 ID 一模一样，区分大小写）")]
    public string rubyID = "GemRuby";
    public string pinkID = "GemPink";
    public string orangeID = "GemOrange";

    [Header("主角独白（锯子哥每次伏击各一句话；复用现有 DialogueManager；-1 = 不说话）")]
    [Tooltip("捡到红宝石（第 1 次伏击）时播放的对话组编号；-1 = 不说话")]
    public int rubyLineGroup = -1;
    [Tooltip("捡到粉宝石（第 2 次伏击）时播放的对话组编号；-1 = 不说话")]
    public int pinkLineGroup = -1;
    [Tooltip("捡到橙宝石（第 3 次伏击）时播放的对话组编号；-1 = 不说话")]
    public int orangeLineGroup = -1;
    [Tooltip("播独白时是否冻结玩家（默认关：伏击时你还在被追，别冻死。勾了 = 说话期间不能动，说完自动解冻）")]
    public bool freezePlayerOnLine = false;

    // ---- 运行时状态 ----
    private BossSawAI saw;                 // 唯一的锯子实例（null = 还没出现）
    private bool sawDead = false;          // 锯子死了 → 全部触发永久失效
    private bool rubyTriggered = false;    // 每颗宝石只触发一次
    private bool pinkTriggered = false;
    private bool orangeTriggered = false;
    private bool lastHadRuby = false;      // 上一帧背包里有没有（边沿检测用）
    private bool lastHadPink = false;
    private bool lastHadOrange = false;
    private Inventory playerInventory;     // 玩家背包（懒查找：场景切换自动重找）
    private DialogueManager dialogueManager; // 主角独白用（懒查找，找不到就静默跳过）
    private PixelGridMovement playerMovement; // 冻结玩家用（只有 freezePlayerOnLine 勾上才用）

    // ---- 世界进度表钥匙（全局，不按场景分：锯子是全局唯一单例） ----
    private const string KeySawDead = "Ambush_SawDead";
    private const string KeyRuby = "Ambush_Ruby";
    private const string KeyPink = "Ambush_Pink";
    private const string KeyOrange = "Ambush_Orange";

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject); // 全局唯一：重复的自动销毁
            return;
        }
        Instance = this;
        DontDestroyOnLoad(gameObject); // 管理器跨场景常驻（锯子实例也由它带着跨场景）
    }

    private void Start()
    {
        // 传送广播：Portal 同场景/跨场景传送都会调 TeleportManager.SetSpawnPosition
        TeleportManager.OnAnyTeleport += OnAnyTeleport;
        // 场景加载：锯子死后尸体不再跟着去新场景（留在死亡场景自然消失）
        SceneManager.sceneLoaded += OnSceneLoaded;

        // 读档/跨场景自查：从世界进度表还原进度（锯子死没死、哪几颗宝石已触发过）
        RestoreFromWorldState();
    }

    private void OnDestroy()
    {
        TeleportManager.OnAnyTeleport -= OnAnyTeleport;
        SceneManager.sceneLoaded -= OnSceneLoaded;
    }

    // ======== 宝石拾取监听（每帧轮询背包，从无到有的边沿触发） ========

    private void Update()
    {
        if (sawDead) return; // 锯子死了：永久失效，什么都不再触发

        // 玩家背包懒查找：每场景的玩家是新实例 → 旧引用销毁后自动重找
        if (playerInventory == null)
        {
            GameObject p = GameObject.FindGameObjectWithTag("Player");
            if (p != null) playerInventory = p.GetComponent<Inventory>();
        }
        if (playerInventory == null) return;

        // 三颗宝石逐一做"从无到有"边沿检测（已触发过的不重复）
        bool had = playerInventory.HasItem(rubyID);
        if (had && !lastHadRuby && !rubyTriggered) { rubyTriggered = true; Ambush(rubyPos, "红宝石"); }
        lastHadRuby = had;

        had = playerInventory.HasItem(pinkID);
        if (had && !lastHadPink && !pinkTriggered) { pinkTriggered = true; Ambush(pinkPos, "粉宝石"); }
        lastHadPink = had;

        had = playerInventory.HasItem(orangeID);
        if (had && !lastHadOrange && !orangeTriggered) { orangeTriggered = true; Ambush(orangePos, "橙宝石"); }
        lastHadOrange = had;
    }

    /// <summary> 宝石伏击：锯子刷新到该宝石的房间坐标 + 激活追击 </summary>
    private void Ambush(Vector2 pos, string gemName)
    {
        BossSawAI s = EnsureSaw();
        if (s == null) return;
        s.RefreshAt(pos);      // 瞬移 + 巡逻中心重置 + 清仇恨回巡逻
        s.ActivateChase();     // 激活追击（永久仇恨）
        WorldState.Set(AmbushKeyFor(gemName), 1); // 世界进度表登记"这颗宝石已触发过伏击"
        Debug.Log("[宝石伏击] 玩家捡到" + gemName + "！锯子哥刷新到 " + pos + " 并开始追击", gameObject);

        // 锯子登场/开始追击的这一步 → 主角独白（复用 DialogueManager；不新建系统）
        PlayMonologue(gemName);
    }

    /// <summary> 按宝石名播放对应主角独白（组编号 &lt; 0 = 不说话；找不到 DialogueManager 静默跳过、不报错） </summary>
    private void PlayMonologue(string gemName)
    {
        int group = LineGroupFor(gemName);
        if (group < 0) return; // -1 = 不播（零漂移：不配置就跟原来一模一样）

        // 懒查找并缓存（场景切换后旧引用销毁会自动重找）
        if (dialogueManager == null) dialogueManager = FindObjectOfType<DialogueManager>();
        if (dialogueManager == null) return; // 场景里没有对话板 → 静默跳过

        if (freezePlayerOnLine)
        {
            if (playerMovement == null) playerMovement = FindObjectOfType<PixelGridMovement>();
            StartCoroutine(PlayLineAndRestore(group)); // 冻结 → 说话 → 说完自动解冻
        }
        else
        {
            dialogueManager.PlayGroup(group); // 默认：不冻结，边跑边说（伏击时你还在被追）
        }
    }

    /// <summary> 宝石名 → 对话组编号（-1 = 不播） </summary>
    private int LineGroupFor(string gemName)
    {
        if (gemName == "红宝石") return rubyLineGroup;
        if (gemName == "粉宝石") return pinkLineGroup;
        return orangeLineGroup;
    }

    /// <summary> 冻结玩家 → 播独白 → 播完解冻（仅 freezePlayerOnLine 勾上时走这条；复用 TriggerEvent 的冻结方式） </summary>
    private IEnumerator PlayLineAndRestore(int group)
    {
        bool froze = false;
        if (playerMovement != null)
        {
            playerMovement.frozen = true; // 与 TriggerEvent 同款：置 PixelGridMovement.frozen
            froze = true;
        }

        dialogueManager.PlayGroup(group);
        // 等这组独白播完（对话板被中途销毁也安全退出）
        yield return new WaitUntil(() => dialogueManager == null || !dialogueManager.isPlaying);

        if (froze && playerMovement != null) playerMovement.frozen = false;
    }

    /// <summary> 宝石名 → 世界进度表钥匙 </summary>
    private string AmbushKeyFor(string gemName)
    {
        if (gemName == "红宝石") return KeyRuby;
        if (gemName == "粉宝石") return KeyPink;
        return KeyOrange;
    }

    /// <summary> 任意传送阵使用：锯子刷新到大厅固定点，随机游走（不追） </summary>
    private void OnAnyTeleport()
    {
        if (sawDead) return;
        if (saw == null) return; // 还没登场过就传送 → 没有锯子可刷，跳过
        saw.RefreshAt(lobbyPos); // 游走 = 现有巡逻逻辑在新中心跑（RefreshAt 已清仇恨回巡逻）
        Debug.Log("[宝石伏击] 玩家使用了传送阵，锯子哥回大厅游荡：" + lobbyPos, gameObject);
    }

    // ======== 锯子实例管理（单例铁律） ========

    /// <summary> 确保唯一的锯子实例存在（没有就实例化预制体；绝不生成第二只） </summary>
    private BossSawAI EnsureSaw()
    {
        if (sawDead) return null;
        if (saw != null) return saw;

        if (sawPrefab == null)
        {
            Debug.LogWarning("[宝石伏击] 没配锯子哥预制体（Saw Prefab），伏击不生效", gameObject);
            return null;
        }

        GameObject go = Instantiate(sawPrefab.gameObject);
        go.name = "SawHunter"; // 场景里一眼认出这只由系统管理
        DontDestroyOnLoad(go); // 跨场景存活（活着时跟着玩家走）
        saw = go.GetComponent<BossSawAI>();

        // 监听死亡：死了 → 后续所有触发永久失效
        HealthSystem hp = go.GetComponent<HealthSystem>();
        if (hp != null) hp.OnDeath += OnSawDead;
        else Debug.LogWarning("[宝石伏击] 锯子预制体身上没有 HealthSystem，死亡失效机制不生效", go);

        Debug.Log("[宝石伏击] 锯子哥首次登场！", go);
        return saw;
    }

    /// <summary> 锯子被打死：全部触发永久失效；尸体留在死亡场景（切场景时销毁，不再跟随） </summary>
    private void OnSawDead()
    {
        sawDead = true;
        WorldState.Set(KeySawDead, 1); // 世界进度表登记"锯子已死"（读档后也不复活）
        Debug.Log("[宝石伏击] 锯子哥已死——宝石/传送伏击全部永久失效", gameObject);
    }

    /// <summary> 场景加载：锯子已死 → 尸体不再跨场景（销毁，玩家看不到新场景里有尸体）；并还原进度表 </summary>
    private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        // 先按世界进度表还原（读档进来后 WorldState 已由 SaveSystem.Awake 恢复）
        RestoreFromWorldState();
    }

    /// <summary> 从世界进度表还原伏击进度（Start 和每次切场景时调）：锯子死没死 + 哪几颗宝石已触发过 </summary>
    private void RestoreFromWorldState()
    {
        sawDead = WorldState.GetBool(KeySawDead);
        rubyTriggered = WorldState.GetBool(KeyRuby);
        pinkTriggered = WorldState.GetBool(KeyPink);
        orangeTriggered = WorldState.GetBool(KeyOrange);

        // 已死 → 尸体不再跨场景（销毁）
        if (sawDead && saw != null)
        {
            Destroy(saw.gameObject);
            saw = null;
        }
    }
}
