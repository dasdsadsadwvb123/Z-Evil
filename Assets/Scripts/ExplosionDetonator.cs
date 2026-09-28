using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 爆破器（决战地图机关总闸，挂爆破器图物体上）：
/// 靠近按 F → 引爆全场/半径内的所有 ExplosiveBarrel 罐子（延迟 X 秒陆续炸）——伤害/僵直由罐子自己的参数管。
/// 也保留外部触发入口：TriggerExplosion() 可被 UnityEvent/触发器/剧情调用（双通道并存）。
/// </summary>
public class ExplosionDetonator : MonoBehaviour
{
    [Header("按 F 引爆（交互）")]
    [Tooltip("玩家离爆破器多近能按 F")]
    public float interactRange = 1.5f;

    [Header("引爆范围")]
    [Tooltip("只引爆这个半径内的罐子（世界单位）；0 = 全场景所有罐子一起炸")]
    public float triggerRadius = 0f;

    [Tooltip("引爆延迟（秒）：按 F → 等这么久才开炸（给玩家逃跑窗口）")]
    public float detonateDelay = 1f;
    [Tooltip("延迟随机浮动（秒）：每颗罐子实际延迟 = 引爆延迟 ± 这个浮动——罐子陆续炸更有演出感")]
    public float fuseVariance = 0.3f;

    private bool firing = false;   // 防重复触发（延迟期间再按不叠加）
    private bool used = false;     // 引爆过一次后永久安静

    private Transform player;
    private GameObject promptUI;   // "按F 引爆罐子"提示
    private Text promptTextUI;

    private void Start()
    {
        GameObject p = GameObject.FindGameObjectWithTag("Player");
        if (p != null) player = p.transform;
        CreatePromptUI();
    }

    private void Update()
    {
        if (used || firing || player == null) return;

        bool near = Vector2.Distance(transform.position, player.position) <= interactRange;
        if (promptUI != null) promptUI.SetActive(near);

        if (near && WorldInteractionBlocker.GetKeyDown(KeyCode.F))
        {
            TriggerExplosion();
        }
    }

    /// <summary> 引爆！（按 F / UnityEvent / 代码调用入口） </summary>
    public void TriggerExplosion()
    {
        if (firing || used) return;
        firing = true;
        used = true;
        if (promptUI != null) promptUI.SetActive(false); // 已引爆，提示收掉

        DoExplode();
    }

    /// <summary>
    /// 给每颗罐子排独立引信：实际延迟 = detonateDelay ± fuseVariance（随机浮动，陆续炸）。
    /// </summary>
    private void DoExplode()
    {
        int count = 0;
        foreach (ExplosiveBarrel barrel in FindObjectsOfType<ExplosiveBarrel>())
        {
            if (barrel.Exploded) continue; // 炸过的跳过
            if (triggerRadius > 0f && Vector2.Distance(barrel.transform.position, transform.position) > triggerRadius)
                continue; // 半径外跳过

            // 每颗罐子独立延迟：基础延迟 ± 随机浮动（不低于 0），陆续爆炸
            float delay = Mathf.Max(0f, detonateDelay + Random.Range(-fuseVariance, fuseVariance));
            barrel.ExplodeDelayed(delay);
            count++;
        }
        Debug.Log("[爆破器] 点燃了 " + count + " 个罐子的引信（基础延迟 " + detonateDelay.ToString("F1") + "s ± " + fuseVariance.ToString("F1") + "s）", gameObject);
    }

    // ======== 提示 UI（"按F 引爆罐子"，轻量套路同款） ========
    private void CreatePromptUI()
    {
        promptUI = new GameObject("DetonatorPrompt");
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
        promptTextUI.text = "按F 引爆罐子";
        promptTextUI.fontSize = 20;
        promptTextUI.color = new Color(1f, 0.6f, 0.4f); // 橙红字，一看就知道是危险按钮
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
