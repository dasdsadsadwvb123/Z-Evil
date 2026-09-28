using System;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 过敏名单——配药板拖线配对谜题（挂配药板/药柜物体上，除颤仪/密码箱同款交互套路）：
/// 靠近按 F → 打开 AllergyMatchUI：左列 5 支药剂、右列 5 张病床名牌（301~305），
/// 玩家按顶部"配伍禁忌表"的排除式线索拖线配对 → 5 对连满自动判定 → 全对咔哒开锁掉钥匙卡。
/// 唯一解在 Inspector 可配（默认 = 小泽拍板的答案：302↔荧光绿 / 303↔琥珀 / 304↔小瓶 / 301↔白瓶 / 305↔青霉素瓶）。
/// 界面由 AllergyMatchUI 负责（运行时自动生成，不用手动挂）；解开一次永久安静；读档重置。
/// 音效走 AudibleAudio.PlayAt 距离听声惯例（不拖 successClip = 静音开锁）。
/// </summary>
public class AllergyMatchBoard : MonoBehaviour
{
    [Serializable]
    public class DrugEntry
    {
        [Tooltip("药剂名（左列卡片标签）")]
        public string drugName;
        [Tooltip("瓶身颜色（属性暗示：荧光绿/琥珀/小瓶蓝/白/青霉素粉——配合禁忌表线索）")]
        public Color bottleColor;
    }

    [Header("药剂配置（左列 5 支，顺序 = 下标 0~4；颜色就是配伍线索的暗示）")]
    public DrugEntry[] drugs = new DrugEntry[5]
    {
        new DrugEntry { drugName = "荧光绿标记瓶", bottleColor = new Color(0.20f, 1f, 0.45f) },
        new DrugEntry { drugName = "琥珀色瓶",     bottleColor = new Color(0.95f, 0.65f, 0.10f) },
        new DrugEntry { drugName = "儿科小瓶",     bottleColor = new Color(0.55f, 0.85f, 1f)   },
        new DrugEntry { drugName = "白瓶",         bottleColor = new Color(0.92f, 0.92f, 0.88f) },
        new DrugEntry { drugName = "青霉素瓶",     bottleColor = new Color(1f, 0.72f, 0.80f)   },
    };

    [Header("唯一解（第 i 支药连到哪张床：0=301 床 1=302 床 2=303 床 3=304 床 4=305 床）")]
    [Tooltip("默认答案（和禁忌表线索一致）：白瓶→301（青霉素过敏排除）、荧光绿→302（夜间用药）、琥珀→303（避光）、小瓶→304（儿科减半）、青霉素瓶→305（排除法剩下）")]
    public int[] correctBedIndex = { 1, 2, 3, 0, 4 };

    [Header("配伍禁忌表（面板顶部显示的排除式线索；文案可改）")]
    [TextArea(4, 8)]
    public string clueText = "【配伍禁忌表】\n· 302 床只在夜间用药（认准荧光绿标记）\n· 303 床的药必须避光（琥珀色瓶）\n· 304 是儿科减半剂量（小瓶）\n· 301 床对青霉素过敏（白瓶那支他不能用）";

    [Header("306 床暗线（灰字手写体，纯叙事不影响判定）")]
    [TextArea(2, 4)]
    public string secretText = "306 床的药……没人敢配。他自己配的。配完第二天，整层楼都变了。";

    [Header("钥匙卡奖励（对接现有钥匙-传送器系统）")]
    [Tooltip("钥匙卡 itemID：填进 KeyDatabase 对应卡槽的 Key ID，传送器勾需要钥匙选那个卡槽")]
    public string keyItemID = "AllergyKeyCard";
    [Tooltip("钥匙卡显示名（拾取 toast / 背包里显示）")]
    public string keyItemName = "钥匙卡";
    [Tooltip("钥匙卡图标（可选；不拖 = 掉在地上的卡没有图但能捡）")]
    public Sprite keyIcon;
    [Tooltip("钥匙卡掉落位置 = 配药板位置 + 这个偏移（默认板前偏下）")]
    public Vector2 spawnOffset = new Vector2(0f, -1.2f);

    [Header("音效（不拖 = 静音开锁，走距离听声惯例）")]
    [Tooltip("全部配对成功的'咔哒'开锁声")]
    public AudioClip successClip;

    [Header("交互")]
    public float interactRange = 1.5f;

    /// <summary> 是否已解开（解开一次后永久安静；读档重置） </summary>
    public bool Solved { get; private set; } = false;

    private Transform player;
    private GameObject promptUI;
    private Text promptTextUI;

    private void Start()
    {
        GameObject p = GameObject.FindGameObjectWithTag("Player");
        if (p != null) player = p.transform;

        CreatePromptUI();
    }

    private void Update()
    {
        if (Solved || player == null) return;
        if (AllergyMatchUI.IsOpen) { if (promptUI != null) promptUI.SetActive(false); return; }
        if (Time.frameCount == AllergyMatchUI.LastClosedFrame) return; // 关闭当帧的 F 不算（防"关了又被同帧重开"）

        bool near = Vector2.Distance(transform.position, player.position) <= interactRange;
        if (promptUI != null)
        {
            promptUI.SetActive(near);
            if (promptTextUI != null) promptTextUI.text = "按F 检查配药板";
        }

        if (near && WorldInteractionBlocker.GetKeyDown(KeyCode.F))
        {
            AllergyMatchUI.Open(this); // 打开配对谜题（界面自己管暂停/拖线/判定）
        }
    }

    /// <summary> 播开锁声（距离听声惯例；没拖 = 静音） </summary>
    public void PlaySuccess()
    {
        if (successClip != null) AudibleAudio.PlayAt(successClip, transform.position);
    }

    /// <summary> 谜题通关（AllergyMatchUI 回调）：开锁声 + 钥匙卡掉在板前 </summary>
    public void SolveSuccess()
    {
        if (Solved) return;
        Solved = true;

        PlaySuccess();

        // 钥匙卡掉在板前（代码生成 PickupItem，密码机钥匙/除颤仪钥匙卡同源套路）
        GameObject go = new GameObject("掉落_" + keyItemName);
        go.transform.position = (Vector2)transform.position + spawnOffset;
        SpriteRenderer dropSr = go.AddComponent<SpriteRenderer>();
        dropSr.sprite = keyIcon;
        dropSr.sortingOrder = 10;
        PickupItem drop = go.AddComponent<PickupItem>();
        drop.itemID = keyItemID;
        drop.itemName = keyItemName;
        drop.itemType = ItemType.Key;
        drop.isKeyItem = true; // 关键物品金色闪烁引导（和密码机钥匙同款）
        drop.icon = keyIcon;
        drop.description = "配药柜吐出来的卡。也许能打开某扇门。";
        drop.destroyOnPickup = true;
        Debug.Log("[过敏名单] 通关！钥匙卡已掉落：" + keyItemID, gameObject);

        if (promptUI != null) promptUI.SetActive(false);
    }

    // ======== 提示 UI（除颤仪同款轻量套路） ========
    private void CreatePromptUI()
    {
        promptUI = new GameObject("AllergyPrompt");
        Canvas canvas = promptUI.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 100;
        UIScale.Setup(promptUI);
        promptUI.AddComponent<GraphicRaycaster>();

        GameObject bg = new GameObject("BG");
        bg.transform.SetParent(promptUI.transform, false);
        Image bgImg = bg.AddComponent<Image>();
        bgImg.color = new Color(0, 0, 0, 0.5f);
        RectTransform bgRect = bgImg.GetComponent<RectTransform>();
        bgRect.anchorMin = new Vector2(0.5f, 0f);
        bgRect.anchorMax = new Vector2(0.5f, 0f);
        bgRect.pivot = new Vector2(0.5f, 0.5f);
        bgRect.anchoredPosition = new Vector2(0, 50f);
        bgRect.sizeDelta = new Vector2(360, 50);

        GameObject textGO = new GameObject("Text");
        textGO.transform.SetParent(bg.transform, false);
        promptTextUI = textGO.AddComponent<Text>();
        promptTextUI.text = "按F 检查配药板";
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
        if (promptUI != null) Destroy(promptUI);
    }
}
