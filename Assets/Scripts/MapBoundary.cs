using UnityEngine;

public class MapBoundary : MonoBehaviour
{
    [Header("边界坐标（世界坐标）")]
    public float left = -10f;
    public float right = 10f;
    public float bottom = -8f;
    public float top = 8f;

    [Header("障碍物层")]
    public LayerMask obstacleLayer;

    [Header("墙厚度")]
    public float wallThickness = 0.5f;

    private GameObject wallParent;

    private void Start()
    {
        CreateWalls();
    }

    private void CreateWalls()
    {
        wallParent = new GameObject("__Boundaries__");
        wallParent.transform.SetParent(transform);

        int layer = GetLayerIndex();

        CreateWall("Wall_Left",   new Vector2(left - wallThickness / 2f, (top + bottom) / 2f), new Vector2(wallThickness, top - bottom + wallThickness * 2f), layer);
        CreateWall("Wall_Right",  new Vector2(right + wallThickness / 2f, (top + bottom) / 2f), new Vector2(wallThickness, top - bottom + wallThickness * 2f), layer);
        CreateWall("Wall_Top",    new Vector2((left + right) / 2f, top + wallThickness / 2f), new Vector2(right - left + wallThickness * 2f, wallThickness), layer);
        CreateWall("Wall_Bottom", new Vector2((left + right) / 2f, bottom - wallThickness / 2f), new Vector2(right - left + wallThickness * 2f, wallThickness), layer);
    }

    private void CreateWall(string name, Vector2 pos, Vector2 size, int layerIndex)
    {
        GameObject wall = new GameObject(name);
        wall.transform.SetParent(wallParent.transform);
        wall.transform.position = pos;
        wall.layer = layerIndex;

        BoxCollider2D col = wall.AddComponent<BoxCollider2D>();
        col.size = size;
        col.isTrigger = false;
    }

    private int GetLayerIndex()
    {
        for (int i = 0; i < 32; i++)
            if ((obstacleLayer.value & (1 << i)) != 0)
                return i;
        return 0;
    }

    private void OnDestroy()
    {
        if (wallParent != null)
            Destroy(wallParent);
    }

    private void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.red;
        Vector3[] corners = new Vector3[]
        {
            new Vector3(left, bottom, 0),
            new Vector3(right, bottom, 0),
            new Vector3(right, top, 0),
            new Vector3(left, top, 0)
        };
        for (int i = 0; i < 4; i++)
            Gizmos.DrawLine(corners[i], corners[(i + 1) % 4]);
    }
}
