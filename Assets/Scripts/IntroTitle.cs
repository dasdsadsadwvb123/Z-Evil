using UnityEngine;
using UnityEngine.UI;
using UnityEngine.SceneManagement;
using System.Collections;
using System.Collections.Generic;

/// <summary>
/// RE2/RE3 式开场（开屏）：背景+暴君整图 → 白字"点击开始" → 点击碎裂转场
/// → 暴君"活"过来（纯代码，不碰 Animator）→ 音乐渐弱 → 画面渐黑 → 进入游戏场景。
///
/// 挂到开场场景的一个空物体上。Inspector 拖引用：
///  - Tyrant Sprite：暴君+背景整图（SpriteRenderer）
///  - Bgm Source：音乐（进入即播）
///  - Click Sound / Roar Sound：你自己配的音效（可留空）
///  - Next Scene Name：进入哪个场景（默认 game1）
/// </summary>
public class IntroTitle : MonoBehaviour
{
    [Header("引用")]
    public SpriteRenderer tyrantSprite;      // 暴君 + 背景 整图
    public string nextSceneName = "game1";   // 开场结束后进入的场景

    [Header("音乐与音效（自己配）")]
    public AudioSource bgmSource;            // 音乐：进入即播放
    public AudioClip clickSound;             // 点击碎裂音效
    public AudioClip roarSound;              // 暴君吼叫音效（可选）
    public float musicFadeDuration = 5f;     // 音乐渐弱时长（秒）

    [Header("提示文字")]
    public string prompt = "点击游戏任意位置开始";
    public float promptPulseSpeed = 2.5f;    // 文字呼吸闪烁速度

    [Header("碎裂转场")]
    public int shardCount = 14;              // 碎裂块数
    public float shardFlyTime = 0.7f;        // 碎片飞散时长
    public float flashTime = 0.12f;          // 白色闪光时长
    public float shakeTime = 0.2f;           // 画面震动时长

    [Header("暴君动作（纯代码）")]
    public float breatheAmount = 0.025f;     // 呼吸起伏幅度
    public float approachScale = 1.22f;      // 逼近镜头的最终大小
    public float roarScale = 1.38f;          // 吼叫瞬间突进大小
    public float swayAmount = 0.035f;        // 逼近时左右晃动幅度
    public float motionSmooth = 0.8f;        // 动作平滑度（越大越柔缓，0.6~1.0 丝滑手感）

    [Header("渐黑")]
    public float fadeToBlackTime = 2f;       // 结尾渐黑时长

    private Text promptText;
    private Image whiteFlash;
    private Image blackOverlay;
    private RectTransform shardLayer;        // 碎片的容器
    private bool started = false;
    private Vector3 baseScale;               // 暴君原始大小
    private Vector3 basePos;                 // 暴君原始位置

    private void Start()
    {
        CreateUI();

        // 记录暴君初始状态（动画的起点）
        if (tyrantSprite != null)
        {
            baseScale = tyrantSprite.transform.localScale;
            basePos = tyrantSprite.transform.localPosition;
        }

        // 音乐进入即播
        if (bgmSource != null && !bgmSource.isPlaying)
            bgmSource.Play();
    }

    private void Update()
    {
        if (started) return;

        // 提示文字呼吸闪烁（纯代码 alpha 脉动）
        if (promptText != null)
        {
            float wave = 0.5f + 0.5f * Mathf.Sin(Time.time * promptPulseSpeed);
            Color c = promptText.color;
            c.a = Mathf.Lerp(0.4f, 1f, wave);
            promptText.color = c;
        }

        // 点击屏幕任意位置 → 开始
        if (Input.GetMouseButtonDown(0))
            BeginSequence();
    }

    // ======== 开场流程 ========

    private void BeginSequence()
    {
        started = true;
        StartCoroutine(Sequence());
    }

    private IEnumerator Sequence()
    {
        // 隐藏提示文字
        if (promptText != null) promptText.gameObject.SetActive(false);

        // 1. 点击音效 + 碎裂转场
        if (clickSound != null)
            AudioSource.PlayClipAtPoint(clickSound, Vector3.zero);
        yield return StartCoroutine(FragmentScreen());

        // 2. 音乐开始 5 秒渐弱
        StartCoroutine(FadeMusicOut());

        // 3. 暴君"活"过来
        yield return StartCoroutine(TyrantMotion());

        // 4. 画面渐黑
        yield return StartCoroutine(FadeToBlack());

        // 5. 进入游戏
        SceneManager.LoadScene(nextSceneName);
    }

    /// <summary> 碎裂转场：白闪 + 画面震动 + 碎片飞散 </summary>
    private IEnumerator FragmentScreen()
    {
        // --- 白闪 ---
        whiteFlash.gameObject.SetActive(true);
        float t = 0f;
        while (t < flashTime)
        {
            t += Time.unscaledDeltaTime;
            Color c = Color.white;
            c.a = 1f - t / flashTime;
            whiteFlash.color = c;
            yield return null;
        }
        whiteFlash.gameObject.SetActive(false);

        // --- 画面震动 ---
        if (tyrantSprite != null)
        {
            Vector3 original = tyrantSprite.transform.localPosition;
            float shakeEnd = Time.time + shakeTime;
            while (Time.time < shakeEnd)
            {
                Vector3 offset = new Vector3(Random.Range(-0.08f, 0.08f), Random.Range(-0.06f, 0.06f), 0f);
                tyrantSprite.transform.localPosition = original + offset;
                yield return null;
            }
            tyrantSprite.transform.localPosition = original;
        }

        // --- 碎片铺屏 + 飞散淡出 ---
        List<RectTransform> shards = new List<RectTransform>();
        for (int i = 0; i < shardCount; i++)
            shards.Add(CreateShard());

        float fly = 0f;
        while (fly < shardFlyTime)
        {
            fly += Time.unscaledDeltaTime;
            float p = fly / shardFlyTime; // 0~1 进度
            foreach (RectTransform s in shards)
            {
                if (!shardInfos.TryGetValue(s, out ShardInfo info)) continue;
                // 位置：沿记录的方向飞出去（加速）
                s.anchoredPosition = info.startPos + info.dir * (p * p * info.distance);
                // 旋转
                s.localRotation = Quaternion.Euler(0, 0, info.rotSpeed * fly);
                // 淡出
                Image img = s.GetComponent<Image>();
                if (img != null)
                {
                    Color c = img.color;
                    c.a = Mathf.Clamp01(1f - p * 1.4f);
                    img.color = c;
                }
            }
            yield return null;
        }

        foreach (RectTransform s in shards)
            if (s != null) Destroy(s.gameObject);
        shardInfos.Clear();
    }

    // 碎片数据（轻量结构体，不继承 MonoBehaviour，避免嵌套类不能挂载的坑）
    private struct ShardInfo
    {
        public Vector2 startPos;
        public Vector2 dir;
        public float distance;
        public float rotSpeed;
    }
    private Dictionary<RectTransform, ShardInfo> shardInfos = new Dictionary<RectTransform, ShardInfo>();

    /// <summary> 创建一个三角形碎片（覆盖整个屏幕的一块） </summary>
    private RectTransform CreateShard()
    {
        GameObject shardGO = new GameObject("Shard", typeof(RectTransform));
        shardGO.transform.SetParent(shardLayer, false);
        Image img = shardGO.AddComponent<Image>();
        img.sprite = CreateTriangleSprite(); // 三角形贴图
        img.color = Color.black;             // 深色碎片（遮住画面 = "裂开"）
        img.raycastTarget = false;

        RectTransform rt = shardGO.GetComponent<RectTransform>();
        // 让碎片初始铺满屏幕（中心为屏幕中心）
        rt.anchorMin = new Vector2(0.5f, 0.5f);
        rt.anchorMax = new Vector2(0.5f, 0.5f);
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.anchoredPosition = Vector2.zero;
        rt.sizeDelta = new Vector2(600, 600);

        // 记录飞出方向和距离（存字典，不 AddComponent）
        ShardInfo info = new ShardInfo();
        info.startPos = Vector2.zero;
        float angle = Random.Range(0f, 360f) * Mathf.Deg2Rad;
        info.dir = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)).normalized;
        info.distance = Random.Range(1200f, 2200f);
        info.rotSpeed = Random.Range(200f, 500f) * (Random.value > 0.5f ? 1f : -1f);
        shardInfos[rt] = info;

        // 初始随机旋转（拼成碎玻璃样）
        rt.localRotation = Quaternion.Euler(0, 0, Random.Range(0f, 360f));
        return rt;
    }

    /// <summary> 代码生成一个三角形贴图（碎片用） </summary>
    private Sprite CreateTriangleSprite()
    {
        int size = 128;
        Texture2D tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
        // 三角形三个顶点
        Vector2 a = new Vector2(0.08f, 0.08f);
        Vector2 b = new Vector2(0.92f, 0.15f);
        Vector2 c = new Vector2(0.35f, 0.92f);
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                Vector2 p = new Vector2(x / (float)(size - 1), y / (float)(size - 1));
                tex.SetPixel(x, y, new Color(1f, 1f, 1f, PointInTriangle(p, a, b, c) ? 1f : 0f));
            }
        }
        tex.Apply();
        tex.wrapMode = TextureWrapMode.Clamp;
        return Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f));
    }

    private bool PointInTriangle(Vector2 p, Vector2 a, Vector2 b, Vector2 c)
    {
        float d1 = Sign(p, a, b), d2 = Sign(p, b, c), d3 = Sign(p, c, a);
        bool hasNeg = (d1 < 0) || (d2 < 0) || (d3 < 0);
        bool hasPos = (d1 > 0) || (d2 > 0) || (d3 > 0);
        return !(hasNeg && hasPos);
    }
    private float Sign(Vector2 p1, Vector2 p2, Vector2 p3)
    {
        return (p1.x - p3.x) * (p2.y - p3.y) - (p2.x - p3.x) * (p1.y - p3.y);
    }

    /// <summary> 音乐 5 秒渐弱（Volume 1 → 0） </summary>
    private IEnumerator FadeMusicOut()
    {
        if (bgmSource == null) yield break;
        float start = bgmSource.volume;
        float t = 0f;
        while (t < musicFadeDuration)
        {
            t += Time.deltaTime;
            bgmSource.volume = Mathf.Lerp(start, 0f, t / musicFadeDuration);
            yield return null;
        }
        bgmSource.volume = 0f;
    }

    /// <summary> 暴君"活"过来：呼吸 → 低头逼近 → 吼叫突进（纯代码，全程 SmoothDamp 平滑） </summary>
    private IEnumerator TyrantMotion()
    {
        if (tyrantSprite == null) yield break;

        // SmoothDamp 平滑核心：只管改 target，它自动顺滑地跟过去，任何阶段切换都无突变
        float scale = 1f;
        float scaleVel = 0f;   // SmoothDamp 内部速度缓存

        // 辅助：应用当前 scale 到 Sprite（考虑初始 baseScale）
        void ApplyScale()
        {
            tyrantSprite.transform.localScale = baseScale * scale;
        }

        // 1) 呼吸起伏（约 1.6 秒）：目标值微微上下呼吸（平滑用 motionSmooth，统一手感）
        float breatheEnd = Time.time + 1.6f;
        while (Time.time < breatheEnd)
        {
            float target = 1f + Mathf.Sin((Time.time - (breatheEnd - 1.6f)) * 6f) * breatheAmount;
            scale = Mathf.SmoothDamp(scale, target, ref scaleVel, motionSmooth);
            ApplyScale();
            yield return null;
        }

        // 2) 缓慢逼近镜头（约 2.5 秒）：目标值缓缓爬升到 approachScale，SmoothDamp 全程平滑
        float approach = 0f;
        float dur = 2.5f;
        while (approach < dur)
        {
            approach += Time.deltaTime;
            float p = approach / dur; // 0~1
            // 用 SmoothStep：两头缓、中间稍快——比之前的 p*p 顺滑得多
            float eased = Mathf.SmoothStep(0f, 1f, p);
            float target = 1f + (approachScale - 1f) * eased;

            // 左右晃动（叠加在 scale 之外，不影响平滑）
            float sway = Mathf.Sin(approach * 9f) * swayAmount * p;
            scale = Mathf.SmoothDamp(scale, target, ref scaleVel, motionSmooth);
            ApplyScale();
            tyrantSprite.transform.localPosition = basePos + new Vector3(sway, -p * 0.15f, 0f);
            yield return null;
        }

        // 4) 吼叫突进：目标平滑冲到 roarScale（不顿停，直接衔接更丝滑）
        if (roarSound != null)
            AudioSource.PlayClipAtPoint(roarSound, Vector3.zero);
        float roarEnd = Time.time + 0.5f;
        while (Time.time < roarEnd)
        {
            scale = Mathf.SmoothDamp(scale, roarScale, ref scaleVel, 0.3f); // 快但不冲
            ApplyScale();
            yield return null;
        }

        // 5) 定格 0.8 秒（让玩家看清"活了"的暴君）
        yield return new WaitForSeconds(0.8f);
    }

    /// <summary> 画面渐黑（2 秒） </summary>
    private IEnumerator FadeToBlack()
    {
        blackOverlay.gameObject.SetActive(true);
        float t = 0f;
        while (t < fadeToBlackTime)
        {
            t += Time.deltaTime;
            Color c = Color.black;
            c.a = Mathf.Clamp01(t / fadeToBlackTime);
            blackOverlay.color = c;
            yield return null;
        }
        blackOverlay.color = Color.black;
    }

    // ======== UI 创建 ========

    private void CreateUI()
    {
        GameObject canvasGO = new GameObject("IntroCanvas");
        Canvas canvas = canvasGO.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 100;
        canvasGO.AddComponent<CanvasScaler>().uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        canvasGO.AddComponent<GraphicRaycaster>();

        // 碎片容器（盖住画面用）
        shardLayer = new GameObject("ShardLayer", typeof(RectTransform)).GetComponent<RectTransform>();
        shardLayer.SetParent(canvasGO.transform, false);
        shardLayer.anchorMin = new Vector2(0.5f, 0.5f);
        shardLayer.anchorMax = new Vector2(0.5f, 0.5f);
        shardLayer.sizeDelta = new Vector2(0, 0);

        // 提示文字（屏幕下方中央，白色）
        GameObject promptGO = new GameObject("PromptText", typeof(RectTransform));
        promptGO.transform.SetParent(canvasGO.transform, false);
        promptText = promptGO.AddComponent<Text>();
        promptText.text = prompt;
        promptText.fontSize = 30;
        promptText.color = Color.white;
        promptText.alignment = TextAnchor.MiddleCenter;
        promptText.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        RectTransform pr = promptText.GetComponent<RectTransform>();
        pr.anchorMin = new Vector2(0.5f, 0f);
        pr.anchorMax = new Vector2(0.5f, 0f);
        pr.pivot = new Vector2(0.5f, 0.5f);
        pr.anchoredPosition = new Vector2(0f, 60f);
        pr.sizeDelta = new Vector2(900f, 60f);

        // 白色闪光层（碎裂瞬间闪白）
        GameObject flashGO = new GameObject("WhiteFlash", typeof(RectTransform));
        flashGO.transform.SetParent(canvasGO.transform, false);
        whiteFlash = flashGO.AddComponent<Image>();
        whiteFlash.color = new Color(1f, 1f, 1f, 0f);
        whiteFlash.raycastTarget = false;
        RectTransform fr = whiteFlash.GetComponent<RectTransform>();
        fr.anchorMin = Vector2.zero;
        fr.anchorMax = Vector2.one;
        fr.offsetMin = Vector2.zero;
        fr.offsetMax = Vector2.zero;
        flashGO.SetActive(false);

        // 黑色渐黑层（结尾转场用）
        GameObject blackGO = new GameObject("BlackOverlay", typeof(RectTransform));
        blackGO.transform.SetParent(canvasGO.transform, false);
        blackOverlay = blackGO.AddComponent<Image>();
        blackOverlay.color = new Color(0f, 0f, 0f, 0f);
        blackOverlay.raycastTarget = false;
        RectTransform br = blackOverlay.GetComponent<RectTransform>();
        br.anchorMin = Vector2.zero;
        br.anchorMax = Vector2.one;
        br.offsetMin = Vector2.zero;
        br.offsetMax = Vector2.zero;
        blackGO.SetActive(false);
    }
}
