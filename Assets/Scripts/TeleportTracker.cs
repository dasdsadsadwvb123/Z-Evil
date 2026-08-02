using UnityEngine;
using UnityEngine.SceneManagement;

public static class TeleportTracker
{
    public static int teleportCount = 0;

    public static void CountTeleport()
    {
        teleportCount++;
        Debug.Log("[传送] 累计传送次数: " + teleportCount);
    }
}
