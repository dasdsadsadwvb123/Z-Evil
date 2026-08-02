using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class PlayerSpawnManager : MonoBehaviour
{
    [Header("默认出生位置（没有传送数据时用）")]
    [SerializeField] private Vector2 defaultSpawnPosition = Vector2.zero;

    private void Start()
    {
        PixelGridMovement movement = GetComponent<PixelGridMovement>();

        if (TeleportManager.Instance != null && TeleportManager.Instance.TryGetSpawnPosition(out Vector2 spawnPos))
        {
            Debug.Log("使用传送位置: " + spawnPos);
            if (movement != null)
                movement.TeleportTo(spawnPos);
            else
                transform.position = spawnPos;
        }
        else
        {
            Debug.Log("使用默认位置: " + defaultSpawnPosition);
            if (movement != null)
                movement.TeleportTo(defaultSpawnPosition);
            else
                transform.position = defaultSpawnPosition;
        }
    }
}
