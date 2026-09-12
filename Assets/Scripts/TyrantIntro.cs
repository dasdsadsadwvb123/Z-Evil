using UnityEngine;
using UnityEngine.Events;
using System.Collections;

/// <summary>
/// 暴君出场演出（挂实验箱物体上，纯代码）：
/// 玩家踩上触发区（Collider2D 勾 IsTrigger）→ 一次性触发：
/// ① 实验箱闪烁几下（原色↔闪烁色交替，每次闪可配 tick 音）→ ② 破箱音效
/// → ③ 激活暴君物体（小泽把场景里的暴君摆好但【取消勾选激活】，出场时这里 SetActive(true)，
///    TyrantAI 的 Start 这时才跑，组件缓存/玩家查找/动画启动自然开始，零衔接问题）
///    → 位置 = 实验箱位置 + 出场偏移（默认箱前一小格）
/// → ④ Boss 战音乐响起（独立音源 loop 循环，恒定音量不走距离听声）。
/// 【三段式音乐】第一次打空血假死（onFakeDeath）→ 音乐渐停；玩家靠近暴起（onLungeStart，伴随惊吓音）
/// → 音乐重新渐入响起；扑击结束真死（onTyrantDeath）→ 音乐再渐停。
/// 演出协程用 WaitForSeconds（暂停时演出也冻结防穿帮，和倒计时同语义）；
/// 演出期间玩家可自由移动；重复触发防护。
/// </summary>
public class TyrantIntro : MonoBehaviour
{
    [Header("暴君引用（重要）")]
    [Tooltip("暴君物体：场景里摆好位置但【取消勾选激活】——出场时这里激活它")]
    public GameObject tyrantObject;
    [Tooltip("暴君出场位置 = 实验箱位置 + 这个偏移（默认箱前偏下一小格）")]
    public Vector2 tyrantSpawnOffset = new Vector2(0f, -1f);

    [Header("实验箱闪烁")]
    [Tooltip("闪烁次数")]
    public int flashCount = 5;
    [Tooltip("闪烁间隔（秒）")]
    public float flashInterval = 0.25f;
    [Tooltip("闪烁色（和箱子原色交替）")]
    public Color flashColor = new Color(1f, 0.4f, 0.15f);
    [Tooltip("每次闪的提示音（不拖 = 静音，走距离听声惯例）")]
    public AudioClip tickClip;
    [Tooltip("破箱/玻璃碎音效（闪烁完播；不拖 = 静音）")]
    public AudioClip revealClip;
    [Tooltip("破箱后实验箱换的图（残箱图；不拖 = 保持原样）")]
    public Sprite boxAfterSprite;

    [Header("Boss 战音乐（循环）")]
    [Tooltip("打 Boss 的音乐（循环播放；和倒计时的一次性音乐是两回事）")]
    public AudioClip bossMusicClip;
    [Tooltip("Boss 音乐音量")]
    [Range(0f, 1f)]
    public float bossMusicVolume = 1f;
    [Tooltip("Boss 音乐渐弱时长（秒）：暴君死亡后音乐从当前音量线性降到 0 再停；0 = 立即停")]
    public float musicFadeDuration = 2f;
    [Tooltip("Boss 音乐【重启渐入】时长（秒）：暴起时音乐重新响起，从 0 渐到满音量；0 = 立刻满音量")]
    public float musicFadeInDuration = 0.5f;

    [Header("交互")]
    [Tooltip("触发区尺寸（自动补碰撞体时用；已有碰撞体则以场景里的为准）")]
    public Vector2 triggerSize = new Vector2(1.5f, 1.5f);

    private bool isTriggered = false; // 一次性触发标记
    private AudioSource musicSource;  // Boss 音乐专用（循环 + 恒定音量）
    private TyrantAI tyrantAI;        // 订阅死亡事件停音乐
    private Coroutine musicFadeRoutine; // 音乐渐弱协程（防重复调用）

    private void Start()
    {
        // 自动补触发碰撞体（防呆：忘加也能用）
        Collider2D col = GetComponent<Collider2D>();
        if (col == null)
        {
            BoxCollider2D box = gameObject.AddComponent<BoxCollider2D>();
            box.isTrigger = true;
            box.size = triggerSize;
        }
        else
        {
            col.isTrigger = true;
        }

        // Boss 音乐专用音源（独立常量音量，spatialBlend=0 不吃距离衰减；先不播，演出时才响）
        musicSource = GetComponent<AudioSource>();
        if (musicSource == null) musicSource = gameObject.AddComponent<AudioSource>();
        musicSource.playOnAwake = false;
        musicSource.loop = true;
        musicSource.spatialBlend = 0f;

        // 订阅暴君死亡 → 停 Boss 音乐（暴君物体未激活也能拿到组件，提前接好）
        if (tyrantObject != null)
        {
            tyrantAI = tyrantObject.GetComponent<TyrantAI>();
            if (tyrantAI != null)
            {
                tyrantAI.onFakeDeath.AddListener(StopBossMusic);     // 第一次打空血（假死倒下）→ 音乐渐停
                tyrantAI.onTyrantDeath.AddListener(StopBossMusic);   // 真死 → 音乐再渐停
                tyrantAI.onLungeStart.AddListener(RestartBossMusic); // 暴起 → 音乐重新响起
            }
            else
            {
                Debug.LogWarning("[暴君出场] 暴君物体上没有 TyrantAI——Boss 死后音乐不会自动停（检查引用）", gameObject);
            }
        }
        else
        {
            Debug.LogWarning("[暴君出场] 没拖暴君物体引用（Tyrant Object）——演出只闪箱不放人", gameObject);
        }
    }

    private void OnDestroy()
    {
        if (tyrantAI != null)
        {
            tyrantAI.onFakeDeath.RemoveListener(StopBossMusic);
            tyrantAI.onTyrantDeath.RemoveListener(StopBossMusic);
            tyrantAI.onLungeStart.RemoveListener(RestartBossMusic);
        }
    }

    private void OnTriggerEnter2D(Collider2D other)
    {
        if (isTriggered) return; // 一次性
        if (!other.CompareTag("Player")) return;

        isTriggered = true;
        StartCoroutine(IntroRoutine());
    }

    /// <summary> 出场演出：闪烁 → 破箱 → 放暴君 → Boss 音乐 </summary>
    private IEnumerator IntroRoutine()
    {
        // ---- ① 实验箱闪烁（原色 ↔ 闪烁色交替）----
        SpriteRenderer boxSr = GetComponent<SpriteRenderer>();
        Color originalColor = boxSr != null ? boxSr.color : Color.white;

        for (int i = 0; i < flashCount; i++)
        {
            if (boxSr != null) boxSr.color = flashColor;
            if (tickClip != null) AudibleAudio.PlayAt(tickClip, transform.position);
            yield return new WaitForSeconds(flashInterval);

            if (boxSr != null) boxSr.color = originalColor;
            yield return new WaitForSeconds(flashInterval);
        }

        // ---- ② 破箱音效 + 箱子换残图（可选） ----
        if (revealClip != null) AudibleAudio.PlayAt(revealClip, transform.position);
        if (boxAfterSprite != null && boxSr != null) boxSr.sprite = boxAfterSprite;

        // ---- ③ 激活暴君（TyrantAI.Start 这时才跑，AI/动画自然启动） ----
        if (tyrantObject != null)
        {
            tyrantObject.transform.position = (Vector2)transform.position + tyrantSpawnOffset;
            tyrantObject.SetActive(true);
            Debug.Log("[暴君出场] 暴君破箱而出！", gameObject);
        }

        // ---- ④ Boss 战音乐（循环，恒定音量） ----
        StartBossMusic();
    }

    private void StartBossMusic()
    {
        if (bossMusicClip == null) return;
        musicSource.clip = bossMusicClip;
        musicSource.volume = bossMusicVolume;
        musicSource.Play();
    }

    /// <summary> 暴君死亡（onTyrantDeath 回调）→ Boss 音乐渐弱到 0 再停 </summary>
    private void StopBossMusic()
    {
        if (musicSource == null) return;

        if (musicFadeRoutine != null) StopCoroutine(musicFadeRoutine); // 防重复调用叠加

        if (musicFadeDuration <= 0f)
        {
            musicSource.Stop();
            Debug.Log("[暴君出场] 暴君死亡，Boss 音乐停止（立即）", gameObject);
            return;
        }
        musicFadeRoutine = StartCoroutine(FadeOutMusicRoutine());
    }

    /// <summary> 在 musicFadeDuration 秒内把 Boss 音乐音量线性降到 0 再停（用 unscaledTime：暂停也不受影响） </summary>
    private IEnumerator FadeOutMusicRoutine()
    {
        float startVol = musicSource.volume;
        float t = 0f;
        while (t < musicFadeDuration && musicSource.isPlaying)
        {
            t += Time.unscaledDeltaTime;
            musicSource.volume = Mathf.Lerp(startVol, 0f, t / musicFadeDuration);
            yield return null;
        }
        musicSource.volume = 0f;
        musicSource.Stop();
        musicSource.volume = bossMusicVolume; // 音量复位（防复用）
        musicFadeRoutine = null;
        Debug.Log("[暴君出场] 暴君死亡，Boss 音乐渐弱结束（" + musicFadeDuration + " 秒）", gameObject);
    }

    /// <summary>
    /// 暴起（TyrantAI.onLungeStart 回调）→ Boss 音乐重新响起（从 0 渐入，或立刻满音量）。
    /// 先停掉正在进行的渐出/渐入协程，重复调用不叠加。
    /// </summary>
    public void RestartBossMusic()
    {
        if (musicSource == null || bossMusicClip == null) return; // 没拖音乐 = 静音不报错

        if (musicFadeRoutine != null) { StopCoroutine(musicFadeRoutine); musicFadeRoutine = null; } // 防叠加

        musicSource.clip = bossMusicClip;
        musicSource.volume = musicFadeInDuration > 0f ? 0f : bossMusicVolume;
        musicSource.Play(); // 从头播（暴起 = 音乐重启）

        if (musicFadeInDuration > 0f) musicFadeRoutine = StartCoroutine(FadeInMusicRoutine());
        Debug.Log("[暴君出场] 暴起！Boss 音乐重新响起", gameObject);
    }

    /// <summary> 在 musicFadeInDuration 秒内把 Boss 音乐音量从 0 升到 bossMusicVolume（用 unscaledTime） </summary>
    private IEnumerator FadeInMusicRoutine()
    {
        float t = 0f;
        while (t < musicFadeInDuration && musicSource.isPlaying)
        {
            t += Time.unscaledDeltaTime;
            musicSource.volume = Mathf.Lerp(0f, bossMusicVolume, t / musicFadeInDuration);
            yield return null;
        }
        musicSource.volume = bossMusicVolume;
        musicFadeRoutine = null;
    }
}
