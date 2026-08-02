using UnityEngine;
using System.Collections;
using Cinemachine;

public class CarDriveIn : MonoBehaviour
{
    [Header("移动设置")]
    public Vector3 startPosition;//检查测试
    public Vector3 stopPosition;
    public float driveSpeed = 5f;

    [Header("变成障碍物")]
    public GameObject obstacleVersion;

    [Header("音效")]
    public AudioSource driveSound;
    public AudioSource explosionSound;

    [Header("爆炸特效")]
    public GameObject explosionEffect;

    [Header("镜头设置")]
    [Tooltip("拖入你的 Cinemachine Virtual Camera，正常工作的那个")]
    public CinemachineVirtualCamera virtualCamera;

    private bool isDriving = false;
    private PixelGridMovement player;
    private Transform originalVCamFollow;
    private Transform originalVCamLookAt;

    public void StartDriving()
    {
        if (isDriving) return;
        isDriving = true;

        player = FindObjectOfType<PixelGridMovement>();
        if (player != null) player.frozen = true;

        // 切换 Cinemachine 跟随到汽车
        if (virtualCamera == null)
            virtualCamera = FindObjectOfType<CinemachineVirtualCamera>();
        if (virtualCamera != null)
        {
            originalVCamFollow = virtualCamera.Follow;
            originalVCamLookAt = virtualCamera.LookAt;
            virtualCamera.Follow = transform;
            virtualCamera.LookAt = transform;
        }

        StartCoroutine(DriveRoutine());
    }

    private IEnumerator DriveRoutine()
    {
        transform.position = startPosition;

        if (driveSound != null) driveSound.Play();

        float t = 0f;
        Vector3 from = transform.position;
        Vector3 to = stopPosition;
        float driveDuration = Vector3.Distance(from, to) / driveSpeed;

        while (t < 1f)
        {
            t += Time.deltaTime / driveDuration;
            transform.position = Vector3.Lerp(from, to, t);
            yield return null;
        }

        transform.position = to;
        if (driveSound != null) driveSound.Stop();

        if (explosionSound != null) explosionSound.Play();
        // 创建爆炸特效（自动，不依赖外部脚本）
        StartCoroutine(SpawnExplosion(transform.position));
        // 等待爆炸动画播完再继续（防止车隐藏时杀死协程）
        yield return new WaitForSeconds(0.5f);

       // 恢复 Cinemachine 跟随到玩家
        if (virtualCamera != null)
        {
            virtualCamera.Follow = originalVCamFollow;
            virtualCamera.LookAt = originalVCamLookAt;
        }

        if (player != null) player.frozen = false;

        if (obstacleVersion != null)
        {
            obstacleVersion.SetActive(true);
            obstacleVersion.transform.position = transform.position;
        }

        // Post-explosion smoke clusters
        Texture2D stex = new Texture2D(32, 32, TextureFormat.ARGB32, false);
        Vector2 sc2 = new Vector2(16, 16);
        for (int sy = 0; sy < 32; sy++)
            for (int sx = 0; sx < 32; sx++)
                stex.SetPixel(sx, sy, Vector2.Distance(new Vector2(sx, sy), sc2) <= 15f ? Color.white : Color.clear);
        stex.Apply();

        GameObject[] aSmokes = new GameObject[3];
        SpriteRenderer[] aSRs = new SpriteRenderer[3];
        for (int si = 0; si < 3; si++)
        {
            aSmokes[si] = new GameObject("ASmoke" + si);
            Vector2 soff = Random.insideUnitCircle * 0.6f;
            aSmokes[si].transform.position = transform.position + new Vector3(soff.x, soff.y, 0);
            aSRs[si] = aSmokes[si].AddComponent<SpriteRenderer>();
            aSRs[si].sprite = Sprite.Create(stex, new Rect(0, 0, 32, 32), new Vector2(0.5f, 0.5f), 16f);
            aSRs[si].sortingOrder = 993;
            float sg = Random.Range(0.1f, 0.25f);
            aSRs[si].color = new Color(sg, sg, sg, 0.8f);
            aSmokes[si].transform.localScale = Vector3.one * Random.Range(0.3f, 0.5f);
        }

        float stm = 0f; float sDur = 2f;
        while (stm < sDur)
        {
            stm += Time.deltaTime;
            float sp = stm / sDur;
            for (int si = 0; si < 3; si++)
            {
                aSmokes[si].transform.position += Vector3.up * 0.3f * Time.deltaTime;
                aSmokes[si].transform.localScale *= 1.005f;
                aSmokes[si].transform.Rotate(0, 0, 60 * Time.deltaTime);
                Color scx = aSRs[si].color;
                scx.a = Mathf.Lerp(0.8f, 0f, sp);
                aSRs[si].color = scx;
            }
            yield return null;
        }

        for (int si = 0; si < 3; si++) Destroy(aSmokes[si]);
        gameObject.SetActive(false);
}

    private IEnumerator SpawnExplosion(Vector3 position)
    {
﻿        Texture2D tex = new Texture2D(32, 32, TextureFormat.ARGB32, false);
        Vector2 circleCenter = new Vector2(16, 16);
        float circleRadius = 15f;
        for (int y = 0; y < 32; y++)
            for (int x = 0; x < 32; x++)
                tex.SetPixel(x, y, Vector2.Distance(new Vector2(x, y), circleCenter) <= circleRadius ? Color.white : Color.clear);
        tex.Apply();

        GameObject main = new GameObject("ExplosionFire");
        main.transform.position = position;
        SpriteRenderer sr = main.AddComponent<SpriteRenderer>();
        sr.sortingOrder = 999;
        sr.sprite = Sprite.Create(tex, new Rect(0, 0, 32, 32), new Vector2(0.5f, 0.5f), 16f);
        sr.color = Color.white;
        main.transform.localScale = Vector3.one * 0.3f;

        GameObject[] debris = new GameObject[16];
        SpriteRenderer[] debrisSr = new SpriteRenderer[16];
        Vector2[] debrisDir = new Vector2[16];
        float[] debrisSpeed = new float[16];
        for (int i = 0; i < 16; i++)
        {
            debris[i] = new GameObject("Flame" + i);
            debris[i].transform.position = position;
            debrisSr[i] = debris[i].AddComponent<SpriteRenderer>();
            debrisSr[i].sprite = sr.sprite;
            debrisSr[i].sortingOrder = 998;
            debrisSr[i].color = new Color(1f, Random.Range(0.3f, 0.6f), Random.Range(0f, 0.1f));
            debris[i].transform.localScale = Vector3.one * Random.Range(0.08f, 0.18f);
            debrisDir[i] = Random.insideUnitCircle.normalized;
            debrisSpeed[i] = Random.Range(1.5f, 4f);
        }

        GameObject[] smokes = new GameObject[12];
        SpriteRenderer[] smokeSr = new SpriteRenderer[12];
        Vector2[] smokeDir = new Vector2[12];
        float[] smokeSpeed = new float[12];
        for (int i = 0; i < 12; i++)
        {
            smokes[i] = new GameObject("Smoke" + i);
            smokes[i].transform.position = position + (Vector3)Random.insideUnitCircle * 0.3f;
            smokeSr[i] = smokes[i].AddComponent<SpriteRenderer>();
            smokeSr[i].sprite = sr.sprite;
            smokeSr[i].sortingOrder = 996;
            float g = Random.Range(0.1f, 0.35f);
            smokeSr[i].color = new Color(g, g, g, 0.6f);
            smokes[i].transform.localScale = Vector3.one * Random.Range(0.2f, 0.4f);
            Vector2 d = Random.insideUnitCircle.normalized;
            d.y -= 0.3f;
            smokeDir[i] = d.normalized;
            smokeSpeed[i] = Random.Range(0.4f, 1f);
        }

        float t = 0f; float duration = 0.55f;
        while (t < duration)
        {
            t += Time.deltaTime;
            float p = t / duration;
            main.transform.localScale = Vector3.one * Mathf.Lerp(0.3f, 2.5f, p);
            Color fireColor;
            if (p < 0.12f)
                fireColor = Color.Lerp(Color.white, new Color(1f, 0.95f, 0.7f), p / 0.12f);
            else if (p < 0.4f)
            {
                float p2 = (p - 0.12f) / 0.28f;
                fireColor = Color.Lerp(new Color(1f, 0.95f, 0.7f), new Color(1f, 0.4f, 0f), p2);
            }
            else
            {
                float p2 = (p - 0.4f) / 0.6f;
                fireColor = Color.Lerp(new Color(0.6f, 0.15f, 0f), new Color(0.2f, 0.02f, 0f, 0f), p2);
            }
            sr.color = fireColor;

            for (int i = 0; i < 16; i++)
            {
                debris[i].transform.position += (Vector3)(debrisDir[i] * debrisSpeed[i]) * Time.deltaTime;
                debris[i].transform.Rotate(0, 0, 360 * Time.deltaTime);
                Color dc = debrisSr[i].color; dc.a = Mathf.Lerp(1f, 0f, p * 1.3f); debrisSr[i].color = dc;
                debris[i].transform.localScale *= 0.992f;
            }

            for (int i = 0; i < 12; i++)
            {
                smokes[i].transform.position += (Vector3)(smokeDir[i] * smokeSpeed[i]) * Time.deltaTime;
                smokes[i].transform.Rotate(0, 0, 180 * Time.deltaTime);
                Color sc = smokeSr[i].color; sc.a = Mathf.Lerp(0.6f, 0f, p * 1.5f); smokeSr[i].color = sc;
                smokes[i].transform.localScale *= 1.005f;
            }

            yield return null;
        }

        Destroy(main);
        for (int i = 0; i < 16; i++) Destroy(debris[i]);
        for (int i = 0; i < 12; i++) Destroy(smokes[i]);

        Camera cam = Camera.main;
        if (cam != null)
        {
            Vector3 orig = cam.transform.localPosition;
            float shakeDuration = 0.25f;
            float shakeMag = 0.6f;
            float st = 0f;
            while (st < shakeDuration)
            {
                st += Time.deltaTime;
                float x = Random.Range(-1f, 1f) * shakeMag;
                float y = Random.Range(-1f, 1f) * shakeMag;
                cam.transform.localPosition = orig + new Vector3(x, y, 0);
                shakeMag *= 0.85f;
                yield return null;
            }
            cam.transform.localPosition = orig;
        }

    }

}