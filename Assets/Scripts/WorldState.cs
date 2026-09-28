using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// 世界进度登记表（新增，零挂载——不用挂到任何物体上，纯静态工具）。
///
/// 作用：把"散落在一堆脚本里、以前进不了存档的运行时标记"（门开没开、密码箱解没解、
/// 宝石镶没镶、压力板触发没有、倒计时停没停、传送门解锁没有、怪死没死……）统一登记到一张表里。
/// 存档时整张表打包进 JSON；读档时整张表还原，各系统在 Start 里"自查"一下就能恢复状态。
///
/// 钥匙格式：类型_场景名_物体层级路径（例如 Door_大厅_门/Door01）。
/// 用"层级路径"而不是单纯物体名，是为了防止一个场景里有多个重名物体（比如好几个 Door）
/// 互相覆盖。同一父物体下仍重名的节点，按加载时的同名顺序加编号并缓存。
///
/// 值全部是 int：0/1 表示"没有/有"（GetBool），也能存 2、3（比如暴君 1=假死、2=真死）。
/// </summary>
public static class WorldState
{
    /// <summary> 存档里的一条记录（JsonUtility 存不了字典，所以用 List<Entry> 打包） </summary>
    [System.Serializable]
    public class Entry
    {
        public string key;
        public int value;
    }

    // 登记表本体：Key = 钥匙字符串，Value = 状态整数
    private static readonly Dictionary<string, int> map = new Dictionary<string, int>();
    private static readonly Dictionary<Transform, string> paths = new Dictionary<Transform, string>();

    // ======== 编辑器友好：每次进 Play 自动清空登记表 ========
    // （关闭"域重载"时，静态字段会残留上一次 Play 的数据；这里强制开局归零，杜绝串档）
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStaticsOnPlay()
    {
        map.Clear();
        paths.Clear();
        SceneManager.sceneLoaded -= CacheLoadedScene;
        SceneManager.sceneLoaded += CacheLoadedScene;
    }

    // ======== 钥匙生成 ========

    /// <summary> 拼钥匙：类型 + "_" + 场景名 + "_" + 物体名（底层用，不推荐直接调） </summary>
    public static string MakeKey(string type, string scene, string objName)
    {
        return type + "_" + scene + "_" + objName;
    }

    /// <summary>
    /// 推荐的钥匙生成入口：给一个组件（通常是 this），自动取它所在的场景 + 层级路径。
    /// 例：WorldState.KeyFor("Door", this) → "Door_大厅_门/Door01"
    /// </summary>
    public static string KeyFor(string type, Component comp)
    {
        if (comp == null) return type + "___";
        string scene = comp.gameObject.scene.name;
        string path = HierarchyPath(comp.transform);
        return type + "_" + scene + "_" + path;
    }

    /// <summary> 取物体的层级路径（父/子/孙），用于生成唯一钥匙 </summary>
    private static string HierarchyPath(Transform t)
    {
        if (!paths.ContainsKey(t)) CacheScenePaths(t.gameObject.scene);
        if (paths.TryGetValue(t, out string path)) return path;
        return t.name;
    }

    private static void CacheLoadedScene(Scene scene, LoadSceneMode mode)
    {
        var removed = new List<Transform>();
        foreach (var entry in paths)
            if (entry.Key == null) removed.Add(entry.Key);
        foreach (Transform t in removed) paths.Remove(t);

        // sceneLoaded 早于 Start：医疗箱/拾取物销毁之前固定全部编号。
        CacheScenePaths(scene);
    }

    private static void CacheScenePaths(Scene scene)
    {
        if (!scene.IsValid() || !scene.isLoaded) return;
        var roots = new List<Transform>();
        foreach (GameObject root in scene.GetRootGameObjects()) roots.Add(root.transform);
        CacheSiblings(roots, "");
    }

    private static void CacheSiblings(List<Transform> siblings, string parentPath)
    {
        var counts = new Dictionary<string, int>();
        var ordinals = new Dictionary<string, int>();
        foreach (Transform t in siblings)
            counts[t.name] = counts.TryGetValue(t.name, out int n) ? n + 1 : 1;

        foreach (Transform t in siblings)
        {
            int ordinal = ordinals.TryGetValue(t.name, out int n) ? n : 0;
            ordinals[t.name] = ordinal + 1;
            string segment = counts[t.name] > 1 ? t.name + "#" + ordinal : t.name;
            if (!paths.ContainsKey(t))
                paths[t] = parentPath + segment;

            var children = new List<Transform>();
            foreach (Transform child in t) children.Add(child);
            CacheSiblings(children, paths[t] + "/");
        }
    }

    // ======== 读写 ========

    /// <summary> 登记/更新一个状态 </summary>
    public static void Set(string key, int value)
    {
        if (string.IsNullOrEmpty(key)) return;
        map[key] = value;
    }

    /// <summary> 读状态（没有就返回 defaultValue，默认 0） </summary>
    public static int Get(string key, int defaultValue = 0)
    {
        if (string.IsNullOrEmpty(key)) return defaultValue;
        int v;
        return map.TryGetValue(key, out v) ? v : defaultValue;
    }

    /// <summary> 读成布尔（非 0 = true） </summary>
    public static bool GetBool(string key)
    {
        return Get(key, 0) != 0;
    }

    /// <summary> 有没有登记过这条（用来区分"没存过" 和 "存了 0"） </summary>
    public static bool Has(string key)
    {
        return !string.IsNullOrEmpty(key) && map.ContainsKey(key);
    }

    /// <summary> 删掉一条 </summary>
    public static void Remove(string key)
    {
        if (!string.IsNullOrEmpty(key)) map.Remove(key);
    }

    /// <summary> 当前登记条数（调试用） </summary>
    public static int Count { get { return map.Count; } }

    // ======== 存档/读档 ========

    /// <summary> 把整张表导出成可存 JSON 的列表（存档时调） </summary>
    public static List<Entry> Snapshot()
    {
        List<Entry> list = new List<Entry>(map.Count);
        foreach (KeyValuePair<string, int> kv in map)
            list.Add(new Entry { key = kv.Key, value = kv.Value });
        return list;
    }

    /// <summary> 用存档里的表覆盖当前表（读档时调；先清空再灌，精确还原） </summary>
    public static void Restore(List<Entry> snapshot)
    {
        map.Clear();
        if (snapshot == null) return;
        foreach (Entry e in snapshot)
        {
            if (e == null || string.IsNullOrEmpty(e.key)) continue;
            map[e.key] = e.value;
        }
    }

    /// <summary> 清空整张表（"从关卡开头重来"时调） </summary>
    public static void Clear()
    {
        map.Clear();
    }
}
