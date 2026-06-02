using UnityEngine;
using System.Collections.Generic;

public class GridDungeonGenerator : MonoBehaviour
{
    public enum CellType
    {
        Empty,
        Room,
        Corridor,
        Stairs
    }

    public enum CorridorStyle
    {
        CircularTunnel,
        SquareCorridor,
        Mixed
    }

    public enum DungeonTheme
    {
        Bunker,
        GothicRuins,
        Mixed
    }

    [System.Serializable]
    public struct DungeonCell
    {
        public CellType type;
        public int roomId;
        public int rotation; // Euler Y rotation for stairs or tunnels
        public bool hasFloor;
        public bool hasCeiling;
    }

    [System.Serializable]
    public class Room
    {
        public int id;
        public int x;
        public int y; // layer
        public int z;
        public int w;
        public int h;
    }

    [Header("Bunker Theme Prefabs")]
    public GameObject floorPrefab;       // floor_1 (4m x 4m)
    public GameObject wallPrefab;        // wall_1_plain (4.06m x 3m)
    public GameObject ceilingPrefab;     // floor_1 flipped upside down as ceiling (4m x 4m)
    public GameObject doorwayPrefab;     // doorway_2_plain (4.06m x 3m)

    [Header("Gothic Theme Prefabs")]
    public GameObject gothicFloorPrefab;    // floor_ceiling_1 (4m x 4m)
    public GameObject gothicWallPrefab;     // wall_1_plain (Gothic version, 4m x 3m)
    public GameObject gothicCeilingPrefab;  // floor_ceiling_1 flipped upside down
    public GameObject gothicDoorwayPrefab;  // arc_1_wall_1_plain (stone archway)

    [Header("Tunnel Prefabs")]
    public GameObject tunnelStraight;    // tunnel_straight (3.5m wide, 6m long)
    public GameObject tunnelCorner;      // tunnel_ancle (6m cell corner)
    public GameObject tunnelTJunction;   // tunnel_junction_three_way (6m cell T)
    public GameObject tunnelXJunction;   // tunnel_junction_four_way (6m cell X)

    [Header("Staircase Prefabs")]
    public GameObject stairsPrefab;      // stairs_mp_1 or stairs_5_concrete (for Gothic)
    public GameObject bunkerStairsPrefab; // stairs_5_wood (for Bunker)

    [Header("Dungeon Theme & Layout Settings")]
    public DungeonTheme dungeonTheme = DungeonTheme.GothicRuins;
    public CorridorStyle corridorStyle = CorridorStyle.SquareCorridor;

    public int width = 12;               // Grid width in cells
    public int height = 12;              // Grid depth in cells
    public int layers = 2;               // Number of vertical layers (levels)
    public float cellSize = 6.0f;        // Size of one cell (6 meters)
    public float cellHeight = 3.0f;      // Height of one layer (3 meters)

    [Header("Generation Settings")]
    public int minRoomSize = 2;          // Min room size in cells
    public int maxRoomSize = 3;          // Max room size in cells
    public int roomsPerLayer = 3;        // Number of random rooms per level
    public float roomDensity = 0.20f;    // Deprecated but kept for API compatibility
    public int seed = 1337;

    [HideInInspector]
    [SerializeField]
    private List<GameObject> generatedObjects = new List<GameObject>();

    private DungeonCell[,,] grid;
    private List<Room> rooms = new List<Room>();

    [ContextMenu("Generate Dungeon")]
    public void GenerateDungeon()
    {
        ClearDungeon();
        Random.InitState(seed);

        // 1. Generate 3D grid layout
        GenerateLayout();

        // 2. Instantiate all modular geometries
        InstantiateDungeon();
    }

    private void GenerateLayout()
    {
        grid = new DungeonCell[width, layers, height];
        rooms.Clear();

        // Initialize cells as Empty with floors and ceilings enabled
        for (int x = 0; x < width; x++)
        {
            for (int y = 0; y < layers; y++)
            {
                for (int z = 0; z < height; z++)
                {
                    grid[x, y, z] = new DungeonCell
                    {
                        type = CellType.Empty,
                        roomId = 0,
                        rotation = 0,
                        hasFloor = true,
                        hasCeiling = true
                    };
                }
            }
        }

        // Place Rooms on each layer using Box Packing
        int currentRoomId = 1;
        for (int y = 0; y < layers; y++)
        {
            int placedRooms = 0;
            int attempts = 0;
            while (placedRooms < roomsPerLayer && attempts < 150)
            {
                attempts++;
                int rw = Random.Range(minRoomSize, maxRoomSize + 1);
                int rh = Random.Range(minRoomSize, maxRoomSize + 1);
                
                // Keep 1 grid buffer on borders
                int rx = Random.Range(1, width - rw - 1);
                int rz = Random.Range(1, height - rh - 1);

                // Check overlap with existing rooms (including buffer zone)
                bool overlap = false;
                for (int tx = rx - 1; tx < rx + rw + 1; tx++)
                {
                    for (int tz = rz - 1; tz < rz + rh + 1; tz++)
                    {
                        if (grid[tx, y, tz].type != CellType.Empty)
                        {
                            overlap = true;
                            break;
                        }
                    }
                    if (overlap) break;
                }

                if (!overlap)
                {
                    Room r = new Room { id = currentRoomId++, x = rx, y = y, z = rz, w = rw, h = rh };
                    rooms.Add(r);
                    placedRooms++;

                    // Mark grid cells
                    for (int tx = rx; tx < rx + rw; tx++)
                    {
                        for (int tz = rz; tz < rz + rh; tz++)
                        {
                            grid[tx, y, tz].type = CellType.Room;
                            grid[tx, y, tz].roomId = r.id;
                        }
                    }
                }
            }
        }

        // Connect Rooms on each layer using A* pathfinding
        for (int y = 0; y < layers; y++)
        {
            List<Room> layerRooms = rooms.FindAll(r => r.y == y);
            for (int i = 0; i < layerRooms.Count - 1; i++)
            {
                Vector3Int start = new Vector3Int(layerRooms[i].x + layerRooms[i].w / 2, y, layerRooms[i].z + layerRooms[i].h / 2);
                Vector3Int end = new Vector3Int(layerRooms[i + 1].x + layerRooms[i + 1].w / 2, y, layerRooms[i + 1].z + layerRooms[i + 1].h / 2);
                ConnectCells(start, end);
            }
            if (layerRooms.Count > 2)
            {
                Vector3Int start = new Vector3Int(layerRooms[layerRooms.Count - 1].x + layerRooms[layerRooms.Count - 1].w / 2, y, layerRooms[layerRooms.Count - 1].z + layerRooms[layerRooms.Count - 1].h / 2);
                Vector3Int end = new Vector3Int(layerRooms[0].x + layerRooms[0].w / 2, y, layerRooms[0].z + layerRooms[0].h / 2);
                ConnectCells(start, end);
            }
        }

        // Connect layers vertically using stairs
        for (int y = 0; y < layers - 1; y++)
        {
            bool stairPlaced = false;
            int attempts = 0;
            while (!stairPlaced && attempts < 300)
            {
                attempts++;
                // Leave room for Northward stairs run (needs z+1 buffer)
                int sx = Random.Range(2, width - 2);
                int sz = Random.Range(2, height - 3);

                CellType lowerType = grid[sx, y, sz].type;
                CellType upperType = grid[sx, y + 1, sz + 1].type;

                // We can place stairs if the lower cell and the upper landing cell are valid
                if ((lowerType == CellType.Room || lowerType == CellType.Corridor) &&
                    (upperType == CellType.Room || upperType == CellType.Corridor))
                {
                    // Place stairs in (sx, y, sz) rising North
                    grid[sx, y, sz].type = CellType.Stairs;
                    grid[sx, y, sz].rotation = 0; // 0 degrees = North
                    grid[sx, y, sz].hasCeiling = false; // Carve ceiling so player can stand

                    // Carve the floor of the cell directly above the stairs
                    grid[sx, y + 1, sz].hasFloor = false;

                    // Ensure connection pathways on the upper layer are open
                    if (grid[sx, y + 1, sz].type == CellType.Empty) grid[sx, y + 1, sz].type = CellType.Corridor;
                    if (grid[sx, y + 1, sz + 1].type == CellType.Empty) grid[sx, y + 1, sz + 1].type = CellType.Corridor;

                    stairPlaced = true;
                }
            }
        }
    }

    private void ConnectCells(Vector3Int start, Vector3Int end)
    {
        PriorityQueue<PathNode3D> openSet = new PriorityQueue<PathNode3D>();
        Dictionary<Vector3Int, Vector3Int> cameFrom = new Dictionary<Vector3Int, Vector3Int>();
        Dictionary<Vector3Int, float> gScore = new Dictionary<Vector3Int, float>();

        openSet.Enqueue(new PathNode3D(start, 0), 0);
        gScore[start] = 0;

        Vector3Int[] dirs = {
            new Vector3Int(0, 0, 1),   // N
            new Vector3Int(0, 0, -1),  // S
            new Vector3Int(1, 0, 0),   // E
            new Vector3Int(-1, 0, 0)   // W
        };

        bool pathFound = false;

        while (openSet.Count > 0)
        {
            PathNode3D current = openSet.Dequeue();

            if (current.Position == end)
            {
                pathFound = true;
                break;
            }

            foreach (var dir in dirs)
            {
                Vector3Int neighbor = current.Position + dir;
                if (neighbor.x < 1 || neighbor.x >= width - 1 || neighbor.z < 1 || neighbor.z >= height - 1)
                    continue;

                float cost = 1.0f;
                CellType nType = grid[neighbor.x, neighbor.y, neighbor.z].type;
                if (nType == CellType.Corridor || nType == CellType.Room)
                {
                    cost = 0.1f;
                }

                float tentativeGScore = gScore[current.Position] + cost;
                if (!gScore.ContainsKey(neighbor) || tentativeGScore < gScore[neighbor])
                {
                    cameFrom[neighbor] = current.Position;
                    gScore[neighbor] = tentativeGScore;
                    float h = Vector3.Distance(new Vector3(neighbor.x, neighbor.y * cellHeight, neighbor.z), new Vector3(end.x, end.y * cellHeight, end.z));
                    openSet.Enqueue(new PathNode3D(neighbor, tentativeGScore), tentativeGScore + h);
                }
            }
        }

        if (pathFound)
        {
            Vector3Int curr = end;
            while (curr != start)
            {
                if (grid[curr.x, curr.y, curr.z].type == CellType.Empty)
                {
                    grid[curr.x, curr.y, curr.z].type = CellType.Corridor;
                }
                curr = cameFrom[curr];
            }
        }
    }

    private void InstantiateDungeon()
    {
        for (int y = 0; y < layers; y++)
        {
            for (int x = 0; x < width; x++)
            {
                for (int z = 0; z < height; z++)
                {
                    Vector3 cellCenter = new Vector3(x * cellSize, y * cellHeight, z * cellSize);
                    DungeonCell cell = grid[x, y, z];

                    if (cell.type == CellType.Room)
                    {
                        InstantiateRoom(x, y, z, cellCenter);
                    }
                    else if (cell.type == CellType.Corridor)
                    {
                        CorridorStyle style = GetCellCorridorStyle(x, y, z);
                        if (style == CorridorStyle.CircularTunnel)
                        {
                            InstantiateCorridor(x, y, z, cellCenter);
                        }
                        else
                        {
                            InstantiateSquareCorridor(x, y, z, cellCenter);
                        }
                    }
                    else if (cell.type == CellType.Stairs)
                    {
                        InstantiateStairs(x, y, z, cellCenter);
                    }
                }
            }
        }
    }

    private void InstantiateRoom(int x, int y, int z, Vector3 center)
    {
        DungeonCell cell = grid[x, y, z];

        // 1. Spawn Floor (if hasFloor is true)
        if (cell.hasFloor)
        {
            GameObject floorPref = GetCellFloorPrefab(x, y, z);
            if (floorPref != null)
            {
                GameObject floor = Instantiate(floorPref, center, Quaternion.identity, transform);
                floor.transform.localScale = new Vector3(1.52f, 1.0f, 1.52f);
                generatedObjects.Add(floor);
            }
        }

        // 2. Spawn Ceiling (if hasCeiling is true)
        if (cell.hasCeiling)
        {
            GameObject ceilingPref = GetCellCeilingPrefab(x, y, z);
            if (ceilingPref != null)
            {
                GameObject ceiling = Instantiate(ceilingPref, center + new Vector3(0, cellHeight, 0), Quaternion.Euler(180, 0, 0), transform);
                ceiling.transform.localScale = new Vector3(1.52f, 1.0f, 1.52f);
                generatedObjects.Add(ceiling);
            }
        }

        // 3. Spawn Walls / Doorways along the 6m boundaries
        // North boundary (Z = +3m)
        SpawnRoomBoundary(x, y, z, center, new Vector2Int(0, 1), new Vector3(0, 0, cellSize * 0.5f), Quaternion.Euler(0, 180, 0));

        // South boundary (Z = -3m)
        SpawnRoomBoundary(x, y, z, center, new Vector2Int(0, -1), new Vector3(0, 0, -cellSize * 0.5f), Quaternion.Euler(0, 0, 0));

        // East boundary (X = +3m)
        SpawnRoomBoundary(x, y, z, center, new Vector2Int(1, 0), new Vector3(cellSize * 0.5f, 0, 0), Quaternion.Euler(0, 270, 0));

        // West boundary (X = -3m)
        SpawnRoomBoundary(x, y, z, center, new Vector2Int(-1, 0), new Vector3(-cellSize * 0.5f, 0, 0), Quaternion.Euler(0, 90, 0));
    }

    private void SpawnRoomBoundary(int x, int y, int z, Vector3 center, Vector2Int dir, Vector3 offset, Quaternion rotation)
    {
        int nx = x + dir.x;
        int nz = z + dir.y;

        CellType neighborType = CellType.Empty;
        if (nx >= 0 && nx < width && nz >= 0 && nz < height)
        {
            neighborType = grid[nx, y, nz].type;
        }

        // Merge adjacent Room cells seamlessly. Place walls/doorways only if not adjacent to another Room.
        if (neighborType == CellType.Room)
        {
            return;
        }
        else if (neighborType == CellType.Corridor || neighborType == CellType.Stairs)
        {
            // If neighbor is Stairs, check if this boundary is the entrance to the stairs
            if (neighborType == CellType.Stairs)
            {
                float rotY = grid[nx, y, nz].rotation;
                Vector3Int forwardDir = Vector3Int.RoundToInt(Quaternion.Euler(0, rotY, 0) * Vector3.forward);
                Vector3Int relativeDir = new Vector3Int(x - nx, 0, z - nz); // from stairs to room
                
                // Entrance is at -forwardDir. If we are not at the entrance, spawn a wall instead of a doorway!
                if (relativeDir != -forwardDir)
                {
                    GameObject wallPref = GetCellWallPrefab(x, y, z);
                    if (wallPref != null)
                    {
                        GameObject wall = Instantiate(wallPref, center + offset, rotation, transform);
                        wall.transform.localScale = new Vector3(1.52f, 1.0f, 1.52f);
                        generatedObjects.Add(wall);
                    }
                    return;
                }
            }

            GameObject doorwayPref = GetCellDoorwayPrefab(x, y, z);
            if (doorwayPref != null)
            {
                GameObject doorway = Instantiate(doorwayPref, center + offset, rotation, transform);
                if (doorwayPref.name.ToLower().Contains("arc"))
                {
                    doorway.transform.localScale = new Vector3(2.02f, 1.0f, 1.52f);
                }
                else
                {
                    doorway.transform.localScale = new Vector3(1.52f, 1.0f, 1.52f);
                }
                if (doorwayPref.name.Contains("doorway") || doorwayPref.name.Contains("arc"))
                {
                    doorway.AddComponent<PhysicalDoor>();
                }
                generatedObjects.Add(doorway);
            }
        }
        else
        {
            if (y > 0 && grid[nx, y - 1, nz].type == CellType.Stairs)
            {
                return; // Do not spawn a wall blocking the stairs exit!
            }

            GameObject wallPref = GetCellWallPrefab(x, y, z);
            if (wallPref != null)
            {
                GameObject wall = Instantiate(wallPref, center + offset, rotation, transform);
                wall.transform.localScale = new Vector3(1.52f, 1.0f, 1.52f);
                generatedObjects.Add(wall);
            }
        }
    }

    private void InstantiateSquareCorridor(int x, int y, int z, Vector3 center)
    {
        DungeonCell cell = grid[x, y, z];

        // 1. Spawn Floor
        if (cell.hasFloor)
        {
            GameObject floorPref = GetCellFloorPrefab(x, y, z);
            if (floorPref != null)
            {
                GameObject floor = Instantiate(floorPref, center, Quaternion.identity, transform);
                floor.transform.localScale = new Vector3(1.52f, 1.0f, 1.52f);
                generatedObjects.Add(floor);
            }
        }

        // 2. Spawn Ceiling
        if (cell.hasCeiling)
        {
            GameObject ceilingPref = GetCellCeilingPrefab(x, y, z);
            if (ceilingPref != null)
            {
                GameObject ceiling = Instantiate(ceilingPref, center + new Vector3(0, cellHeight, 0), Quaternion.Euler(180, 0, 0), transform);
                ceiling.transform.localScale = new Vector3(1.52f, 1.0f, 1.52f);
                generatedObjects.Add(ceiling);
            }
        }

        // 3. Spawn Walls / Doorways along the 6m boundaries
        // North boundary (Z = +3m)
        SpawnCorridorBoundary(x, y, z, center, new Vector2Int(0, 1), new Vector3(0, 0, cellSize * 0.5f), Quaternion.Euler(0, 180, 0));

        // South boundary (Z = -3m)
        SpawnCorridorBoundary(x, y, z, center, new Vector2Int(0, -1), new Vector3(0, 0, -cellSize * 0.5f), Quaternion.Euler(0, 0, 0));

        // East boundary (X = +3m)
        SpawnCorridorBoundary(x, y, z, center, new Vector2Int(1, 0), new Vector3(cellSize * 0.5f, 0, 0), Quaternion.Euler(0, 270, 0));

        // West boundary (X = -3m)
        SpawnCorridorBoundary(x, y, z, center, new Vector2Int(-1, 0), new Vector3(-cellSize * 0.5f, 0, 0), Quaternion.Euler(0, 90, 0));
    }

    private void SpawnCorridorBoundary(int x, int y, int z, Vector3 center, Vector2Int dir, Vector3 offset, Quaternion rotation)
    {
        int nx = x + dir.x;
        int nz = z + dir.y;

        CellType neighborType = CellType.Empty;
        if (nx >= 0 && nx < width && nz >= 0 && nz < height)
        {
            neighborType = grid[nx, y, nz].type;
        }

        if (neighborType == CellType.Empty)
        {
            if (y > 0 && grid[nx, y - 1, nz].type == CellType.Stairs)
            {
                return; // Do not spawn a wall blocking the stairs exit!
            }

            GameObject wallPref = GetCellWallPrefab(x, y, z);
            if (wallPref != null)
            {
                GameObject wall = Instantiate(wallPref, center + offset, rotation, transform);
                wall.transform.localScale = new Vector3(1.52f, 1.0f, 1.52f);
                generatedObjects.Add(wall);
            }
        }
        else if (neighborType == CellType.Corridor || neighborType == CellType.Stairs)
        {
            // If neighbor is Stairs, check if this boundary is the entrance to the stairs
            if (neighborType == CellType.Stairs)
            {
                float rotY = grid[nx, y, nz].rotation;
                Vector3Int forwardDir = Vector3Int.RoundToInt(Quaternion.Euler(0, rotY, 0) * Vector3.forward);
                Vector3Int relativeDir = new Vector3Int(x - nx, 0, z - nz); // from stairs to corridor
                
                // Entrance is at -forwardDir. If we are not at the entrance, spawn a wall instead of a doorway/nothing!
                if (relativeDir != -forwardDir)
                {
                    GameObject wallPref = GetCellWallPrefab(x, y, z);
                    if (wallPref != null)
                    {
                        GameObject wall = Instantiate(wallPref, center + offset, rotation, transform);
                        wall.transform.localScale = new Vector3(1.52f, 1.0f, 1.52f);
                        generatedObjects.Add(wall);
                    }
                    return;
                }
            }

            // If the neighbor is a circular tunnel or a stairs entrance, spawn a doorway frame as a transition
            if (GetCellCorridorStyle(nx, y, nz) == CorridorStyle.CircularTunnel || neighborType == CellType.Stairs)
            {
                GameObject doorwayPref = GetCellDoorwayPrefab(x, y, z);
                if (doorwayPref != null)
                {
                    GameObject doorway = Instantiate(doorwayPref, center + offset, rotation, transform);
                    if (doorwayPref.name.ToLower().Contains("arc"))
                    {
                        doorway.transform.localScale = new Vector3(2.02f, 1.0f, 1.52f);
                    }
                    else
                    {
                        doorway.transform.localScale = new Vector3(1.52f, 1.0f, 1.52f);
                    }
                    if (doorwayPref.name.Contains("doorway") || doorwayPref.name.Contains("arc"))
                    {
                        doorway.AddComponent<PhysicalDoor>();
                    }
                    generatedObjects.Add(doorway);
                }
            }
        }
    }

    private void InstantiateCorridor(int x, int y, int z, Vector3 center)
    {
        bool N = IsConnected(x, y, z + 1);
        bool S = IsConnected(x, y, z - 1);
        bool E = IsConnected(x + 1, y, z);
        bool W = IsConnected(x - 1, y, z);

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
            if (N && S) SpawnTunnel(tunnelStraight, center, Quaternion.Euler(0, 0, 0));
            else if (E && W) SpawnTunnel(tunnelStraight, center, Quaternion.Euler(0, 90, 0));
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

    private void InstantiateStairs(int x, int y, int z, Vector3 center)
    {
        DungeonCell cell = grid[x, y, z];
        float rotY = cell.rotation;
        Quaternion stairsRot = Quaternion.Euler(0, rotY, 0);
        Vector3 riseDir = stairsRot * Vector3.forward;
        Vector3 sideDir = stairsRot * Vector3.right;

        // Choose stair prefab based on layer
        GameObject activeStairsPrefab = (y % 2 == 0) ? stairsPrefab : (bunkerStairsPrefab != null ? bunkerStairsPrefab : stairsPrefab);

        // 1. Spawn Stairs Prefab
        if (activeStairsPrefab != null)
        {
            GameObject stairsInstance = Instantiate(activeStairsPrefab, center, stairsRot, transform);
            float widthScale = activeStairsPrefab.name.ToLower().Contains("stairs_5") ? 3.0f : 1.5f;
            stairsInstance.transform.localScale = new Vector3(widthScale, 1.0f, 1.0f);
            generatedObjects.Add(stairsInstance);
        }

        // 2. Spawn Floor under stairs
        if (cell.hasFloor)
        {
            GameObject floorPref = GetCellFloorPrefab(x, y, z);
            if (floorPref != null)
            {
                GameObject floor = Instantiate(floorPref, center, Quaternion.identity, transform);
                floor.transform.localScale = new Vector3(1.52f, 1.0f, 1.52f);
                generatedObjects.Add(floor);
            }
        }

        // 3. Spawn Ceiling for the upper cell (above stairs)
        GameObject ceilingPref = GetCellCeilingPrefab(x, y + 1, z);
        if (ceilingPref != null)
        {
            GameObject ceiling = Instantiate(ceilingPref, center + new Vector3(0, cellHeight * 2.0f, 0), Quaternion.Euler(180, 0, 0), transform);
            ceiling.transform.localScale = new Vector3(1.52f, 1.0f, 1.52f);
            generatedObjects.Add(ceiling);
        }

        // 4. Spawn Enclosing Walls
        GameObject wallPref = GetCellWallPrefab(x, y, z);
        if (wallPref != null)
        {
            // East wall (Y = y)
            GameObject wallE = Instantiate(wallPref, center + sideDir * (cellSize * 0.5f), Quaternion.Euler(0, rotY - 90f, 0), transform);
            wallE.transform.localScale = new Vector3(1.52f, 1.0f, 1.52f);
            generatedObjects.Add(wallE);

            // West wall (Y = y)
            GameObject wallW = Instantiate(wallPref, center - sideDir * (cellSize * 0.5f), Quaternion.Euler(0, rotY + 90f, 0), transform);
            wallW.transform.localScale = new Vector3(1.52f, 1.0f, 1.52f);
            generatedObjects.Add(wallW);

            // East upper wall (Y = y+1)
            GameObject wallE_up = Instantiate(wallPref, center + sideDir * (cellSize * 0.5f) + new Vector3(0, cellHeight, 0), Quaternion.Euler(0, rotY - 90f, 0), transform);
            wallE_up.transform.localScale = new Vector3(1.52f, 1.0f, 1.52f);
            generatedObjects.Add(wallE_up);

            // West upper wall (Y = y+1)
            GameObject wallW_up = Instantiate(wallPref, center - sideDir * (cellSize * 0.5f) + new Vector3(0, cellHeight, 0), Quaternion.Euler(0, rotY + 90f, 0), transform);
            wallW_up.transform.localScale = new Vector3(1.52f, 1.0f, 1.52f);
            generatedObjects.Add(wallW_up);

            // North wall (Y = y) - under the high end of the stairs
            GameObject wallN = Instantiate(wallPref, center + riseDir * (cellSize * 0.5f), Quaternion.Euler(0, rotY + 180f, 0), transform);
            wallN.transform.localScale = new Vector3(1.52f, 1.0f, 1.52f);
            generatedObjects.Add(wallN);

            // South wall (Y = y+1) - behind the low end of the stairs on the upper layer
            GameObject wallS_up = Instantiate(wallPref, center - riseDir * (cellSize * 0.5f) + new Vector3(0, cellHeight, 0), Quaternion.Euler(0, rotY, 0), transform);
            wallS_up.transform.localScale = new Vector3(1.52f, 1.0f, 1.52f);
            generatedObjects.Add(wallS_up);
        }
    }

    private bool IsConnected(int x, int y, int z)
    {
        if (x < 0 || x >= width || z < 0 || z >= height) return false;
        CellType type = grid[x, y, z].type;
        return type == CellType.Room || type == CellType.Corridor || type == CellType.Stairs;
    }

    private CorridorStyle GetCellCorridorStyle(int x, int y, int z)
    {
        if (x < 0 || x >= width || z < 0 || z >= height) return corridorStyle;
        if (grid[x, y, z].type != CellType.Corridor) return corridorStyle;
        
        // Gothic layers (even Y in Mixed theme) do not support circular sci-fi tunnels
        if (dungeonTheme == DungeonTheme.Mixed && y % 2 == 0)
        {
            return CorridorStyle.SquareCorridor;
        }

        if (corridorStyle == CorridorStyle.Mixed)
        {
            int cellHash = (x * 73856093) ^ (z * 19349663) ^ (y * 83492791) ^ seed;
            return (System.Math.Abs(cellHash) % 2 == 0) ? CorridorStyle.CircularTunnel : CorridorStyle.SquareCorridor;
        }
        return corridorStyle;
    }

    private GameObject GetFloorPrefab()
    {
        return (dungeonTheme == DungeonTheme.GothicRuins) ? gothicFloorPrefab : floorPrefab;
    }

    private GameObject GetCeilingPrefab()
    {
        return (dungeonTheme == DungeonTheme.GothicRuins) ? gothicCeilingPrefab : ceilingPrefab;
    }

    private GameObject GetWallPrefab()
    {
        return (dungeonTheme == DungeonTheme.GothicRuins) ? gothicWallPrefab : wallPrefab;
    }

    private GameObject GetDoorwayPrefab()
    {
        return (dungeonTheme == DungeonTheme.GothicRuins) ? gothicDoorwayPrefab : doorwayPrefab;
    }

    private GameObject GetCellFloorPrefab(int x, int y, int z)
    {
        if (dungeonTheme == DungeonTheme.Mixed)
        {
            return (y % 2 == 0) ? gothicFloorPrefab : floorPrefab;
        }
        return GetFloorPrefab();
    }

    private GameObject GetCellCeilingPrefab(int x, int y, int z)
    {
        if (dungeonTheme == DungeonTheme.Mixed)
        {
            return (y % 2 == 0) ? gothicCeilingPrefab : ceilingPrefab;
        }
        return GetCeilingPrefab();
    }

    private GameObject GetCellWallPrefab(int x, int y, int z)
    {
        if (dungeonTheme == DungeonTheme.Mixed)
        {
            return (y % 2 == 0) ? gothicWallPrefab : wallPrefab;
        }
        return GetWallPrefab();
    }

    private GameObject GetCellDoorwayPrefab(int x, int y, int z)
    {
        if (dungeonTheme == DungeonTheme.Mixed)
        {
            return (y % 2 == 0) ? gothicDoorwayPrefab : doorwayPrefab;
        }
        return GetDoorwayPrefab();
    }

    public string GetGridLayoutAsString()
    {
        System.Text.StringBuilder sb = new System.Text.StringBuilder();
        sb.AppendLine("=== LAYER 0 LAYOUT ===");
        for (int z = height - 1; z >= 0; z--)
        {
            for (int x = 0; x < width; x++)
            {
                if (grid == null) return "Grid is null";
                if (grid[x, 0, z].type == CellType.Room) sb.Append("R");
                else if (grid[x, 0, z].type == CellType.Corridor) sb.Append("C");
                else if (grid[x, 0, z].type == CellType.Stairs) sb.Append("S");
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

public class PathNode3D
{
    public Vector3Int Position { get; private set; }
    public float GCost { get; private set; }

    public PathNode3D(Vector3Int position, float gCost)
    {
        Position = position;
        GCost = gCost;
    }
}

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
