using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

public class TeleportManager : MonoBehaviour
{
    public static TeleportManager Instance;

    private Vector2 spawnPosition;
    private bool hasSpawnData = false;

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
        Debug.Log("保存传送位置：" + pos);
    }

    public bool TryGetSpawnPosition(out Vector2 pos)
    {
        if (hasSpawnData)
        {
            pos = spawnPosition;
            hasSpawnData = false;
            Debug.Log("读取传送位置：" + pos);
            return true;
        }
        pos = Vector2.zero;
        Debug.Log("没有传送数据");
        return false;
    }
}