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
    public GameObject pillarPrefab;       // pillar_1 (Gothic Ruins)
    public GameObject bunkerPillarPrefab; // pillar_11 (Bunkers)
    public float stairsRotationOffset = 180f; // Offset to align stairs model forward with rising direction

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

    private HashSet<Vector3> spawnedPillarPositions = new HashSet<Vector3>();

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
            while (!stairPlaced && attempts < 500)
            {
                attempts++;
                int sx = Random.Range(2, width - 2);
                int sz = Random.Range(2, height - 2);

                // Shuffled rotations to evaluate all directions (North, East, South, West)
                List<int> rotations = new List<int> { 0, 90, 180, 270 };
                for (int i = 0; i < rotations.Count; i++)
                {
                    int temp = rotations[i];
                    int randomIndex = Random.Range(i, rotations.Count);
                    rotations[i] = rotations[randomIndex];
                    rotations[randomIndex] = temp;
                }

                foreach (int rot in rotations)
                {
                    Vector3Int riseDir = Vector3Int.zero;
                    Vector3Int entranceDir = Vector3Int.zero;
                    if (rot == 0) { riseDir = new Vector3Int(0, 0, 1); entranceDir = new Vector3Int(0, 0, -1); }
                    else if (rot == 90) { riseDir = new Vector3Int(1, 0, 0); entranceDir = new Vector3Int(-1, 0, 0); }
                    else if (rot == 180) { riseDir = new Vector3Int(0, 0, -1); entranceDir = new Vector3Int(0, 0, 1); }
                    else if (rot == 270) { riseDir = new Vector3Int(-1, 0, 0); entranceDir = new Vector3Int(1, 0, 0); }

                    int ex = sx + entranceDir.x;
                    int ez = sz + entranceDir.z;
                    int lx = sx + riseDir.x;
                    int lz = sz + riseDir.z;

                    // Grid bounds checks
                    if (ex < 1 || ex >= width - 1 || ez < 1 || ez >= height - 1 ||
                        lx < 1 || lx >= width - 1 || lz < 1 || lz >= height - 1)
                        continue;

                    CellType stairsType = grid[sx, y, sz].type;
                    CellType entranceType = grid[ex, y, ez].type;

                    // Place stairs if the cell itself and the entrance cell are Room/Corridor
                    if ((stairsType == CellType.Room || stairsType == CellType.Corridor) &&
                        (entranceType == CellType.Room || entranceType == CellType.Corridor))
                    {
                        grid[sx, y, sz].type = CellType.Stairs;
                        grid[sx, y, sz].rotation = rot;
                        grid[sx, y, sz].hasCeiling = false; // Carve ceiling so player can stand

                        // Carve the floor of the cell directly above the stairs
                        grid[sx, y + 1, sz].hasFloor = false;

                        // Ensure connection pathways on the upper layer are open
                        if (grid[sx, y + 1, sz].type == CellType.Empty) grid[sx, y + 1, sz].type = CellType.Corridor;
                        if (grid[lx, y + 1, lz].type == CellType.Empty) grid[lx, y + 1, lz].type = CellType.Corridor;

                        stairPlaced = true;
                        break;
                    }
                }
                if (stairPlaced) break;
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

                    if (y > 0 && grid[x, y - 1, z].type == CellType.Stairs)
                    {
                        // Skip normal room/corridor instantiation for the cell directly above the stairs
                        continue;
                    }

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
                float floorYOffset = 0.001f * ((x + z) % 2);
                Vector3 floorPos = center + new Vector3(0, floorYOffset, 0);
                GameObject floor = Instantiate(floorPref, floorPos, Quaternion.identity, transform);
                floor.transform.localScale = new Vector3(1.56f, 1.0f, 1.56f);
                generatedObjects.Add(floor);
            }
        }

        // 2. Spawn Ceiling (if hasCeiling is true)
        if (cell.hasCeiling)
        {
            GameObject ceilingPref = GetCellCeilingPrefab(x, y, z);
            if (ceilingPref != null)
            {
                float ceilingYOffset = 0.001f * ((x + z) % 2);
                Vector3 ceilingPos = center + new Vector3(0, cellHeight - 0.02f + ceilingYOffset, 0); // Offset downward by 2cm to avoid floor z-fighting
                GameObject ceiling = Instantiate(ceilingPref, ceilingPos, Quaternion.Euler(180, 0, 0), transform);
                ceiling.transform.localScale = new Vector3(1.56f, 1.0f, 1.56f);
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

        // 1. If current cell is directly above a Stairs cell, it's the stairs upper shaft
        if (y > 0 && grid[x, y - 1, z].type == CellType.Stairs)
        {
            return; // Boundaries of the upper stairs shaft are handled by neighbors or InstantiateStairs
        }

        // Compute wall normal offset to resolve parallel wall z-fighting
        Vector3 wallNormal = new Vector3(dir.x, 0, dir.y);
        float wallOffsetVal = 0f; // Set to 0 to prevent 1mm offset seams!
        Vector3 finalOffset = offset + wallNormal * wallOffsetVal;

        // 2. If neighbor is the upper cell of stairs
        if (y > 0 && nx >= 0 && nx < width && nz >= 0 && nz < height && grid[nx, y - 1, nz].type == CellType.Stairs)
        {
            if (!IsStairsUpperExit(nx, y - 1, nz, x, y, z))
            {
                GameObject wallPref = GetCellWallPrefab(x, y, z);
                if (wallPref != null)
                {
                    GameObject wall = Instantiate(wallPref, center + finalOffset, rotation, transform);
                    wall.transform.localScale = new Vector3(1.56f, 1.0f, 1.56f);
                    generatedObjects.Add(wall);
                    // Spawn pillars at both ends of this wall to cover seams!
                    SpawnDoorwayCornersPillars(x, y, z, center, dir);
                }
            }
            else
            {
                // It is the exit of the stairs! Spawn a doorway transition into the room
                GameObject doorwayPref = GetCellDoorwayPrefab(x, y, z);
                if (doorwayPref != null)
                {
                    GameObject doorway = Instantiate(doorwayPref, center + finalOffset, rotation, transform);
                    if (doorwayPref.name.ToLower().Contains("arc"))
                    {
                        doorway.transform.localScale = new Vector3(2.12f, 1.0f, 1.56f);
                    }
                    else
                    {
                        doorway.transform.localScale = new Vector3(1.56f, 1.0f, 1.56f);
                    }
                    if (doorwayPref.name.Contains("doorway") || doorwayPref.name.Contains("arc"))
                    {
                        doorway.AddComponent<PhysicalDoor>();
                    }
                    generatedObjects.Add(doorway);
                    
                    // Spawn support pillars to cover seams between the doorframe columns and adjacent walls
                    SpawnDoorwayCornersPillars(x, y, z, center, dir);
                }
            }
            return;
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
                        GameObject wall = Instantiate(wallPref, center + finalOffset, rotation, transform);
                        wall.transform.localScale = new Vector3(1.56f, 1.0f, 1.56f);
                        generatedObjects.Add(wall);
                        // Spawn pillars at both ends of this wall to cover seams!
                        SpawnDoorwayCornersPillars(x, y, z, center, dir);
                    }
                    return;
                }
            }

            GameObject doorwayPref = GetCellDoorwayPrefab(x, y, z);
            if (doorwayPref != null)
            {
                GameObject doorway = Instantiate(doorwayPref, center + finalOffset, rotation, transform);
                if (doorwayPref.name.ToLower().Contains("arc"))
                {
                    doorway.transform.localScale = new Vector3(2.12f, 1.0f, 1.56f);
                }
                else
                {
                    doorway.transform.localScale = new Vector3(1.56f, 1.0f, 1.56f);
                }
                if (doorwayPref.name.Contains("doorway") || doorwayPref.name.Contains("arc"))
                {
                    doorway.AddComponent<PhysicalDoor>();
                }
                generatedObjects.Add(doorway);

                // Spawn support pillars to cover seams between the doorframe columns and adjacent walls
                SpawnDoorwayCornersPillars(x, y, z, center, dir);
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
                GameObject wall = Instantiate(wallPref, center + finalOffset, rotation, transform);
                wall.transform.localScale = new Vector3(1.56f, 1.0f, 1.56f);
                generatedObjects.Add(wall);
                // Spawn pillars at both ends of this wall to cover seams!
                SpawnDoorwayCornersPillars(x, y, z, center, dir);
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
                float floorYOffset = 0.001f * ((x + z) % 2);
                Vector3 floorPos = center + new Vector3(0, floorYOffset, 0);
                GameObject floor = Instantiate(floorPref, floorPos, Quaternion.identity, transform);
                floor.transform.localScale = new Vector3(1.56f, 1.0f, 1.56f);
                generatedObjects.Add(floor);
            }
        }

        // 2. Spawn Ceiling
        if (cell.hasCeiling)
        {
            GameObject ceilingPref = GetCellCeilingPrefab(x, y, z);
            if (ceilingPref != null)
            {
                float ceilingYOffset = 0.001f * ((x + z) % 2);
                Vector3 ceilingPos = center + new Vector3(0, cellHeight - 0.02f + ceilingYOffset, 0); // Offset downward by 2cm to avoid floor z-fighting
                GameObject ceiling = Instantiate(ceilingPref, ceilingPos, Quaternion.Euler(180, 0, 0), transform);
                ceiling.transform.localScale = new Vector3(1.56f, 1.0f, 1.56f);
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

        // 1. If current cell is directly above a Stairs cell, it's the stairs upper shaft
        if (y > 0 && grid[x, y - 1, z].type == CellType.Stairs)
        {
            return; // Boundaries of the upper stairs shaft are handled by neighbors or InstantiateStairs
        }

        // Compute wall normal offset to resolve parallel wall z-fighting
        Vector3 wallNormal = new Vector3(dir.x, 0, dir.y);
        float wallOffsetVal = 0f; // Set to 0 to prevent 1mm offset seams!
        Vector3 finalOffset = offset + wallNormal * wallOffsetVal;

        // 2. If neighbor is the upper cell of stairs
        if (y > 0 && nx >= 0 && nx < width && nz >= 0 && nz < height && grid[nx, y - 1, nz].type == CellType.Stairs)
        {
            if (!IsStairsUpperExit(nx, y - 1, nz, x, y, z))
            {
                GameObject wallPref = GetCellWallPrefab(x, y, z);
                if (wallPref != null)
                {
                    GameObject wall = Instantiate(wallPref, center + finalOffset, rotation, transform);
                    wall.transform.localScale = new Vector3(1.56f, 1.0f, 1.56f);
                    generatedObjects.Add(wall);
                    // Spawn pillars at both ends of this wall to cover seams!
                    SpawnDoorwayCornersPillars(x, y, z, center, dir);
                }
            }
            else
            {
                // It is the exit of the stairs! Spawn a doorway transition into the corridor
                GameObject doorwayPref = GetCellDoorwayPrefab(x, y, z);
                if (doorwayPref != null)
                {
                    GameObject doorway = Instantiate(doorwayPref, center + finalOffset, rotation, transform);
                    if (doorwayPref.name.ToLower().Contains("arc"))
                    {
                        doorway.transform.localScale = new Vector3(2.12f, 1.0f, 1.56f);
                    }
                    else
                    {
                        doorway.transform.localScale = new Vector3(1.56f, 1.0f, 1.56f);
                    }
                    if (doorwayPref.name.Contains("doorway") || doorwayPref.name.Contains("arc"))
                    {
                        doorway.AddComponent<PhysicalDoor>();
                    }
                    generatedObjects.Add(doorway);
                    
                    // Spawn support pillars to cover seams between the doorframe columns and adjacent walls
                    SpawnDoorwayCornersPillars(x, y, z, center, dir);
                }
            }
            return;
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
                GameObject wall = Instantiate(wallPref, center + finalOffset, rotation, transform);
                wall.transform.localScale = new Vector3(1.56f, 1.0f, 1.56f);
                generatedObjects.Add(wall);
                // Spawn pillars at both ends of this wall to cover seams!
                SpawnDoorwayCornersPillars(x, y, z, center, dir);
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
                        GameObject wall = Instantiate(wallPref, center + finalOffset, rotation, transform);
                        wall.transform.localScale = new Vector3(1.56f, 1.0f, 1.56f);
                        generatedObjects.Add(wall);
                        // Spawn pillars at both ends of this wall to cover seams!
                        SpawnDoorwayCornersPillars(x, y, z, center, dir);
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
                    GameObject doorway = Instantiate(doorwayPref, center + finalOffset, rotation, transform);
                    if (doorwayPref.name.ToLower().Contains("arc"))
                    {
                        doorway.transform.localScale = new Vector3(2.12f, 1.0f, 1.56f);
                    }
                    else
                    {
                        doorway.transform.localScale = new Vector3(1.56f, 1.0f, 1.56f);
                    }
                    if (doorwayPref.name.Contains("doorway") || doorwayPref.name.Contains("arc"))
                    {
                        doorway.AddComponent<PhysicalDoor>();
                    }
                    generatedObjects.Add(doorway);

                    // Spawn support pillars to cover seams between the doorframe columns and adjacent walls
                    SpawnDoorwayCornersPillars(x, y, z, center, dir);
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
            // Apply stairsModelRotationOffset to match the physical model to layout rise direction
            Quaternion stairsInstanceRot = stairsRot * Quaternion.Euler(0, stairsRotationOffset, 0);
            GameObject stairsInstance = Instantiate(activeStairsPrefab, center, stairsInstanceRot, transform);
            
            // Get the local bounds of the prefab mesh to align exactly with cell boundaries
            float zMin = -2.0f;
            float zMax = 2.0f;
            float originalLength = 4.0f;
            float originalWidth = 2.0f;
            
            MeshFilter[] meshFilters = stairsInstance.GetComponentsInChildren<MeshFilter>();
            if (meshFilters.Length > 0)
            {
                Bounds combinedBounds = new Bounds(Vector3.zero, Vector3.zero);
                bool hasBounds = false;
                
                foreach (var mf in meshFilters)
                {
                    if (mf.sharedMesh != null)
                    {
                        Bounds localB = mf.sharedMesh.bounds;
                        Vector3[] corners = new Vector3[8];
                        Vector3 ext = localB.extents;
                        Vector3 cnt = localB.center;
                        corners[0] = cnt + new Vector3(ext.x, ext.y, ext.z);
                        corners[1] = cnt + new Vector3(ext.x, ext.y, -ext.z);
                        corners[2] = cnt + new Vector3(ext.x, -ext.y, ext.z);
                        corners[3] = cnt + new Vector3(ext.x, -ext.y, -ext.z);
                        corners[4] = cnt - new Vector3(ext.x, ext.y, ext.z);
                        corners[5] = cnt - new Vector3(ext.x, ext.y, -ext.z);
                        corners[6] = cnt - new Vector3(ext.x, -ext.y, ext.z);
                        corners[7] = cnt - new Vector3(ext.x, -ext.y, -ext.z);
                        
                        for (int i = 0; i < 8; i++)
                        {
                            Vector3 localCorner = stairsInstance.transform.InverseTransformPoint(mf.transform.TransformPoint(corners[i]));
                            if (!hasBounds)
                            {
                                combinedBounds = new Bounds(localCorner, Vector3.zero);
                                hasBounds = true;
                            }
                            else
                            {
                                combinedBounds.Encapsulate(localCorner);
                            }
                        }
                    }
                }
                
                if (hasBounds)
                {
                    zMin = combinedBounds.min.z;
                    zMax = combinedBounds.max.z;
                    originalLength = combinedBounds.size.z;
                    originalWidth = combinedBounds.size.x;
                }
            }

            // Scale dynamically: width matches cell width, length scaled to span full cell size (6.0m)
            float lengthScale = cellSize / originalLength;
            float widthScale = (originalWidth > 0.1f) ? (cellSize / originalWidth) : 1.5f;
            
            if (activeStairsPrefab.name.ToLower().Contains("stairs_5"))
            {
                widthScale = cellSize / 2.0f; // 3.0f
            }
            else
            {
                widthScale = 1.5f;
            }

            stairsInstance.transform.localScale = new Vector3(widthScale, 1.0f, lengthScale);
            
            // Translate along local Z axis to align bottom/top of stairs perfectly to cell boundaries (eliminating entrance gap)
            float localZOffset = -cellSize * 0.5f - lengthScale * zMin;
            stairsInstance.transform.position += stairsInstanceRot * new Vector3(0, 0, localZOffset);
            
            generatedObjects.Add(stairsInstance);
        }

        // 2. Spawn Floor under stairs (with checkerboard Y offset)
        if (cell.hasFloor)
        {
            GameObject floorPref = GetCellFloorPrefab(x, y, z);
            if (floorPref != null)
            {
                float floorYOffset = 0.001f * ((x + z) % 2);
                Vector3 floorPos = center + new Vector3(0, floorYOffset, 0);
                GameObject floor = Instantiate(floorPref, floorPos, Quaternion.identity, transform);
                floor.transform.localScale = new Vector3(1.56f, 1.0f, 1.56f);
                generatedObjects.Add(floor);
            }
        }

        // 3. Spawn Ceiling for the upper cell (above stairs, with checkerboard Y offset)
        GameObject ceilingPref = GetCellCeilingPrefab(x, y + 1, z);
        if (ceilingPref != null)
        {
            float ceilingYOffset = 0.001f * ((x + z) % 2);
            Vector3 ceilingPos = center + new Vector3(0, cellHeight * 2.0f - 0.02f + ceilingYOffset, 0); // Offset downward by 2cm to avoid floor z-fighting
            GameObject ceiling = Instantiate(ceilingPref, ceilingPos, Quaternion.Euler(180, 0, 0), transform);
            ceiling.transform.localScale = new Vector3(1.56f, 1.0f, 1.56f);
            generatedObjects.Add(ceiling);
        }

        // 4. Spawn Enclosing Walls (only if adjacent cell is Empty/CircularTunnel/other non-wall-spawning cells to prevent duplicate z-fighting walls)
        GameObject wallPref = GetCellWallPrefab(x, y, z);
        if (wallPref != null)
        {
            Vector3Int vRiseDir = Vector3Int.RoundToInt(riseDir);
            Vector3Int vSideDir = Vector3Int.RoundToInt(sideDir);

            // East wall (Y = y)
            if (ShouldSpawnStairsWall(x, y, z, vSideDir))
            {
                Vector3 wallPosE = center + sideDir * (cellSize * 0.5f);
                GameObject wallE = Instantiate(wallPref, wallPosE, Quaternion.Euler(0, rotY - 90f, 0), transform);
                wallE.transform.localScale = new Vector3(1.56f, 1.0f, 1.56f);
                generatedObjects.Add(wallE);
            }

            // West wall (Y = y)
            if (ShouldSpawnStairsWall(x, y, z, -vSideDir))
            {
                Vector3 wallPosW = center - sideDir * (cellSize * 0.5f);
                GameObject wallW = Instantiate(wallPref, wallPosW, Quaternion.Euler(0, rotY + 90f, 0), transform);
                wallW.transform.localScale = new Vector3(1.56f, 1.0f, 1.56f);
                generatedObjects.Add(wallW);
            }

            // East upper wall (Y = y+1)
            if (ShouldSpawnStairsWall(x, y, z, vSideDir + new Vector3Int(0, 1, 0)))
            {
                Vector3 wallPosE_up = center + sideDir * (cellSize * 0.5f) + new Vector3(0, cellHeight, 0);
                GameObject wallE_up = Instantiate(wallPref, wallPosE_up, Quaternion.Euler(0, rotY - 90f, 0), transform);
                wallE_up.transform.localScale = new Vector3(1.56f, 1.0f, 1.56f);
                generatedObjects.Add(wallE_up);
            }

            // West upper wall (Y = y+1)
            if (ShouldSpawnStairsWall(x, y, z, -vSideDir + new Vector3Int(0, 1, 0)))
            {
                Vector3 wallPosW_up = center - sideDir * (cellSize * 0.5f) + new Vector3(0, cellHeight, 0);
                GameObject wallW_up = Instantiate(wallPref, wallPosW_up, Quaternion.Euler(0, rotY + 90f, 0), transform);
                wallW_up.transform.localScale = new Vector3(1.56f, 1.0f, 1.56f);
                generatedObjects.Add(wallW_up);
            }

            // North wall (Y = y) - under the high end of the stairs
            if (ShouldSpawnStairsWall(x, y, z, vRiseDir))
            {
                Vector3 wallPosN = center + riseDir * (cellSize * 0.5f);
                GameObject wallN = Instantiate(wallPref, wallPosN, Quaternion.Euler(0, rotY + 180f, 0), transform);
                wallN.transform.localScale = new Vector3(1.56f, 1.0f, 1.56f);
                generatedObjects.Add(wallN);
            }

            // South wall (Y = y+1) - behind the low end of the stairs on the upper layer
            if (ShouldSpawnStairsWall(x, y, z, -vRiseDir + new Vector3Int(0, 1, 0)))
            {
                Vector3 wallPosS_up = center - riseDir * (cellSize * 0.5f) + new Vector3(0, cellHeight, 0);
                GameObject wallS_up = Instantiate(wallPref, wallPosS_up, Quaternion.Euler(0, rotY, 0), transform);
                wallS_up.transform.localScale = new Vector3(1.56f, 1.0f, 1.56f);
                generatedObjects.Add(wallS_up);
            }
        }

        // 5. Spawn Corner Support Pillars (seals gaps between doorframe/walls)
        // Lower layer (Y = y) pillars
        GameObject lowerPillarPref = (y % 2 == 0) ? pillarPrefab : (bunkerPillarPrefab != null ? bunkerPillarPrefab : pillarPrefab);
        if (lowerPillarPref != null)
        {
            Vector3[] lowerCorners = new Vector3[] {
                center - riseDir * (cellSize * 0.5f) - sideDir * (cellSize * 0.5f),
                center - riseDir * (cellSize * 0.5f) + sideDir * (cellSize * 0.5f),
                center + riseDir * (cellSize * 0.5f) - sideDir * (cellSize * 0.5f),
                center + riseDir * (cellSize * 0.5f) + sideDir * (cellSize * 0.5f)
            };
            foreach (var pos in lowerCorners)
            {
                SpawnPillarAt(lowerPillarPref, pos);
            }
        }

        // Upper layer (Y = y+1) pillars
        GameObject upperPillarPref = ((y + 1) % 2 == 0) ? pillarPrefab : (bunkerPillarPrefab != null ? bunkerPillarPrefab : pillarPrefab);
        if (upperPillarPref != null)
        {
            Vector3[] upperCorners = new Vector3[] {
                center - riseDir * (cellSize * 0.5f) - sideDir * (cellSize * 0.5f) + new Vector3(0, cellHeight, 0),
                center - riseDir * (cellSize * 0.5f) + sideDir * (cellSize * 0.5f) + new Vector3(0, cellHeight, 0),
                center + riseDir * (cellSize * 0.5f) - sideDir * (cellSize * 0.5f) + new Vector3(0, cellHeight, 0),
                center + riseDir * (cellSize * 0.5f) + sideDir * (cellSize * 0.5f) + new Vector3(0, cellHeight, 0)
            };
            foreach (var pos in upperCorners)
            {
                SpawnPillarAt(upperPillarPref, pos);
            }
        }

        // 6. Spawn Transition Doorways and Pillars if adjacent to a Circular Tunnel
        Vector3Int vRiseDir2 = Vector3Int.RoundToInt(riseDir);
        
        // Transition doorway at entrance
        Vector3Int entranceCoord = new Vector3Int(x - vRiseDir2.x, y, z - vRiseDir2.z);
        if (entranceCoord.x >= 0 && entranceCoord.x < width && entranceCoord.z >= 0 && entranceCoord.z < height)
        {
            if (grid[entranceCoord.x, y, entranceCoord.z].type == CellType.Corridor && 
                GetCellCorridorStyle(entranceCoord.x, y, entranceCoord.z) == CorridorStyle.CircularTunnel)
            {
                GameObject doorwayPref = GetCellDoorwayPrefab(x, y, z);
                if (doorwayPref != null)
                {
                    float wallOffsetVal = 0f;
                    Vector3 doorwayPos = center - riseDir * (cellSize * 0.5f) - riseDir * wallOffsetVal;
                    GameObject doorway = Instantiate(doorwayPref, doorwayPos, Quaternion.Euler(0, rotY, 0), transform);
                    if (doorwayPref.name.ToLower().Contains("arc"))
                    {
                        doorway.transform.localScale = new Vector3(2.12f, 1.0f, 1.56f);
                    }
                    else
                    {
                        doorway.transform.localScale = new Vector3(1.56f, 1.0f, 1.56f);
                    }
                    generatedObjects.Add(doorway);
                    
                    SpawnDoorwayCornersPillars(x, y, z, center, -new Vector2Int(vRiseDir2.x, vRiseDir2.z));
                }
            }
        }

        // Transition doorway at exit
        Vector3Int exitCoord = new Vector3Int(x + vRiseDir2.x, y + 1, z + vRiseDir2.z);
        if (exitCoord.x >= 0 && exitCoord.x < width && exitCoord.z >= 0 && exitCoord.z < height)
        {
            if (grid[exitCoord.x, y + 1, exitCoord.z].type == CellType.Corridor && 
                GetCellCorridorStyle(exitCoord.x, y + 1, exitCoord.z) == CorridorStyle.CircularTunnel)
            {
                GameObject doorwayPref = GetCellDoorwayPrefab(x, y + 1, z);
                if (doorwayPref != null)
                {
                    float wallOffsetVal = 0f;
                    Vector3 doorwayPos = center + riseDir * (cellSize * 0.5f) + new Vector3(0, cellHeight, 0) + riseDir * wallOffsetVal;
                    GameObject doorway = Instantiate(doorwayPref, doorwayPos, Quaternion.Euler(0, rotY + 180f, 0), transform);
                    if (doorwayPref.name.ToLower().Contains("arc"))
                    {
                        doorway.transform.localScale = new Vector3(2.12f, 1.0f, 1.56f);
                    }
                    else
                    {
                        doorway.transform.localScale = new Vector3(1.56f, 1.0f, 1.56f);
                    }
                    generatedObjects.Add(doorway);
                    
                    SpawnDoorwayCornersPillars(x, y + 1, z, center + new Vector3(0, cellHeight, 0), new Vector2Int(vRiseDir2.x, vRiseDir2.z));
                }
            }
        }
    }

    private bool IsStairsUpperExit(int stairsX, int stairsY, int stairsZ, int targetX, int targetY, int targetZ)
    {
        if (grid == null) return false;
        if (stairsX < 0 || stairsX >= width || stairsY < 0 || stairsY >= layers || stairsZ < 0 || stairsZ >= height) return false;
        if (grid[stairsX, stairsY, stairsZ].type != CellType.Stairs) return false;
        float rotY = grid[stairsX, stairsY, stairsZ].rotation;
        Quaternion stairsRot = Quaternion.Euler(0, rotY, 0);
        Vector3Int riseDir = Vector3Int.RoundToInt(stairsRot * Vector3.forward);
        return (targetX == stairsX + riseDir.x && targetY == stairsY + 1 && targetZ == stairsZ + riseDir.z);
    }

    private bool ShouldSpawnStairsWall(int sx, int sy, int sz, Vector3Int dir)
    {
        int nx = sx + dir.x;
        int ny = sy + dir.y;
        int nz = sz + dir.z;

        if (nx < 0 || nx >= width || ny < 0 || ny >= layers || nz < 0 || nz >= height)
        {
            return true; // Out of bounds, spawn a wall
        }

        if (grid == null) return true;

        DungeonCell neighbor = grid[nx, ny, nz];
        bool isRoom = neighbor.type == CellType.Room;
        bool isSquareCorridor = neighbor.type == CellType.Corridor && GetCellCorridorStyle(nx, ny, nz) == CorridorStyle.SquareCorridor;

        return !(isRoom || isSquareCorridor);
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
        spawnedPillarPositions.Clear();
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

    private void SpawnPillarAt(GameObject prefab, Vector3 pos)
    {
        if (prefab == null) return;
        
        Vector3 key = new Vector3(
            Mathf.Round(pos.x * 100f) / 100f,
            Mathf.Round(pos.y * 100f) / 100f,
            Mathf.Round(pos.z * 100f) / 100f
        );

        if (spawnedPillarPositions.Contains(key)) return;

        GameObject pillar = Instantiate(prefab, pos, Quaternion.identity, transform);
        generatedObjects.Add(pillar);
        spawnedPillarPositions.Add(key);
    }

    private void SpawnDoorwayCornersPillars(int x, int y, int z, Vector3 center, Vector2Int dir)
    {
        GameObject pillarPref = (y % 2 == 0) ? pillarPrefab : (bunkerPillarPrefab != null ? bunkerPillarPrefab : pillarPrefab);
        if (pillarPref == null) return;

        Vector3 p1 = Vector3.zero;
        Vector3 p2 = Vector3.zero;

        if (dir.x != 0)
        {
            p1 = center + new Vector3(dir.x * cellSize * 0.5f, 0, cellSize * 0.5f);
            p2 = center + new Vector3(dir.x * cellSize * 0.5f, 0, -cellSize * 0.5f);
        }
        else if (dir.y != 0)
        {
            p1 = center + new Vector3(cellSize * 0.5f, 0, dir.y * cellSize * 0.5f);
            p2 = center + new Vector3(-cellSize * 0.5f, 0, dir.y * cellSize * 0.5f);
        }

        SpawnPillarAt(pillarPref, p1);
        SpawnPillarAt(pillarPref, p2);
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
