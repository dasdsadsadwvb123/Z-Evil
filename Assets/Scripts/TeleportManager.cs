using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

public class TeleportManager : MonoBehaviour
{
    public static TeleportManager Instance;

    /// <summary> 任意传送阵使用时广播（宝石伏击系统订阅用）：Portal 同场景/跨场景传送都会先调 SetSpawnPosition，在此统一广播 </summary>
    public static System.Action OnAnyTeleport;

    /// <summary> 传送是否进行中（= 转场进行中）。Portal 用它防"连按 F 重复传送"。锁源是 FadeController 的转场状态。 </summary>
    public static bool IsTeleporting { get { return FadeController.IsTransitioning; } }

    private Vector2 spawnPosition;
    private bool hasSpawnData = false;

    // 兜底诊断用（每次场景加载复位）：本次加载有没有人成功拿到过 spawn；缺 spawn 的警告是否已报过
    private static bool spawnServedThisLoad = false;
    private static bool noSpawnWarnedThisLoad = false;

    private void Awake()
    {
        if (Instance == null)
        {
            Instance = this;
            DontDestroyOnLoad(gameObject);
            // 注册场景加载完成事件
            SceneManager.sceneLoaded += OnSceneLoaded;
            Debug.Log("TeleportManager 创建成功");
        }
        else
        {
            Destroy(gameObject);
        }
    }

    private void OnDestroy()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
    }

    // ★ 每次场景加载完成后自动执行 ★
    private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        // 复位兜底诊断标记（本次加载重新计数）
        spawnServedThisLoad = false;
        noSpawnWarnedThisLoad = false;

        Debug.Log("场景加载完成：" + scene.name + "，开始修复障碍物...");
        FixAllObstacles();
    }

    // ★ 修复所有障碍物 ★
    private void FixAllObstacles()
    {
        // 方法1：通过名称查找 Collision 父物体
        GameObject collisionParent = GameObject.Find("Collision");
        if (collisionParent == null)
        {
            Debug.LogWarning("没有找到 Collision 物体");
            return;
        }

        int fixCount = 0;

        // 遍历所有子物体
        foreach (Transform child in collisionParent.transform)
        {
            // 强制重新激活物体
            child.gameObject.SetActive(false);
            child.gameObject.SetActive(true);

            // 修复所有 Collider
            Collider2D[] colliders = child.GetComponents<Collider2D>();
            foreach (var col in colliders)
            {
                col.enabled = false;
                col.enabled = true;
            }

            fixCount++;
        }

        Debug.Log("修复了 " + fixCount + " 个障碍物");
    }

    public void SetSpawnPosition(Vector2 pos)
    {
        spawnPosition = pos;
        hasSpawnData = true;
        OnAnyTeleport?.Invoke(); // 广播"有传送发生"（伏击系统等订阅者用）
        Debug.Log("保存传送位置：" + pos);
    }

    public bool TryGetSpawnPosition(out Vector2 pos)
    {
        if (hasSpawnData)
        {
            pos = spawnPosition;
            hasSpawnData = false;
            spawnServedThisLoad = true; // 本次加载有人成功拿到 spawn（用于兜底诊断去重，避免第二次询问误报）
            Debug.Log("读取传送位置：" + pos);
            return true;
        }

        pos = Vector2.zero;

        // 兜底诊断（每场景只报一次）：明明正在传送，本届加载却没人拿到 spawn 数据
        // → 十有八九是重复传送请求（快速连按 F）把场景/数据搞乱了，这里明确喊出来，别再静默落默认出生点。
        if (IsTeleporting && !spawnServedThisLoad && !noSpawnWarnedThisLoad)
        {
            noSpawnWarnedThisLoad = true;
            Debug.LogWarning("[传送] 没有收到 spawn 数据，玩家落到默认出生点——请检查是否发生了重复的传送请求（快速连按 F）或目标 Portal 的 spawnPosition 未配置。");
        }

        Debug.Log("没有传送数据");
        return false;
    }
}