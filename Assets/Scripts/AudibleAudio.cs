using UnityEngine;

/// <summary>
/// 全局"距离听声"系统 —— 项目音效惯例（小泽定的规矩：主角能看到/走到的地方，声音才能听到）。
///
/// 用法（以后所有"发生在世界某个位置"的音效都走这个入口，不要直接 PlayOneShot）：
///     AudibleAudio.PlayAt(clip, transform.position);          // 全音量版
///     AudibleAudio.PlayAt(clip, transform.position, 0.8f);    // 传入音量（会被距离衰减再叠加）
///
/// 原理：在指定位置生成一个临时 AudioSource（spatialBlend=1 纯 3D 音效 + Linear 线性衰减），
/// 两段式听声：≤ minDistance 恒定全音量、min~max 之间线性渐变（走远渐弱/走近渐强，双向自动生效）、
/// ≥ maxDistance 完全听不见，播完自动销毁。
///
/// 全局可调参数：场景里会自动生成一个"AudibleAudioSettings"常驻物体（不用手动建），
/// 选中它就能在 Inspector 里调 Max Distance（默认 16 格）和 Min Distance（全音量核心圈）。
///
/// 不接入的例外（它们已有自己的声音系统，别打架）：
/// - 电锯哥/舔食者等敌人的音效（BossSawAI/LickerAI 自带距离淡出/音调系统）
/// - 玩家自己身上播的音效（枪声/挥刀：距离恒 0，本来就全音量）
/// </summary>
[DisallowMultipleComponent]
public class AudibleAudio : MonoBehaviour
{
    // ---- 全局设置（自动生成的常驻物体上可调） ----

    [Tooltip("声音最大可闻距离（世界单位，本游戏 1 = 1 格）：超过这个距离完全听不见")]
    [Range(1f, 50f)]
    public float maxDistance = 16f; // 默认 16 格（小泽定稿：12 太小听不清邻房动静）

    [Tooltip("全音量核心圈半径（世界单位）：这个圈内恒定全音量，圈外到 Max Distance 之间线性渐变（两段式听声：12 内全音量 / 12~16 渐变 / 16 外无声）")]
    [Range(0.1f, 50f)]
    public float minDistance = 12f; // 默认 12 格（小泽定稿两段式：12 格内纯音量，12~16 由远及近渐强）

    /// <summary> 全局单例：懒加载，第一次播声音时自动生成，跨场景常驻 </summary>
    private static AudibleAudio instance;

    /// <summary> 全局"全音量核心圈"半径（供需要自配 3D 衰减的循环音源读取，保证与项目听声手感统一） </summary>
    public static float MinDistance { get { EnsureSettings(); return instance.minDistance; } }

    /// <summary> 全局"最大可闻距离"（同上，供循环音源对齐项目的两段式听声规则） </summary>
    public static float MaxDistance { get { EnsureSettings(); return instance.maxDistance; } }

    /// <summary> 确保设置物体存在（没有就自动建一个，不用手动搭建） </summary>
    private static void EnsureSettings()
    {
        if (instance != null) return;

        // 场景里可能已经手动建了一个（挂着本脚本的物体）——找到就用它
        instance = FindObjectOfType<AudibleAudio>();
        if (instance != null) return;

        // 没有 → 自动生成常驻设置物体（Hierarchy 里可见，选中即可调参数）
        GameObject go = new GameObject("AudibleAudioSettings");
        instance = go.AddComponent<AudibleAudio>();
        DontDestroyOnLoad(go); // 跨场景常驻，设置不用每个场景重配
        Debug.Log("[距离音效] 已自动生成全局设置物体 AudibleAudioSettings（Inspector 里可调最大可闻距离）", go);
    }

    /// <summary>
    /// 在指定位置播一段"距离听声"音效：近处全音量，越远越小，超过 Max Distance 完全听不见。
    /// 播完自动销毁临时音源物体，不留垃圾。
    /// </summary>
    /// <param name="clip">音效文件（null = 静默跳过，不报错）</param>
    /// <param name="position">声音发出的世界坐标（一般填 transform.position）</param>
    /// <param name="volume">基础音量（0~1），实际大小 = 基础音量 × 距离衰减</param>
    public static void PlayAt(AudioClip clip, Vector3 position, float volume = 1f)
    {
        if (clip == null) return; // 空槽静音惯例：不拖 clip 就不出声、不报错
        EnsureSettings();

        // 临时音源物体：位置 = 声音发出的地方
        GameObject go = new GameObject("临时音效_" + clip.name);
        go.transform.position = position;
        AudioSource src = go.AddComponent<AudioSource>();
        src.clip = clip;
        src.volume = Mathf.Clamp01(volume);
        src.playOnAwake = false;
        src.spatialBlend = 1f;                        // 纯 3D 音效：随距离衰减的前提
        src.rolloffMode = AudioRolloffMode.Linear;    // 线性衰减：远近变化直观好调
        src.minDistance = instance.minDistance;       // 这个圈内全音量
        src.maxDistance = instance.maxDistance;       // 这个圈外完全无声
        src.Play();

        // 播完 + 0.1 秒缓冲后销毁，不留垃圾
        Object.Destroy(go, clip.length + 0.1f);
    }
}
