using UnityEngine;
using System.Collections.Generic;

public class GridDungeonGenerator : MonoBehaviour
{
    public enum CellType
    {
        Empty,
        Room,
        Corridor
    }

    public enum CorridorStyle
    {
        CircularTunnel,
        SquareCorridor,
        Mixed
    }

    [Header("Room Prefabs")]
    public GameObject floorPrefab;       // floor_1 (4m x 4m)
    public GameObject wallPrefab;        // wall_1_plain (4.06m x 3m)
    public GameObject ceilingPrefab;     // floor_1 flipped upside down as ceiling (4m x 4m)
    public GameObject doorwayPrefab;     // doorway_2_plain (4.06m x 3m)

    [Header("Tunnel Prefabs")]
    public GameObject tunnelStraight;    // tunnel_straight (3.5m wide, 6m long)
    public GameObject tunnelCorner;      // tunnel_ancle (6m cell corner)
    public GameObject tunnelTJunction;   // tunnel_junction_three_way (6m cell T)
    public GameObject tunnelXJunction;   // tunnel_junction_four_way (6m cell X)

    [Header("Corridor Style")]
    public CorridorStyle corridorStyle = CorridorStyle.SquareCorridor;

    [Header("Grid Layout Settings")]
    public int width = 10;               // Width of the dungeon in 6m cells
    public int height = 10;              // Height of the dungeon in 6m cells
    public float cellSize = 6.0f;        // Size of one grid cell (6 meters)

    [Header("Generation Settings")]
    [Range(0.1f, 0.4f)]
    public float roomDensity = 0.2f;     // Target ratio of rooms to grid size
    public int seed = 1337;

    [HideInInspector]
    [SerializeField]
    private List<GameObject> generatedObjects = new List<GameObject>();

    private CellType[,] grid;

    [ContextMenu("Generate Dungeon")]
    public void GenerateDungeon()
    {
        ClearDungeon();
        Random.InitState(seed);

        grid = new CellType[width, height];

        // 1. Generate Layout (Rooms & Corridors)
        GenerateLayout();

        // 2. Instantiate Geometry based on Layout
        InstantiateDungeon();
    }

    private void GenerateLayout()
    {
        // Initialize as Empty
        for (int x = 0; x < width; x++)
        {
            for (int z = 0; z < height; z++)
            {
                grid[x, z] = CellType.Empty;
            }
        }

        // Place Rooms randomly
        int targetRooms = Mathf.Max(3, Mathf.RoundToInt(width * height * roomDensity));
        List<Vector2Int> roomCenters = new List<Vector2Int>();
        int attempts = 0;

        while (roomCenters.Count < targetRooms && attempts < 150)
        {
            attempts++;
            int rx = Random.Range(1, width - 1);
            int rz = Random.Range(1, height - 1);

            if (grid[rx, rz] == CellType.Empty)
            {
                grid[rx, rz] = CellType.Room;
                roomCenters.Add(new Vector2Int(rx, rz));
            }
        }

        // Connect Rooms using Corridors via A* Pathfinding
        for (int i = 0; i < roomCenters.Count - 1; i++)
        {
            ConnectCells(roomCenters[i], roomCenters[i + 1]);
        }
        // Connect the last one to the first to create loops
        if (roomCenters.Count > 2)
        {
            ConnectCells(roomCenters[roomCenters.Count - 1], roomCenters[0]);
        }
    }

    private void ConnectCells(Vector2Int start, Vector2Int end)
    {
        PriorityQueue<PathNode> openSet = new PriorityQueue<PathNode>();
        Dictionary<Vector2Int, Vector2Int> cameFrom = new Dictionary<Vector2Int, Vector2Int>();
        Dictionary<Vector2Int, float> gScore = new Dictionary<Vector2Int, float>();

        openSet.Enqueue(new PathNode(start, 0), 0);
        gScore[start] = 0;

        Vector2Int[] dirs = {
            new Vector2Int(0, 1),  // N
            new Vector2Int(0, -1), // S
            new Vector2Int(1, 0),  // E
            new Vector2Int(-1, 0)  // W
        };

        bool pathFound = false;

        while (openSet.Count > 0)
        {
            PathNode current = openSet.Dequeue();

            if (current.Position == end)
            {
                pathFound = true;
                break;
            }

            foreach (var dir in dirs)
            {
                Vector2Int neighbor = current.Position + dir;
                if (neighbor.x < 0 || neighbor.x >= width || neighbor.y < 0 || neighbor.y >= height)
                    continue;

                float cost = 1.0f;
                if (grid[neighbor.x, neighbor.y] == CellType.Corridor || grid[neighbor.x, neighbor.y] == CellType.Room)
                {
                    cost = 0.1f;
                }

                float tentativeGScore = gScore[current.Position] + cost;
                if (!gScore.ContainsKey(neighbor) || tentativeGScore < gScore[neighbor])
                {
                    cameFrom[neighbor] = current.Position;
                    gScore[neighbor] = tentativeGScore;
                    float h = Vector2Int.Distance(neighbor, end);
                    openSet.Enqueue(new PathNode(neighbor, tentativeGScore), tentativeGScore + h);
                }
            }
        }

        if (pathFound)
        {
            Vector2Int curr = end;
            while (curr != start)
            {
                if (grid[curr.x, curr.y] == CellType.Empty)
                {
                    grid[curr.x, curr.y] = CellType.Corridor;
                }
                curr = cameFrom[curr];
            }
        }
    }

    private void InstantiateDungeon()
    {
        for (int x = 0; x < width; x++)
        {
            for (int z = 0; z < height; z++)
            {
                Vector3 cellCenter = new Vector3(x * cellSize, 0, z * cellSize);

                if (grid[x, z] == CellType.Room)
                {
                    InstantiateRoom(x, z, cellCenter);
                }
                else if (grid[x, z] == CellType.Corridor)
                {
                    CorridorStyle style = GetCellCorridorStyle(x, z);
                    if (style == CorridorStyle.CircularTunnel)
                    {
                        InstantiateCorridor(x, z, cellCenter);
                    }
                    else
                    {
                        InstantiateSquareCorridor(x, z, cellCenter);
                    }
                }
            }
        }
    }

    private CorridorStyle GetCellCorridorStyle(int x, int z)
    {
        if (x < 0 || x >= width || z < 0 || z >= height) return corridorStyle;
        if (grid[x, z] != CellType.Corridor) return corridorStyle;
        if (corridorStyle == CorridorStyle.Mixed)
        {
            // Deterministic selection based on cell coordinates and seed
            int cellHash = (x * 73856093) ^ (z * 19349663) ^ seed;
            return (System.Math.Abs(cellHash) % 2 == 0) ? CorridorStyle.CircularTunnel : CorridorStyle.SquareCorridor;
        }
        return corridorStyle;
    }

    private void InstantiateSquareCorridor(int x, int z, Vector3 center)
    {
        // 1. Spawn Floor (floor_1) scaled by Vector3(1.5, 1.0, 1.5)
        if (floorPrefab != null)
        {
            GameObject floor = Instantiate(floorPrefab, center, Quaternion.identity, transform);
            floor.transform.localScale = new Vector3(1.5f, 1.0f, 1.5f);
            generatedObjects.Add(floor);
        }

        // 2. Spawn Ceiling (floor_1 flipped upside down at Y=3.0m) scaled by Vector3(1.5, 1.0, 1.5)
        if (ceilingPrefab != null)
        {
            GameObject ceiling = Instantiate(ceilingPrefab, center + new Vector3(0, 3.0f, 0), Quaternion.Euler(180, 0, 0), transform);
            ceiling.transform.localScale = new Vector3(1.5f, 1.0f, 1.5f);
            generatedObjects.Add(ceiling);
        }

        // 3. Spawn Walls / Doorways along the 6m boundaries
        // North boundary (Z = +3m)
        SpawnCorridorBoundary(x, z, center, new Vector2Int(0, 1), new Vector3(0, 0, 3.0f), Quaternion.Euler(0, 180, 0));

        // South boundary (Z = -3m)
        SpawnCorridorBoundary(x, z, center, new Vector2Int(0, -1), new Vector3(0, 0, -3.0f), Quaternion.Euler(0, 0, 0));

        // East boundary (X = +3m)
        SpawnCorridorBoundary(x, z, center, new Vector2Int(1, 0), new Vector3(3.0f, 0, 0), Quaternion.Euler(0, 270, 0));

        // West boundary (X = -3m)
        SpawnCorridorBoundary(x, z, center, new Vector2Int(-1, 0), new Vector3(-3.0f, 0, 0), Quaternion.Euler(0, 90, 0));
    }

    private void SpawnCorridorBoundary(int x, int z, Vector3 center, Vector2Int dir, Vector3 offset, Quaternion rotation)
    {
        int nx = x + dir.x;
        int nz = z + dir.y;

        CellType neighborType = CellType.Empty;
        if (nx >= 0 && nx < width && nz >= 0 && nz < height)
        {
            neighborType = grid[nx, nz];
        }

        if (neighborType == CellType.Empty)
        {
            if (wallPrefab != null)
            {
                GameObject wall = Instantiate(wallPrefab, center + offset, rotation, transform);
                wall.transform.localScale = new Vector3(1.5f, 1.0f, 1.5f);
                generatedObjects.Add(wall);
            }
        }
        else if (neighborType == CellType.Corridor)
        {
            // If the neighbor is a circular tunnel, we spawn a doorway frame as a transition
            if (GetCellCorridorStyle(nx, nz) == CorridorStyle.CircularTunnel)
            {
                if (doorwayPrefab != null)
                {
                    GameObject doorway = Instantiate(doorwayPrefab, center + offset, rotation, transform);
                    doorway.transform.localScale = new Vector3(1.5f, 1.0f, 1.5f);
                    doorway.AddComponent<PhysicalDoor>();
                    generatedObjects.Add(doorway);
                }
            }
        }
    }

    private void InstantiateRoom(int x, int z, Vector3 center)
    {
        // 1. Spawn Floor (floor_1) scaled by Vector3(1.5, 1.0, 1.5) to cover 6.06m x 6.06m
        if (floorPrefab != null)
        {
            GameObject floor = Instantiate(floorPrefab, center, Quaternion.identity, transform);
            floor.transform.localScale = new Vector3(1.5f, 1.0f, 1.5f);
            generatedObjects.Add(floor);
        }

        // 2. Spawn Ceiling (floor_1 flipped upside down at Y=3.0m) scaled by Vector3(1.5, 1.0, 1.5)
        if (ceilingPrefab != null)
        {
            GameObject ceiling = Instantiate(ceilingPrefab, center + new Vector3(0, 3.0f, 0), Quaternion.Euler(180, 0, 0), transform);
            ceiling.transform.localScale = new Vector3(1.5f, 1.0f, 1.5f);
            generatedObjects.Add(ceiling);
        }

        // 3. Spawn Walls / Doorways along the 6m boundaries scaled by Vector3(1.5, 1.0, 1.5)
        // North boundary (Z = +3m)
        SpawnRoomBoundary(x, z, center, new Vector2Int(0, 1), new Vector3(0, 0, 3.0f), Quaternion.Euler(0, 180, 0));

        // South boundary (Z = -3m)
        SpawnRoomBoundary(x, z, center, new Vector2Int(0, -1), new Vector3(0, 0, -3.0f), Quaternion.Euler(0, 0, 0));

        // East boundary (X = +3m)
        SpawnRoomBoundary(x, z, center, new Vector2Int(1, 0), new Vector3(3.0f, 0, 0), Quaternion.Euler(0, 270, 0));

        // West boundary (X = -3m)
        SpawnRoomBoundary(x, z, center, new Vector2Int(-1, 0), new Vector3(-3.0f, 0, 0), Quaternion.Euler(0, 90, 0));
    }

    private void SpawnRoomBoundary(int x, int z, Vector3 center, Vector2Int dir, Vector3 offset, Quaternion rotation)
    {
        int nx = x + dir.x;
        int nz = z + dir.y;

        CellType neighborType = CellType.Empty;
        if (nx >= 0 && nx < width && nz >= 0 && nz < height)
        {
            neighborType = grid[nx, nz];
        }

        // Merge adjacent Room cells seamlessly. Place walls/doorways only if not adjacent to another Room.
        if (neighborType == CellType.Room)
        {
            return;
        }
        else if (neighborType == CellType.Corridor)
        {
            if (doorwayPrefab != null)
            {
                GameObject doorway = Instantiate(doorwayPrefab, center + offset, rotation, transform);
                doorway.transform.localScale = new Vector3(1.5f, 1.0f, 1.5f);
                doorway.AddComponent<PhysicalDoor>();
                generatedObjects.Add(doorway);
            }
        }
        else
        {
            if (wallPrefab != null)
            {
                GameObject wall = Instantiate(wallPrefab, center + offset, rotation, transform);
                wall.transform.localScale = new Vector3(1.5f, 1.0f, 1.5f);
                generatedObjects.Add(wall);
            }
        }
    }

    private void InstantiateCorridor(int x, int z, Vector3 center)
    {
        bool N = IsConnected(x, z + 1);
        bool S = IsConnected(x, z - 1);
        bool E = IsConnected(x + 1, z);
        bool W = IsConnected(x - 1, z);

        int connectionsCount = (N ? 1 : 0) + (S ? 1 : 0) + (E ? 1 : 0) + (W ? 1 : 0);

        if (connectionsCount == 0) return;

        // Spawn exactly one unscaled tunnel piece centered in this cell
        if (connectionsCount == 1)
        {
            if (N || S) SpawnTunnel(tunnelStraight, center, Quaternion.Euler(0, 0, 0));
            else if (E || W) SpawnTunnel(tunnelStraight, center, Quaternion.Euler(0, 90, 0));
        }
        else if (connectionsCount == 2)
        {
            if (N && S)
            {
                SpawnTunnel(tunnelStraight, center, Quaternion.Euler(0, 0, 0));
            }
            else if (E && W)
            {
                SpawnTunnel(tunnelStraight, center, Quaternion.Euler(0, 90, 0));
            }
            else
            {
                if (W && N) SpawnTunnel(tunnelCorner, center, Quaternion.Euler(0, 0, 0));
                else if (N && E) SpawnTunnel(tunnelCorner, center, Quaternion.Euler(0, 90, 0));
                else if (E && S) SpawnTunnel(tunnelCorner, center, Quaternion.Euler(0, 180, 0));
                else if (S && W) SpawnTunnel(tunnelCorner, center, Quaternion.Euler(0, 270, 0));
            }
        }
        else if (connectionsCount == 3)
        {
            if (N && S && W) SpawnTunnel(tunnelTJunction, center, Quaternion.Euler(0, 0, 0));
            else if (E && W && N) SpawnTunnel(tunnelTJunction, center, Quaternion.Euler(0, 90, 0));
            else if (S && N && E) SpawnTunnel(tunnelTJunction, center, Quaternion.Euler(0, 180, 0));
            else if (W && E && S) SpawnTunnel(tunnelTJunction, center, Quaternion.Euler(0, 270, 0));
        }
        else if (connectionsCount == 4)
        {
            SpawnTunnel(tunnelXJunction, center, Quaternion.identity);
        }
    }

    private void SpawnTunnel(GameObject prefab, Vector3 position, Quaternion rotation)
    {
        if (prefab != null)
        {
            GameObject tunnelInstance = Instantiate(prefab, position, rotation, transform);
            generatedObjects.Add(tunnelInstance);
        }
    }

    private bool IsConnected(int x, int z)
    {
        if (x < 0 || x >= width || z < 0 || z >= height) return false;
        return grid[x, z] == CellType.Room || grid[x, z] == CellType.Corridor;
    }

    public string GetGridLayoutAsString()
    {
        System.Text.StringBuilder sb = new System.Text.StringBuilder();
        for (int z = height - 1; z >= 0; z--)
        {
            for (int x = 0; x < width; x++)
            {
                if (grid == null) return "Grid is null";
                if (grid[x, z] == CellType.Room) sb.Append("R");
                else if (grid[x, z] == CellType.Corridor) sb.Append("C");
                else sb.Append(".");
            }
            sb.AppendLine();
        }
        return sb.ToString();
    }

    [ContextMenu("Clear Dungeon")]
    public void ClearDungeon()
    {
        foreach (var obj in generatedObjects)
        {
            if (obj != null)
            {
                DestroyImmediate(obj);
            }
        }
        generatedObjects.Clear();

        List<GameObject> children = new List<GameObject>();
        foreach (Transform child in transform)
        {
            children.Add(child.gameObject);
        }
        foreach (var child in children)
        {
            DestroyImmediate(child);
        }
    }
}

// A simple node class for pathfinding
public class PathNode
{
    public Vector2Int Position { get; private set; }
    public float GCost { get; private set; }

    public PathNode(Vector2Int position, float gCost)
    {
        Position = position;
        GCost = gCost;
    }
}

// Simple Priority Queue implementation for A*
public class PriorityQueue<T>
{
    private List<System.Tuple<T, float>> elements = new List<System.Tuple<T, float>>();

    public int Count => elements.Count;

    public void Enqueue(T item, float priority)
    {
        elements.Add(System.Tuple.Create(item, priority));
    }

    public T Dequeue()
    {
        int bestIndex = 0;

        for (int i = 0; i < elements.Count; i++)
        {
            if (elements[i].Item2 < elements[bestIndex].Item2)
            {
                bestIndex = i;
            }
        }

        T bestItem = elements[bestIndex].Item1;
        elements.RemoveAt(bestIndex);
        return bestItem;
    }
}
