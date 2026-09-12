using UnityEngine;
using System.Collections;

/// <summary>
/// 轻量全局震屏（静态调用，不用挂任何物体）：CameraShaker.Shake(强度, 时长)。
/// 实现方式与 CarDriveIn 的过场震屏同款套路（直抖 Camera.main），但用"增量偏移"：
/// 每帧先扣掉自己上帧加的偏移得到真实基准位，再叠加新的随机偏移——
/// 兼容 Cinemachine/相机跟随（跟随照常走，抖动叠加在上面，结束不回跳）。
/// 多个震动请求并发时取更大强度、刷新时长（不叠加成抽风）。
/// 灭火器/爆炸/暴君脚步/扑击等都调这个。
/// </summary>
public class CameraShaker : MonoBehaviour
{
    private static CameraShaker instance;

    private float strength = 0f;      // 当前震动强度（世界单位幅度）
    private float timer = 0f;         // 已震时长
    private float duration = 0f;      // 总时长
    private bool shaking = false;
    private Vector3 lastOffset = Vector3.zero; // 上帧加的偏移（本帧先扣掉它）

    /// <summary> 触发一次震屏：strength = 幅度（世界单位，0.25 左右 = 明显微震），duration = 秒 </summary>
    public static void Shake(float strength, float duration)
    {
        if (strength <= 0f || duration <= 0f) return;

        if (instance == null)
        {
            GameObject go = new GameObject("CameraShaker");
            instance = go.AddComponent<CameraShaker>();
        }
        instance.Begin(strength, duration);
    }

    private void Begin(float newStrength, float newDuration)
    {
        // 并发请求：取更大强度（正在猛震时不被小震动削弱）；时长刷新
        strength = shaking ? Mathf.Max(strength, newStrength) : newStrength;
        duration = Mathf.Max(duration - timer, newDuration);
        timer = 0f;

        if (!shaking) StartCoroutine(ShakeRoutine());
    }

    private IEnumerator ShakeRoutine()
    {
        shaking = true;
        Camera cam = Camera.main;
        if (cam == null)
        {
            shaking = false;
            yield break;
        }

        while (timer < duration)
        {
            timer += Time.deltaTime;
            float k = Mathf.Clamp01(1f - timer / duration); // 线性衰减

            // 增量方案：当前相机位置 - 上帧偏移 = 跟随脚本写好的真实基准位
            Vector3 basePos = cam.transform.localPosition - lastOffset;
            float x = Random.Range(-1f, 1f) * strength * k;
            float y = Random.Range(-1f, 1f) * strength * k;
            lastOffset = new Vector3(x, y, 0f);
            cam.transform.localPosition = basePos + lastOffset;

            yield return null;
        }

        // 结束：扣掉最后帧偏移，回到跟随脚本的基准位（无回跳）
        cam.transform.localPosition -= lastOffset;
        lastOffset = Vector3.zero;
        strength = 0f;
        shaking = false;
    }
}
