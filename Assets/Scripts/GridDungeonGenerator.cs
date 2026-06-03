using UnityEngine;
using System.Collections.Generic;

public class GridDungeonGenerator : MonoBehaviour
{
    public enum CellType
    {
        Empty,
        Room,
        Corridor,
        Doorway,
        Stairs
    }

    public enum DungeonTheme
    {
        Bunker,
        GothicRuins,
        Mixed
    }

    public enum CorridorStyle
    {
        CircularTunnel,
        SquareCorridor,
        Mixed
    }

    [System.Serializable]
    public struct DungeonCell
    {
        public CellType type;
        public int roomId;
        public int rotation; // Euler Y rotation for stairs, doors, etc.
        public bool hasFloor;
        public bool hasCeiling;
        public string floorPrefabName; // Overrides for specific themes
        public string wallPrefabName;  // Overrides for specific themes
    }

    [System.Serializable]
    public class Room
    {
        public int id;
        public string theme;
        public int x; // Grid X start
        public int y; // Grid Y start (bottom layer)
        public int z; // Grid Z start
        public int w; // Width in cells
        public int h; // Depth in cells
        public int layersCount; // Height in cells
        public List<Vector3Int> entrancePoints = new List<Vector3Int>();
        public List<Vector3Int> exitPoints = new List<Vector3Int>();
    }

    [Header("Standard Prefab Assignments (Fallbacks)")]
    public GameObject floorPrefab;
    public GameObject wallPrefab;
    public GameObject ceilingPrefab;
    public GameObject doorwayPrefab;

    [Header("Gothic Theme Prefabs")]
    public GameObject gothicFloorPrefab;
    public GameObject gothicWallPrefab;
    public GameObject gothicCeilingPrefab;
    public GameObject gothicDoorwayPrefab;

    [Header("Tunnel Prefabs (Deprecated)")]
    public GameObject tunnelStraight;
    public GameObject tunnelCorner;
    public GameObject tunnelTJunction;
    public GameObject tunnelXJunction;

    [Header("Staircase Prefabs")]
    public GameObject stairsPrefab;
    public GameObject bunkerStairsPrefab;
    public GameObject pillarPrefab;
    public GameObject bunkerPillarPrefab;

    [Header("Theme & Scale Configuration")]
    public DungeonTheme dungeonTheme = DungeonTheme.Mixed;
    public CorridorStyle corridorStyle = CorridorStyle.SquareCorridor;
    public float cellSize = 4.0f;
    public float cellHeight = 4.0f;
    public Vector3 prefabScale = Vector3.one;

    [Header("Layout Settings")]
    public int width = 24;
    public int height = 24;
    public int layers = 3;
    public bool useRandomSeed = true;
    public int seed = 1337;

    // Kept for inspector layout compatibility
    [HideInInspector] public int minRoomSize = 2;
    [HideInInspector] public int maxRoomSize = 3;
    [HideInInspector] public int roomsPerLayer = 3;
    [HideInInspector] public float roomDensity = 0.2f;
    [HideInInspector] public float stairsRotationOffset = 180f;

    [HideInInspector]
    [SerializeField]
    private List<GameObject> generatedObjects = new List<GameObject>();

    private HashSet<Vector3> spawnedPillarPositions = new HashSet<Vector3>();
    private DungeonCell[,,] grid;
    private List<Room> rooms = new List<Room>();

#if UNITY_EDITOR
    private GameObject LoadPrefab(string name)
    {
        string path = "Assets/Prefabs/KayKit Dungeon/" + name + ".prefab";
        GameObject prefab = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(path);
        if (prefab == null)
        {
            Debug.LogError($"[GridDungeonGenerator] Prefab not found at path: {path}");
        }
        return prefab;
    }
#endif

    [ContextMenu("Generate Dungeon")]
    public void GenerateDungeon()
    {
        int attemptsToPlaceAll = 0;
        bool allRoomsPlaced = false;

        string[] themes = {
            "DividingHall",   // Y=0, 3x3
            "JailCells",      // Y=0, 2x2
            "Cellar",         // Y=0, 3x3
            "GuardPost",      // Y=0..1, 2x2 (Climb from 0 to 1)
            "MageLibrary",    // Y=1, 2x2
            "GoldTreasury",   // Y=1, 2x2
            "SewerDock",      // Y=0..1, 2x2 (Descend from 1 to 0)
            "CastleHall",     // Y=0..2, 3x3 (Climb from 0 to 2)
            "MineShaft",      // Y=1..2, 3x3 (Descend from 2 to 1)
            "LivingQuarters", // Y=0..1, 2x2 (Descend from 1 to 0)
            "TreasureVault"   // Y=0, 2x2
        };

        while (!allRoomsPlaced && attemptsToPlaceAll < 100)
        {
            attemptsToPlaceAll++;
            ClearDungeon();

            // 1. Force grid properties for our 11 themed rooms and vertical transitions
            width = 24;
            height = 24;
            layers = 3;
            cellSize = 4.0f;
            cellHeight = 4.0f;
            prefabScale = Vector3.one;

            if (useRandomSeed)
            {
                // Use System.Random to ensure seed changes in Editor Mode when clicking Tools
                seed = new System.Random().Next(0, 1000000);
            }
            Random.InitState(seed);

            // 2. Initialize Grid
            grid = new DungeonCell[width, layers, height];
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
                            hasFloor = false,
                            hasCeiling = false,
                            floorPrefabName = "",
                            wallPrefabName = ""
                        };
                    }
                }
            }

            // 3. Layout Rooms sequentially to build our rogue-like graph
            rooms.Clear();
            allRoomsPlaced = true;

            for (int i = 0; i < themes.Length; i++)
            {
                string theme = themes[i];
                int rw = 2;
                int rh = 2;
                int rlayers = 1;
                int startY = 0;

                // Define room sizes
                if (theme == "DividingHall" || theme == "Cellar" || theme == "CastleHall" || theme == "MineShaft")
                {
                    rw = 3;
                    rh = 3;
                }
                if (theme == "GuardPost" || theme == "SewerDock" || theme == "LivingQuarters" || theme == "MineShaft")
                {
                    rlayers = 2;
                }
                if (theme == "CastleHall")
                {
                    rlayers = 3;
                }

                // Set starting layer based on our vertical flow sequence
                if (theme == "MageLibrary" || theme == "GoldTreasury") startY = 1;
                if (theme == "MineShaft") startY = 1; // spans Y=1..2

                bool placed = false;
                for (int attempt = 0; attempt < 500; attempt++)
                {
                    int rx = Random.Range(1, width - rw - 1);
                    int rz = Random.Range(1, height - rh - 1);

                    if (CanPlaceRoom(rx, startY, rz, rw, rlayers, rh))
                    {
                        Room r = new Room
                        {
                            id = i + 1,
                            theme = theme,
                            x = rx,
                            y = startY,
                            z = rz,
                            w = rw,
                            h = rh,
                            layersCount = rlayers
                        };

                        DefineRoomEntryExits(r);
                        rooms.Add(r);
                        MarkRoomInGrid(r);
                        placed = true;
                        break;
                    }
                }

                if (!placed)
                {
                    allRoomsPlaced = false;
                    break; // Restart with new seed if we failed to place any room
                }
            }
        }

        if (!allRoomsPlaced)
        {
            Debug.LogError("[GridDungeonGenerator] Failed to place all 11 rooms after 100 attempts!");
            return;
        }

        // 4. Connect Rooms sequentially using A* pathfinding on their respective connection layers
        for (int i = 0; i < rooms.Count - 1; i++)
        {
            Room rA = rooms[i];
            Room rB = rooms[i + 1];
            if (rA.exitPoints.Count > 0 && rB.entrancePoints.Count > 0)
            {
                Vector3Int exit = rA.exitPoints[0];
                Vector3Int entrance = rB.entrancePoints[0];
                ConnectCellsOnLayer(exit, entrance, exit.y);
            }
        }

        // Ensure doorway neighbors are connected (to prevent doors to void and guarantee room access)
        for (int i = 0; i < rooms.Count; i++)
        {
            Room r = rooms[i];
            
            // Handle entrance points (West facing outwards)
            if (r.entrancePoints.Count > 0 && r.id > 1) // First room has no entrance door
            {
                Vector3Int ent = r.entrancePoints[0];
                int nx = ent.x - 1;
                if (nx >= 0 && nx < width)
                {
                    if (grid[nx, ent.y, ent.z].type == CellType.Empty)
                    {
                        grid[nx, ent.y, ent.z].type = CellType.Corridor;
                        grid[nx, ent.y, ent.z].hasFloor = true;
                        grid[nx, ent.y, ent.z].hasCeiling = true;
                    }
                }
            }

            // Handle exit points (East facing outwards)
            if (r.exitPoints.Count > 0 && r.id < 11) // Last room has no exit door
            {
                Vector3Int ex = r.exitPoints[0];
                int nx = ex.x + 1;
                if (nx >= 0 && nx < width)
                {
                    if (grid[nx, ex.y, ex.z].type == CellType.Empty)
                    {
                        grid[nx, ex.y, ex.z].type = CellType.Corridor;
                        grid[nx, ex.y, ex.z].hasFloor = true;
                        grid[nx, ex.y, ex.z].hasCeiling = true;
                    }
                }
            }
        }

        // 5. Instantiate all environmental geometries (walls, floors, columns, etc.)
        InstantiateDungeon();

        // 6. Decorate rooms with theme-specific props, lights, and banners
        foreach (var room in rooms)
        {
            DecorateRoom(room);
        }
    }

    private bool CanPlaceRoom(int rx, int ry, int rz, int rw, int rlayers, int rh)
    {
        if (rx < 1 || rx + rw >= width - 1 || rz < 1 || rz + rh >= height - 1) return false;
        if (ry < 0 || ry + rlayers > layers) return false;

        // Check for overlap with 1-cell border buffer
        for (int x = rx - 1; x <= rx + rw; x++)
        {
            for (int z = rz - 1; z <= rz + rh; z++)
            {
                for (int y = ry; y < ry + rlayers; y++)
                {
                    if (grid[x, y, z].type != CellType.Empty)
                    {
                        return false;
                    }
                }
            }
        }
        return true;
    }

    private void DefineRoomEntryExits(Room r)
    {
        int entranceY = r.y;
        int exitY = r.y + r.layersCount - 1;

        // Special layer adjustments for vertical progressions
        if (r.theme == "SewerDock" || r.theme == "LivingQuarters")
        {
            entranceY = r.y + 1;
            exitY = r.y;
        }
        else if (r.theme == "MineShaft")
        {
            entranceY = r.y + 1; // Spans Y=1..2. Entrance at upper level Y=2.
            exitY = r.y;         // Exit at lower level Y=1.
        }

        // Entrance on West wall, Exit on East wall
        Vector3Int ent = new Vector3Int(r.x, entranceY, r.z + r.h / 2);
        Vector3Int ex = new Vector3Int(r.x + r.w - 1, exitY, r.z + r.h / 2);

        r.entrancePoints.Add(ent);
        r.exitPoints.Add(ex);
    }

    private void MarkRoomInGrid(Room r)
    {
        for (int x = r.x; x < r.x + r.w; x++)
        {
            for (int z = r.z; z < r.z + r.h; z++)
            {
                for (int y = r.y; y < r.y + r.layersCount; y++)
                {
                    grid[x, y, z].type = CellType.Room;
                    grid[x, y, z].roomId = r.id;
                    grid[x, y, z].hasFloor = (y == r.y); // Floor on bottom layer by default
                    grid[x, y, z].hasCeiling = (y == r.y + r.layersCount - 1); // Ceiling on top layer by default
                }
            }
        }

        // Apply platform layout floor rules for double-tier rooms
        if (r.theme == "GuardPost")
        {
            grid[r.x, r.y + 1, r.z].hasFloor = false;
            grid[r.x + 1, r.y + 1, r.z].hasFloor = false;
            grid[r.x, r.y + 1, r.z + 1].hasFloor = true;
            grid[r.x + 1, r.y + 1, r.z + 1].hasFloor = true;
        }
        else if (r.theme == "SewerDock")
        {
            grid[r.x, r.y + 1, r.z].hasFloor = false;
            grid[r.x, r.y + 1, r.z + 1].hasFloor = false;
            grid[r.x + 1, r.y + 1, r.z].hasFloor = true;
            grid[r.x + 1, r.y + 1, r.z + 1].hasFloor = true;
        }
        else if (r.theme == "CastleHall")
        {
            // Y=1 side balconies (Balcony on x == r.x and x == r.x + 2)
            // But we must open a hole for Stair 1 at (r.x, 1, r.z)
            for (int x = r.x; x < r.x + r.w; x++)
            {
                for (int z = r.z; z < r.z + r.h; z++)
                {
                    if (x == r.x)
                    {
                        // Stair 1 rises from (r.x, 0, r.z) to (r.x, 1, r.z + 1)
                        // So (r.x, 1, r.z) must be false (hole for stairs)
                        grid[x, r.y + 1, z].hasFloor = (z != r.z);
                    }
                    else if (x == r.x + 2)
                    {
                        // Stair 2 rises starting from Y=1, so floor is fully kept at Y=1
                        grid[x, r.y + 1, z].hasFloor = true;
                    }
                    else
                    {
                        grid[x, r.y + 1, z].hasFloor = false;
                    }
                }
            }
            // Y=2 high back bridge (Bridge at z == r.z + 1, but we must open a hole for Stair 2 at (r.x + 2, 2, r.z + 2))
            for (int x = r.x; x < r.x + r.w; x++)
            {
                for (int z = r.z; z < r.z + r.h; z++)
                {
                    if (x == r.x + 2)
                    {
                        grid[x, r.y + 2, z].hasFloor = (z == r.z + 1);
                    }
                    else
                    {
                        grid[x, r.y + 2, z].hasFloor = (z == r.z + 1 || z == r.z + 2);
                    }
                }
            }
        }
        else if (r.theme == "MineShaft")
        {
            // Y=2 mine high platform
            for (int x = r.x; x < r.x + r.w; x++)
            {
                for (int z = r.z; z < r.z + r.h; z++)
                {
                    grid[x, r.y + 1, z].hasFloor = (z >= r.z + 1);
                }
            }
        }
        else if (r.theme == "LivingQuarters")
        {
            // Y=1 living area has wood floor, but Stair 1 rises from (r.x, 0, r.z) to (r.x, 1, r.z + 1)
            // So (r.x, 1, r.z) must be false to prevent blocking the stairs.
            for (int x = r.x; x < r.x + r.w; x++)
            {
                for (int z = r.z; z < r.z + r.h; z++)
                {
                    grid[x, r.y + 1, z].hasFloor = !(x == r.x && z == r.z);
                }
            }
        }

        // Force doorways
        if (r.id > 1)
        {
            Vector3Int ent = r.entrancePoints[0];
            grid[ent.x, ent.y, ent.z].type = CellType.Doorway;
            grid[ent.x, ent.y, ent.z].roomId = r.id;
            grid[ent.x, ent.y, ent.z].hasFloor = true;
            grid[ent.x, ent.y, ent.z].hasCeiling = true;
        }
        if (r.id < 11)
        {
            Vector3Int ex = r.exitPoints[0];
            grid[ex.x, ex.y, ex.z].type = CellType.Doorway;
            grid[ex.x, ex.y, ex.z].roomId = r.id;
            grid[ex.x, ex.y, ex.z].hasFloor = true;
            grid[ex.x, ex.y, ex.z].hasCeiling = true;
        }
    }

    private void ConnectCellsOnLayer(Vector3Int start, Vector3Int end, int layer)
    {
        PriorityQueue<PathNode3D> openSet = new PriorityQueue<PathNode3D>();
        Dictionary<Vector3Int, Vector3Int> cameFrom = new Dictionary<Vector3Int, Vector3Int>();
        Dictionary<Vector3Int, float> gScore = new Dictionary<Vector3Int, float>();

        Vector3Int s = new Vector3Int(start.x, layer, start.z);
        Vector3Int e = new Vector3Int(end.x, layer, end.z);

        openSet.Enqueue(new PathNode3D(s, 0), 0);
        gScore[s] = 0;

        Vector3Int[] dirs = {
            new Vector3Int(0, 0, 1),
            new Vector3Int(0, 0, -1),
            new Vector3Int(1, 0, 0),
            new Vector3Int(-1, 0, 0)
        };

        bool pathFound = false;

        while (openSet.Count > 0)
        {
            PathNode3D current = openSet.Dequeue();

            if (current.Position == e)
            {
                pathFound = true;
                break;
            }

            foreach (var dir in dirs)
            {
                Vector3Int neighbor = current.Position + dir;
                neighbor.y = layer; // Stay on the same connection layer

                if (neighbor.x < 1 || neighbor.x >= width - 1 || neighbor.z < 1 || neighbor.z >= height - 1)
                    continue;

                float cost = 1.0f;
                CellType nType = grid[neighbor.x, layer, neighbor.z].type;

                // Prevent pathfinding from crossing through room interiors (which causes dead-ends due to solid walls).
                // Pathfinding must stay on Empty space or existing Corridors.
                if (nType == CellType.Room)
                {
                    if (neighbor != s && neighbor != e)
                    {
                        continue; // Block pathing through room interiors
                    }
                }

                if (nType == CellType.Corridor || nType == CellType.Doorway)
                {
                    cost = 0.1f; // Prefer merging with existing corridors
                }
                else if (nType == CellType.Empty)
                {
                    cost = 1.0f;
                }
                else
                {
                    continue; // Block other types like Stairs/Empty boundary exceptions
                }

                float tentativeGScore = gScore[current.Position] + cost;
                if (!gScore.ContainsKey(neighbor) || tentativeGScore < gScore[neighbor])
                {
                    cameFrom[neighbor] = current.Position;
                    gScore[neighbor] = tentativeGScore;
                    float h = Vector3.Distance(neighbor, e);
                    openSet.Enqueue(new PathNode3D(neighbor, tentativeGScore), tentativeGScore + h);
                }
            }
        }

        if (pathFound)
        {
            Vector3Int curr = e;
            while (curr != s)
            {
                if (grid[curr.x, curr.y, curr.z].type == CellType.Empty)
                {
                    grid[curr.x, curr.y, curr.z].type = CellType.Corridor;
                    grid[curr.x, curr.y, curr.z].hasFloor = true;
                    grid[curr.x, curr.y, curr.z].hasCeiling = true;
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
                    DungeonCell cell = grid[x, y, z];
                    if (cell.type == CellType.Empty) continue;

                    Vector3 center = new Vector3(x * cellSize, y * cellHeight, z * cellSize);

                    // 1. Floor Placement
                    if (cell.hasFloor)
                    {
                        GameObject floorPref = GetFloorPrefabForCell(x, y, z);
                        if (floorPref != null)
                        {
                            float floorYOffset = 0.001f * ((x + z) % 2); // Micro Y-offset for Z-Fighting
                            Vector3 floorPos = center + new Vector3(0, floorYOffset, 0);
                            
                            // Check if this is a raised platform border cell to apply modular foundation prefabs
                            if (y > 0 && cell.type == CellType.Room)
                            {
                                Room r = GetRoomById(cell.roomId);
                                if (r != null && r.layersCount > 1)
                                {
                                    SpawnPlatformFloor(r, x, y, z, floorPos);
                                }
                                else
                                {
                                    SpawnStandardFloor(floorPref, floorPos);
                                }
                            }
                            else
                            {
                                SpawnStandardFloor(floorPref, floorPos);
                            }
                        }
                    }

                    // 2. Ceiling Placement
                    if (cell.hasCeiling)
                    {
                        GameObject ceilingPref = GetCeilingPrefabForCell(x, y, z);
                        if (ceilingPref != null)
                        {
                            float ceilingYOffset = 0.001f * ((x + z) % 2);
                            Vector3 ceilingPos = center + new Vector3(0, cellHeight - 0.02f + ceilingYOffset, 0);
                            GameObject ceiling = Instantiate(ceilingPref, ceilingPos, Quaternion.Euler(180, 0, 0), transform);
                            ceiling.transform.localScale = prefabScale;
                            generatedObjects.Add(ceiling);
                        }
                    }

                    // 3. Wall and Doorway Boundaries
                    // North (Z = +2m)
                    SpawnCellBoundary(x, y, z, center, new Vector2Int(0, 1), new Vector3(0, 0, cellSize * 0.5f), Quaternion.Euler(0, 180, 0));
                    // South (Z = -2m)
                    SpawnCellBoundary(x, y, z, center, new Vector2Int(0, -1), new Vector3(0, 0, -cellSize * 0.5f), Quaternion.Euler(0, 0, 0));
                    // East (X = +2m)
                    SpawnCellBoundary(x, y, z, center, new Vector2Int(1, 0), new Vector3(cellSize * 0.5f, 0, 0), Quaternion.Euler(0, 270, 0));
                    // West (X = -2m)
                    SpawnCellBoundary(x, y, z, center, new Vector2Int(-1, 0), new Vector3(-cellSize * 0.5f, 0, 0), Quaternion.Euler(0, 90, 0));
                }
            }
        }
    }

    private void SpawnStandardFloor(GameObject floorPref, Vector3 pos)
    {
        GameObject floor = Instantiate(floorPref, pos, Quaternion.identity, transform);
        floor.transform.localScale = prefabScale;
        generatedObjects.Add(floor);
    }

    private void SpawnPlatformFloor(Room r, int x, int y, int z, Vector3 pos)
    {
#if UNITY_EDITOR
        // Determine edge adjacency on the platform layer Y
        bool N = (z + 1 < height) && grid[x, y, z + 1].roomId == r.id && grid[x, y, z + 1].hasFloor;
        bool S = (z - 1 >= 0) && grid[x, y, z - 1].roomId == r.id && grid[x, y, z - 1].hasFloor;
        bool E = (x + 1 < width) && grid[x + 1, y, z].roomId == r.id && grid[x + 1, y, z].hasFloor;
        bool W = (x - 1 >= 0) && grid[x - 1, y, z].roomId == r.id && grid[x - 1, y, z].hasFloor;

        int connections = (N ? 1 : 0) + (S ? 1 : 0) + (E ? 1 : 0) + (W ? 1 : 0);

        GameObject cornerPrefab = LoadPrefab("floor_foundation_corner");
        GameObject frontPrefab = LoadPrefab("floor_foundation_front");
        GameObject normalPrefab = GetFloorPrefabForCell(x, y, z);

        if (connections == 2)
        {
            // Corner platform blocks
            if (E && N) SpawnFoundationPiece(cornerPrefab, pos, 0f);
            else if (S && E) SpawnFoundationPiece(cornerPrefab, pos, 90f);
            else if (W && S) SpawnFoundationPiece(cornerPrefab, pos, 180f);
            else if (N && W) SpawnFoundationPiece(cornerPrefab, pos, 270f);
            else SpawnStandardFloor(normalPrefab, pos);
        }
        else if (connections == 3)
        {
            // Front edge platform blocks
            if (W && N && E) SpawnFoundationPiece(frontPrefab, pos, 0f); // flat on South
            else if (N && E && S) SpawnFoundationPiece(frontPrefab, pos, 90f); // flat on West
            else if (E && S && W) SpawnFoundationPiece(frontPrefab, pos, 180f); // flat on North
            else if (S && W && N) SpawnFoundationPiece(frontPrefab, pos, 270f); // flat on East
            else SpawnStandardFloor(normalPrefab, pos);
        }
        else
        {
            SpawnStandardFloor(normalPrefab, pos);
        }
#else
        SpawnStandardFloor(GetFloorPrefabForCell(x, y, z), pos);
#endif
    }

    private void SpawnFoundationPiece(GameObject prefab, Vector3 pos, float rotationY)
    {
        if (prefab == null) return;
        GameObject instance = Instantiate(prefab, pos, Quaternion.Euler(0, rotationY, 0), transform);
        instance.transform.localScale = Vector3.one;
        generatedObjects.Add(instance);
    }

    private bool IsStairsUpperExit(int stairsX, int stairsY, int stairsZ, int targetX, int targetY, int targetZ)
    {
        if (stairsX < 0 || stairsX >= width || stairsY < 0 || stairsY >= layers || stairsZ < 0 || stairsZ >= height) return false;
        if (grid[stairsX, stairsY, stairsZ].type != CellType.Stairs) return false;
        float rotY = grid[stairsX, stairsY, stairsZ].rotation;
        Quaternion stairsRot = Quaternion.Euler(0, rotY, 0);
        Vector3Int riseDir = Vector3Int.RoundToInt(stairsRot * Vector3.forward);
        return (targetX == stairsX + riseDir.x && targetY == stairsY + 1 && targetZ == stairsZ + riseDir.z);
    }

    private bool IsStairsLowerEntrance(int stairsX, int stairsY, int stairsZ, int targetX, int targetY, int targetZ)
    {
        if (stairsX < 0 || stairsX >= width || stairsY < 0 || stairsY >= layers || stairsZ < 0 || stairsZ >= height) return false;
        if (grid[stairsX, stairsY, stairsZ].type != CellType.Stairs) return false;
        float rotY = grid[stairsX, stairsY, stairsZ].rotation;
        Quaternion stairsRot = Quaternion.Euler(0, rotY, 0);
        Vector3Int riseDir = Vector3Int.RoundToInt(stairsRot * Vector3.forward);
        return (targetX == stairsX - riseDir.x && targetY == stairsY && targetZ == stairsZ - riseDir.z);
    }

    private void SpawnCellBoundary(int x, int y, int z, Vector3 center, Vector2Int dir, Vector3 offset, Quaternion rotation)
    {
        int nx = x + dir.x;
        int nz = z + dir.y;

        CellType neighborType = CellType.Empty;
        int neighborRoomId = 0;
        bool isOutBounds = false;
        if (nx < 0 || nx >= width || nz < 0 || nz >= height)
        {
            isOutBounds = true;
        }
        else
        {
            neighborType = grid[nx, y, nz].type;
            neighborRoomId = grid[nx, y, nz].roomId;
        }

        DungeonCell cell = grid[x, y, z];

        // 1. If currently in Stairs, we don't spawn any wall (stairs_walled prefab already includes wall structure)
        if (cell.type == CellType.Stairs)
        {
            return;
        }

        // Convert to logical type (treating Doorway as Room area)
        CellType logicalCellType = (cell.type == CellType.Doorway) ? CellType.Room : cell.type;
        CellType logicalNeighborType = (neighborType == CellType.Doorway) ? CellType.Room : neighborType;

        // 2. Determine if neighbor is empty space (void)
        bool isNeighborEmpty = (isOutBounds || neighborType == CellType.Empty);
        if (!isNeighborEmpty)
        {
            // Case A: Neighbor is Stairs
            if (neighborType == CellType.Stairs)
            {
                // If currently at the upper exit (at y = stairsY + 1) or lower entrance (at y = stairsY), do not spawn wall.
                if ((y > 0 && IsStairsUpperExit(nx, y - 1, nz, x, y, z)) || 
                    (IsStairsLowerEntrance(nx, y, nz, x, y, z)))
                {
                    return;
                }
            }
            // Case B: Both are Corridor
            else if (logicalCellType == CellType.Corridor && logicalNeighborType == CellType.Corridor)
            {
                return;
            }
            // Case C: Both are Room-like
            else if (logicalCellType == CellType.Room && logicalNeighborType == CellType.Room)
            {
                if (cell.roomId == neighborRoomId) return; // Same room (e.g. Doorway connecting to its own Room cell)
                if (cell.roomId > neighborRoomId) return;  // Different room: only smaller roomId spawns
            }
            // Case D: One is Room-like, one is Corridor
            else
            {
                if (logicalCellType == CellType.Corridor && logicalNeighborType == CellType.Room)
                {
                    // Corridor facing Room-like: let Room-like handle it
                    return;
                }
            }
        }

        // 3. Determine if we need to spawn a doorway or wall
        bool isDoorwayBoundary = false;
        if (cell.type == CellType.Doorway)
        {
            Room r = GetRoomById(cell.roomId);
            if (r != null)
            {
                // Check if this cell is on the room boundary where entrance/exit sits
                Vector3Int pos = new Vector3Int(x, y, z);
                if (r.entrancePoints.Contains(pos) && dir == new Vector2Int(-1, 0)) isDoorwayBoundary = true;
                if (r.exitPoints.Contains(pos) && dir == new Vector2Int(1, 0)) isDoorwayBoundary = true;
            }
        }

        if (isDoorwayBoundary)
        {
            // Defensive check: if the neighbour is empty space (void), do not spawn a doorway.
            // Spawn a wall instead to prevent doors opening directly to the void.
            if (isNeighborEmpty)
            {
                isDoorwayBoundary = false;
            }
        }

        if (isDoorwayBoundary)
        {
            GameObject doorwayPref = GetDoorwayPrefabForCell(x, y, z);
            if (doorwayPref != null)
            {
                GameObject doorway = Instantiate(doorwayPref, center + offset, rotation, transform);
                doorway.transform.localScale = prefabScale;
                if (doorway.GetComponent<PhysicalDoor>() == null && (doorway.name.Contains("doorway") || doorway.name.Contains("arc")))
                {
                    doorway.AddComponent<PhysicalDoor>();
                }
                generatedObjects.Add(doorway);
                
                // Spawn support pillars removed (no seams/gaps in this prefab set)
            }
        }
        else
        {
            GameObject wallPref = GetWallPrefabForCell(x, y, z);
            if (wallPref != null)
            {
                GameObject wall = Instantiate(wallPref, center + offset, rotation, transform);
                wall.transform.localScale = prefabScale;
                generatedObjects.Add(wall);

                // Add corner pillars removed (no seams/gaps in this prefab set)
            }
        }
    }

    private void SpawnDoorwayCornersPillars(int x, int y, int z, Vector3 center, Vector2Int dir)
    {
        // Removed: No support pillars needed for the current prefab set.
        return;
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

    // Decorate room depending on its themes using pre-generated prefabs
    private void DecorateRoom(Room r)
    {
#if UNITY_EDITOR
        switch (r.theme)
        {
            case "DividingHall":
                DecorateDividingHall(r);
                break;
            case "JailCells":
                DecorateJailCells(r);
                break;
            case "Cellar":
                DecorateCellar(r);
                break;
            case "GuardPost":
                DecorateGuardPost(r);
                break;
            case "MageLibrary":
                DecorateMageLibrary(r);
                break;
            case "GoldTreasury":
                DecorateGoldTreasury(r);
                break;
            case "SewerDock":
                DecorateSewerDock(r);
                break;
            case "CastleHall":
                DecorateCastleHall(r);
                break;
            case "MineShaft":
                DecorateMineShaft(r);
                break;
            case "LivingQuarters":
                DecorateLivingQuarters(r);
                break;
            case "TreasureVault":
                DecorateTreasureVault(r);
                break;
        }
#endif
    }

#if UNITY_EDITOR
    private void DecorateDividingHall(Room r)
    {
        // 1. Dirt corners floor tiles (with minor Y-offset to prevent Z-fighting)
        GameObject dirtCorner = LoadPrefab("floor_dirt_small_corner");
        if (dirtCorner != null)
        {
            float yOffset = 0.01f;
            InstantiateDecor(dirtCorner, new Vector3(r.x * cellSize, r.y * cellHeight + yOffset, r.z * cellSize), 0f);
            InstantiateDecor(dirtCorner, new Vector3((r.x + 2) * cellSize, r.y * cellHeight + yOffset, r.z * cellSize), 90f);
            InstantiateDecor(dirtCorner, new Vector3((r.x + 2) * cellSize, r.y * cellHeight + yOffset, (r.z + 2) * cellSize), 180f);
            InstantiateDecor(dirtCorner, new Vector3(r.x * cellSize, r.y * cellHeight + yOffset, (r.z + 2) * cellSize), 270f);
        }

        // 2. Central half-wall divider
        GameObject halfWall = LoadPrefab("wall_half_endcap");
        if (halfWall != null)
        {
            InstantiateDecor(halfWall, new Vector3((r.x + 1) * cellSize, r.y * cellHeight, (r.z + 1) * cellSize - 1.0f), 90f);
        }

        // 3. Railing barriers and storage props in dirt corners (with column end-caps)
        GameObject barrier = LoadPrefab("barrier");
        GameObject column = LoadPrefab("column");
        if (barrier != null)
        {
            InstantiateDecor(barrier, new Vector3(r.x * cellSize + 2.0f, r.y * cellHeight, r.z * cellSize), 90f);
        }
        if (column != null)
        {
            InstantiateDecor(column, new Vector3(r.x * cellSize + 2.0f, r.y * cellHeight, r.z * cellSize - 2.0f), 0f);
            InstantiateDecor(column, new Vector3(r.x * cellSize + 2.0f, r.y * cellHeight, r.z * cellSize + 2.0f), 0f);
        }

        GameObject barrelStack = LoadPrefab("barrel_small_stack");
        GameObject crateStack = LoadPrefab("crates_stacked");
        if (barrelStack != null) InstantiateDecor(barrelStack, new Vector3(r.x * cellSize + 1f, r.y * cellHeight, r.z * cellSize + 0.5f), 0f);
        if (crateStack != null) InstantiateDecor(crateStack, new Vector3((r.x + 2) * cellSize - 0.5f, r.y * cellHeight, (r.z + 2) * cellSize - 0.5f), 45f);

        // Central chest loot
        GameObject chest = LoadPrefab("chest");
        if (chest != null)
        {
            InstantiateDecor(chest, new Vector3((r.x + 1) * cellSize, r.y * cellHeight, (r.z + 1) * cellSize + 1.0f), 180f);
        }
    }

    private void DecorateJailCells(Room r)
    {
        // 1. Enclose cells at X = r.x using Iron Bars
        GameObject barStraight = LoadPrefab("bar_straight_A");
        GameObject barCorner = LoadPrefab("bar_innercorner");
        
        if (barStraight != null)
        {
            // Separates cell at left from right walkway
            InstantiateDecor(barStraight, new Vector3(r.x * cellSize + 2.0f, r.y * cellHeight, r.z * cellSize + 1.0f), 90f);
            InstantiateDecor(barStraight, new Vector3(r.x * cellSize + 2.0f, r.y * cellHeight, (r.z + 1) * cellSize - 1.0f), 90f);
        }

        // 2. Jail cell internal furniture (straw bed, stool, plate with bones)
        GameObject bedFloor = LoadPrefab("bed_floor");
        GameObject stool = LoadPrefab("stool");
        GameObject plate = LoadPrefab("plate_food_B"); // Bones plate
        
        if (bedFloor != null) InstantiateDecor(bedFloor, new Vector3(r.x * cellSize + 0.6f, r.y * cellHeight + 0.02f, r.z * cellSize + 0.6f), 0f);
        if (stool != null) InstantiateDecor(stool, new Vector3(r.x * cellSize + 1.4f, r.y * cellHeight, r.z * cellSize + 1.4f), 0f);
        if (plate != null) InstantiateDecor(plate, new Vector3(r.x * cellSize + 1.4f, r.y * cellHeight + 0.5f, r.z * cellSize + 1.4f), 0f);

        // 3. Ambient lighting
        GameObject candle = LoadPrefab("candle_lit");
        if (candle != null)
        {
            InstantiateDecor(candle, new Vector3(r.x * cellSize + 1.4f, r.y * cellHeight + 0.92f, r.z * cellSize + 1.4f), 0f);
        }
    }

    private void DecorateCellar(Room r)
    {
        // 1. Spawns T-split wall divider
        GameObject tWall = LoadPrefab("wall_Tsplit");
        if (tWall != null)
        {
            InstantiateDecor(tWall, new Vector3((r.x + 1) * cellSize, r.y * cellHeight, (r.z + 1) * cellSize), 180f);
        }

        // 2. Brewing barrels (kegs) on dirt side lanes (dirtFloor automatically handled by GetFloorPrefabForCell)
        GameObject kegDec = LoadPrefab("keg_decorated");
        GameObject keg = LoadPrefab("keg");
        if (kegDec != null) InstantiateDecor(kegDec, new Vector3(r.x * cellSize + 0.8f, r.y * cellHeight, r.z * cellSize + 0.8f), 0f);
        if (keg != null) InstantiateDecor(keg, new Vector3((r.x + 2) * cellSize - 0.8f, r.y * cellHeight, r.z * cellSize + 0.8f), 90f);

        // 3. Feast table
        GameObject tableLong = LoadPrefab("table_long");
        GameObject bench = LoadPrefab("bench");
        if (tableLong != null) InstantiateDecor(tableLong, new Vector3((r.x + 1) * cellSize, r.y * cellHeight, (r.z + 2) * cellSize), 90f);
        if (bench != null) InstantiateDecor(bench, new Vector3((r.x + 1) * cellSize - 0.8f, r.y * cellHeight, (r.z + 2) * cellSize), 90f);
    }

    private void DecorateGuardPost(Room r)
    {
        // 1. Scaffold Frame Support
        GameObject scaffold = LoadPrefab("scaffold_frame_large");
        if (scaffold != null)
        {
            // Place it centered under the Y=1 platform (which is at z == r.z + 1)
            InstantiateDecor(scaffold, new Vector3((r.x + 0.5f) * cellSize, r.y * cellHeight, (r.z + 1.0f) * cellSize), 0f);
        }

        // 2. Staircase Y=0 -> Y=1
        GameObject stairs = LoadPrefab("stairs_narrow");
        if (stairs != null)
        {
            // Position staircase at left cell, climbing South to North
            Vector3 stairsPos = new Vector3(r.x * cellSize, r.y * cellHeight, r.z * cellSize);
            InstantiateDecor(stairs, stairsPos, 0f); // 180 rot offset handled in prefab gen or here. Y-height automatically reaches 4m
            
            // Mark cell type as Stairs for physical wall and hole inspection
            grid[r.x, r.y, r.z].type = CellType.Stairs;
            grid[r.x, r.y, r.z].rotation = 0;
        }

        // 3. Raised Platform (Y=1) barrier railings and ending columns
        GameObject barrier = LoadPrefab("barrier");
        GameObject column = LoadPrefab("column");
        if (barrier != null)
        {
            // Railing at platform edge facing the Y=0 void
            InstantiateDecor(barrier, new Vector3((r.x + 1) * cellSize, (r.y + 1) * cellHeight, r.z * cellSize + 2.0f), 0f);
        }
        if (column != null)
        {
            // Left end-cap
            InstantiateDecor(column, new Vector3((r.x + 1) * cellSize - 2.0f, (r.y + 1) * cellHeight, r.z * cellSize + 2.0f), 0f);
            // Right end-cap
            InstantiateDecor(column, new Vector3((r.x + 1) * cellSize + 2.0f, (r.y + 1) * cellHeight, r.z * cellSize + 2.0f), 0f);
        }

        // 3. Props: broken table below, treasure chest above
        GameObject tableBroken = LoadPrefab("table_medium_broken");
        GameObject chest = LoadPrefab("chest_gold");
        if (tableBroken != null) InstantiateDecor(tableBroken, new Vector3((r.x + 1) * cellSize, r.y * cellHeight, r.z * cellSize), 30f);
        if (chest != null) InstantiateDecor(chest, new Vector3((r.x + 1) * cellSize, (r.y + 1) * cellHeight, (r.z + 1) * cellSize), 180f);

        // 4. Mounted wall torch
        GameObject torch = LoadPrefab("torch_mounted");
        if (torch != null)
        {
            InstantiateDecor(torch, new Vector3((r.x + 1) * cellSize, r.y * cellHeight + 2.5f, (r.z + 1) * cellSize + 2.0f), 0f);
        }
    }

    private void DecorateMageLibrary(Room r)
    {
        // 1. Spawns double bookcase and single bookcase against walls
        GameObject doubleBookcase = LoadPrefab("bookcase_double_decoratedA");
        GameObject singleBookcase = LoadPrefab("bookcase_single_decoratedB");
        if (doubleBookcase != null) InstantiateDecor(doubleBookcase, new Vector3(r.x * cellSize + 1.0f, r.y * cellHeight, (r.z + 1) * cellSize + 1.6f), 180f);
        if (singleBookcase != null) InstantiateDecor(singleBookcase, new Vector3(r.x * cellSize - 1.0f, r.y * cellHeight, (r.z + 1) * cellSize + 1.6f), 180f);

        // 2. Study desk and chairs in the center
        GameObject tableLong = LoadPrefab("table_long");
        GameObject chair = LoadPrefab("chair");
        if (tableLong != null) InstantiateDecor(tableLong, new Vector3(r.x * cellSize + 1.0f, r.y * cellHeight, r.z * cellSize + 1.0f), 90f);
        if (chair != null)
        {
            InstantiateDecor(chair, new Vector3(r.x * cellSize + 1.0f, r.y * cellHeight, r.z * cellSize + 0.2f), 0f);
            InstantiateDecor(chair, new Vector3(r.x * cellSize + 1.0f, r.y * cellHeight, r.z * cellSize + 1.8f), 180f);
        }

        // 3. Scattered books and candle lit lightings
        GameObject book1 = LoadPrefab("book_brown");
        GameObject book2 = LoadPrefab("book_grey");
        GameObject candle = LoadPrefab("candle_lit");
        if (book1 != null) InstantiateDecor(book1, new Vector3(r.x * cellSize + 0.8f, r.y * cellHeight + 1.0f, r.z * cellSize + 0.8f), 15f);
        if (book2 != null) InstantiateDecor(book2, new Vector3(r.x * cellSize + 1.2f, r.y * cellHeight + 1.0f, r.z * cellSize + 1.1f), -45f);
        if (candle != null) InstantiateDecor(candle, new Vector3(r.x * cellSize + 1.0f, r.y * cellHeight + 1.0f, r.z * cellSize + 1.0f), 0f);

        // Hanging banner
        GameObject banner = LoadPrefab("banner_green");
        if (banner != null) InstantiateDecor(banner, new Vector3(r.x * cellSize + 2.0f, r.y * cellHeight + 2.0f, (r.z + 1) * cellSize), 90f);
    }

    private void DecorateGoldTreasury(Room r)
    {
        // 1. Spawns dark wood floor runner in the center path (woodFloor automatically handled by GetFloorPrefabForCell)

        // 2. Large gold treasure chest and stacks of coins
        GameObject chestLarge = LoadPrefab("chest_large_gold");
        GameObject coinStackL = LoadPrefab("coin_stack_large");
        GameObject coinStackM = LoadPrefab("coin_stack_medium");
        
        if (chestLarge != null) InstantiateDecor(chestLarge, new Vector3((r.x + 1) * cellSize, r.y * cellHeight, (r.z + 1) * cellSize + 1.0f), 180f);
        if (coinStackL != null)
        {
            InstantiateDecor(coinStackL, new Vector3((r.x + 1) * cellSize - 1.2f, r.y * cellHeight, (r.z + 1) * cellSize + 0.8f), 0f);
            InstantiateDecor(coinStackL, new Vector3((r.x + 1) * cellSize + 1.2f, r.y * cellHeight, (r.z + 1) * cellSize + 0.8f), 0f);
        }
        if (coinStackM != null)
        {
            InstantiateDecor(coinStackM, new Vector3((r.x + 1) * cellSize - 0.8f, r.y * cellHeight, (r.z + 1) * cellSize + 1.2f), 0f);
        }

        // 3. Hanging banners
        GameObject banner = LoadPrefab("banner_yellow");
        if (banner != null)
        {
            InstantiateDecor(banner, new Vector3((r.x + 1) * cellSize - 2.0f, r.y * cellHeight + 2.0f, (r.z + 1) * cellSize), 90f);
            InstantiateDecor(banner, new Vector3((r.x + 1) * cellSize + 2.0f, r.y * cellHeight + 2.0f, (r.z + 1) * cellSize), -90f);
        }
    }

    private void DecorateSewerDock(Room r)
    {
        // 1. Water grates on Y=0 lower level waterway (grate automatically handled by GetFloorPrefabForCell)

        // 2. Floating barrels in waterway
        GameObject keg = LoadPrefab("keg");
        if (keg != null)
        {
            InstantiateDecor(keg, new Vector3(r.x * cellSize + 0.5f, r.y * cellHeight, r.z * cellSize + 0.8f), 15f);
        }

        // 3. Wooden stairs going down from land bridge to waterway
        GameObject stairsWood = LoadPrefab("stairs_wood");
        if (stairsWood != null)
        {
            // Put the stair in the channel x = r.x, climbing East (90 degrees) onto the platform x = r.x + 1
            // Centered on Z to match bridge alignment
            Vector3 stairsPos = new Vector3(r.x * cellSize + 0.5f, r.y * cellHeight, r.z * cellSize + 2.0f);
            InstantiateDecor(stairsWood, stairsPos, 90f);
            
            // Mark cell type as Stairs for physical wall and hole inspection
            grid[r.x, r.y, r.z + 1].type = CellType.Stairs;
            grid[r.x, r.y, r.z + 1].rotation = 90;
        }

        // 4. Bridge railings (with start/middle/end columns)
        GameObject barrier = LoadPrefab("barrier");
        GameObject column = LoadPrefab("column");
        if (barrier != null)
        {
            InstantiateDecor(barrier, new Vector3((r.x + 1) * cellSize - 2.0f, (r.y + 1) * cellHeight, r.z * cellSize), 90f);
            InstantiateDecor(barrier, new Vector3((r.x + 1) * cellSize - 2.0f, (r.y + 1) * cellHeight, (r.z + 1) * cellSize), 90f);
        }
        if (column != null)
        {
            // Start end-cap
            InstantiateDecor(column, new Vector3((r.x + 1) * cellSize - 2.0f, (r.y + 1) * cellHeight, r.z * cellSize - 2.0f), 0f);
            // Middle connection joint column
            InstantiateDecor(column, new Vector3((r.x + 1) * cellSize - 2.0f, (r.y + 1) * cellHeight, r.z * cellSize + 2.0f), 0f);
            // End end-cap
            InstantiateDecor(column, new Vector3((r.x + 1) * cellSize - 2.0f, (r.y + 1) * cellHeight, r.z * cellSize + 6.0f), 0f);
        }
    }

    private void DecorateCastleHall(Room r)
    {
        // 1. Spawns two stone staircases for multi-tier navigation
        GameObject stairs = LoadPrefab("stairs_narrow");
        if (stairs != null)
        {
            // Stair 1 (Y=0 to Y=1): left side
            InstantiateDecor(stairs, new Vector3(r.x * cellSize, r.y * cellHeight, r.z * cellSize), 0f);
            grid[r.x, r.y, r.z].type = CellType.Stairs;
            grid[r.x, r.y, r.z].rotation = 0;
            
            // Stair 2 (Y=1 to Y=2): right side
            InstantiateDecor(stairs, new Vector3((r.x + 2) * cellSize, (r.y + 1) * cellHeight, (r.z + 2) * cellSize), 180f);
            grid[r.x + 2, r.y + 1, r.z + 2].type = CellType.Stairs;
            grid[r.x + 2, r.y + 1, r.z + 2].rotation = 180;
        }

        // 2. Balcony railings (Y=1 and Y=2 exposed edges with columns)
        GameObject barrier = LoadPrefab("barrier");
        GameObject column = LoadPrefab("column");
        if (barrier != null)
        {
            // Y=1 balcony edge
            InstantiateDecor(barrier, new Vector3(r.x * cellSize + 2.0f, (r.y + 1) * cellHeight, (r.z + 1) * cellSize), 90f);
            
            // Y=2 high balcony edge
            InstantiateDecor(barrier, new Vector3((r.x + 1) * cellSize, (r.y + 2) * cellHeight, (r.z + 2) * cellSize - 2.0f), 0f);
        }
        if (column != null)
        {
            // Y=1 balcony edge end caps
            InstantiateDecor(column, new Vector3(r.x * cellSize + 2.0f, (r.y + 1) * cellHeight, (r.z + 1) * cellSize - 2.0f), 0f);
            InstantiateDecor(column, new Vector3(r.x * cellSize + 2.0f, (r.y + 1) * cellHeight, (r.z + 1) * cellSize + 2.0f), 0f);
            
            // Y=2 high balcony edge end caps
            InstantiateDecor(column, new Vector3((r.x + 1) * cellSize - 2.0f, (r.y + 2) * cellHeight, (r.z + 2) * cellSize - 2.0f), 0f);
            InstantiateDecor(column, new Vector3((r.x + 1) * cellSize + 2.0f, (r.y + 2) * cellHeight, (r.z + 2) * cellSize - 2.0f), 0f);
        }

        // 3. Furniture layout per layer
        GameObject tableSmall = LoadPrefab("table_small");
        GameObject tableMedium = LoadPrefab("table_medium");
        GameObject tableCloth = LoadPrefab("table_small_tablecloth");
        GameObject chair = LoadPrefab("chair");

        if (tableSmall != null) InstantiateDecor(tableSmall, new Vector3((r.x + 1) * cellSize, r.y * cellHeight, r.z * cellSize + 1.0f), 0f);
        if (tableMedium != null) InstantiateDecor(tableMedium, new Vector3(r.x * cellSize, (r.y + 1) * cellHeight, (r.z + 1) * cellSize), 90f);
        if (tableCloth != null) InstantiateDecor(tableCloth, new Vector3((r.x + 1) * cellSize, (r.y + 2) * cellHeight, (r.z + 2) * cellSize + 1.0f), 0f);
        if (chair != null)
        {
            InstantiateDecor(chair, new Vector3((r.x + 1) * cellSize - 0.8f, (r.y + 2) * cellHeight, (r.z + 2) * cellSize + 1.0f), 90f);
            InstantiateDecor(chair, new Vector3((r.x + 1) * cellSize + 0.8f, (r.y + 2) * cellHeight, (r.z + 2) * cellSize + 1.0f), -90f);
        }
    }

    private void DecorateMineShaft(Room r)
    {
        // 1. Spawns wood frames (scaffolding) supporting upper platform
        GameObject scaffold = LoadPrefab("scaffold_frame_large");
        if (scaffold != null)
        {
            // Centered under the Y=2 platform
            InstantiateDecor(scaffold, new Vector3((r.x + 1) * cellSize, r.y * cellHeight, (r.z + 1) * cellSize), 0f);
        }

        // 2. Miner's rest bed on Y=1 lower level
        GameObject bed = LoadPrefab("bed_A_single");
        GameObject tools = LoadPrefab("bucket_pickaxes");
        if (bed != null) InstantiateDecor(bed, new Vector3(r.x * cellSize + 0.8f, r.y * cellHeight, r.z * cellSize + 0.8f), 90f);
        if (tools != null) InstantiateDecor(tools, new Vector3(r.x * cellSize + 1.8f, r.y * cellHeight, r.z * cellSize + 0.8f), 0f);

        // 3. Wooden stairs
        GameObject stairsWood = LoadPrefab("stairs_wood");
        if (stairsWood != null)
        {
            InstantiateDecor(stairsWood, new Vector3((r.x + 1) * cellSize, r.y * cellHeight, r.z * cellSize), 0f);
            
            // Mark cell type as Stairs for physical wall and hole inspection
            grid[r.x + 1, r.y, r.z].type = CellType.Stairs;
            grid[r.x + 1, r.y, r.z].rotation = 0;
        }

        // 4. Gold veins and rock ores on upper mine platform Y=2
        GameObject goldVein = LoadPrefab("rocks_gold");
        GameObject normalRock = LoadPrefab("rocks");
        GameObject pickaxe = LoadPrefab("pickaxe");
        if (goldVein != null) InstantiateDecor(goldVein, new Vector3((r.x + 2) * cellSize - 0.8f, (r.y + 1) * cellHeight, (r.z + 2) * cellSize - 0.8f), 45f);
        if (normalRock != null) InstantiateDecor(normalRock, new Vector3(r.x * cellSize + 0.8f, (r.y + 1) * cellHeight, (r.z + 2) * cellSize - 0.8f), 0f);
        if (pickaxe != null) InstantiateDecor(pickaxe, new Vector3((r.x + 2) * cellSize - 1.2f, (r.y + 1) * cellHeight, (r.z + 2) * cellSize - 1.2f), -15f);
    }

    private void DecorateLivingQuarters(Room r)
    {
        // 1. Scaffold Frame Support
        GameObject scaffold = LoadPrefab("scaffold_frame_large");
        if (scaffold != null)
        {
            // Place large scaffold at the center of the 2x2 room
            InstantiateDecor(scaffold, new Vector3((r.x + 0.5f) * cellSize, r.y * cellHeight, (r.z + 0.5f) * cellSize), 0f);
        }

        // 2. Spawns wood stairs Y=0 -> Y=1
        GameObject stairsWood = LoadPrefab("stairs_wood");
        if (stairsWood != null)
        {
            InstantiateDecor(stairsWood, new Vector3(r.x * cellSize, r.y * cellHeight, r.z * cellSize), 0f);
            
            // Mark cell type as Stairs for physical wall and hole inspection
            grid[r.x, r.y, r.z].type = CellType.Stairs;
            grid[r.x, r.y, r.z].rotation = 0;
        }

        // 3. Layer 0: Dining Mess Hall (table with tablecloth + benches)
        GameObject tableCloth = LoadPrefab("table_long_tablecloth");
        GameObject bench = LoadPrefab("bench");
        if (tableCloth != null)
        {
            // Shift table away from the exit door (which is at r.x + 1, r.z + 1)
            // Shift it South-West to (r.x + 0.8, r.z + 0.8)
            Vector3 tablePos = new Vector3((r.x + 0.8f) * cellSize, r.y * cellHeight, (r.z + 0.8f) * cellSize);
            InstantiateDecor(tableCloth, tablePos, 90f);
            if (bench != null)
            {
                InstantiateDecor(bench, tablePos + new Vector3(-0.8f, 0, 0), 90f);
                InstantiateDecor(bench, tablePos + new Vector3(0.8f, 0, 0), 90f);
            }
        }

        // 3. Layer 1: Living/Sleeping quarters
        GameObject bed = LoadPrefab("bed_A_single");
        GameObject bookcase = LoadPrefab("bookcase_double");
        GameObject tableSmall = LoadPrefab("table_small");
        GameObject crates = LoadPrefab("box_stacked");

        if (bed != null)
        {
            InstantiateDecor(bed, new Vector3((r.x + 1) * cellSize - 0.6f, (r.y + 1) * cellHeight, (r.z + 1) * cellSize + 1.2f), 180f);
            InstantiateDecor(bed, new Vector3((r.x + 1) * cellSize + 0.6f, (r.y + 1) * cellHeight, (r.z + 1) * cellSize + 1.2f), 180f);
        }
        if (bookcase != null) InstantiateDecor(bookcase, new Vector3(r.x * cellSize + 1.0f, (r.y + 1) * cellHeight, (r.z + 1) * cellSize + 1.6f), 180f);
        if (tableSmall != null) InstantiateDecor(tableSmall, new Vector3((r.x + 1) * cellSize, (r.y + 1) * cellHeight, r.z * cellSize + 0.6f), 0f);
        if (crates != null) InstantiateDecor(crates, new Vector3((r.x + 1) * cellSize - 1.4f, (r.y + 1) * cellHeight, r.z * cellSize + 0.6f), 45f);
    }

    private void DecorateTreasureVault(Room r)
    {
        // 1. Spawns final big gold chest
        GameObject chestLarge = LoadPrefab("chest_large_gold");
        GameObject chestNormal = LoadPrefab("chest_gold");
        if (chestLarge != null) InstantiateDecor(chestLarge, new Vector3(r.x * cellSize + 2.0f, r.y * cellHeight, r.z * cellSize + 2.0f), 180f);
        if (chestNormal != null) InstantiateDecor(chestNormal, new Vector3(r.x * cellSize + 0.8f, r.y * cellHeight, r.z * cellSize + 2.4f), 150f);

        // 2. Large piles of gold coins and decorative wall sword shield
        GameObject coinStackL = LoadPrefab("coin_stack_large");
        GameObject coinStackM = LoadPrefab("coin_stack_medium");
        GameObject swordShield = LoadPrefab("sword_shield_gold");
        
        if (coinStackL != null)
        {
            InstantiateDecor(coinStackL, new Vector3(r.x * cellSize + 1.4f, r.y * cellHeight, r.z * cellSize + 1.4f), 0f);
            InstantiateDecor(coinStackL, new Vector3(r.x * cellSize + 2.4f, r.y * cellHeight, r.z * cellSize + 1.2f), 0f);
        }
        if (coinStackM != null)
        {
            InstantiateDecor(coinStackM, new Vector3(r.x * cellSize + 1.8f, r.y * cellHeight, r.z * cellSize + 2.2f), 0f);
        }
        if (swordShield != null)
        {
            InstantiateDecor(swordShield, new Vector3(r.x * cellSize + 2.0f, r.y * cellHeight + 2.5f, (r.z + 1) * cellSize + 2.0f), 180f);
        }
    }

    private void InstantiateDecor(GameObject prefab, Vector3 pos, float rotationY)
    {
        if (prefab == null) return;

        // Apply filters to prevent obstructing doors or stairs
        if (IsPositionObstructingDoor(pos, prefab.name))
        {
            return;
        }

        if (IsPositionObstructingStairs(pos, prefab.name))
        {
            return;
        }

        GameObject instance = Instantiate(prefab, pos, Quaternion.Euler(0, rotationY, 0), transform);
        instance.transform.localScale = Vector3.one;
        generatedObjects.Add(instance);
    }

    private bool IsPositionObstructingDoor(Vector3 pos, string prefabName)
    {
        string lowerName = prefabName.ToLower();
        if (lowerName.Contains("torch") || lowerName.Contains("banner") || lowerName.Contains("shield") || lowerName.Contains("weed") || lowerName.Contains("candle") || lowerName.Contains("scaffold"))
        {
            return false;
        }

        foreach (var r in rooms)
        {
            if (r.entrancePoints.Count > 0)
            {
                Vector3Int ent = r.entrancePoints[0];
                Vector3 entPos = new Vector3(ent.x * cellSize, ent.y * cellHeight, ent.z * cellSize);
                if (Mathf.Abs(pos.y - entPos.y) < 1.0f)
                {
                    float dist = Vector2.Distance(new Vector2(pos.x, pos.z), new Vector2(entPos.x, entPos.z));
                    if (dist < 1.8f)
                    {
                        return true;
                    }
                }
            }

            if (r.exitPoints.Count > 0)
            {
                Vector3Int ex = r.exitPoints[0];
                Vector3 exPos = new Vector3(ex.x * cellSize, ex.y * cellHeight, ex.z * cellSize);
                if (Mathf.Abs(pos.y - exPos.y) < 1.0f)
                {
                    float dist = Vector2.Distance(new Vector2(pos.x, pos.z), new Vector2(exPos.x, exPos.z));
                    if (dist < 1.8f)
                    {
                        return true;
                    }
                }
            }
        }
        return false;
    }

    private bool IsPositionObstructingStairs(Vector3 pos, string prefabName)
    {
        string lowerName = prefabName.ToLower();
        bool isFence = lowerName.Contains("barrier") || lowerName.Contains("bar_");
        if (!isFence)
        {
            return false;
        }

        for (int x = 0; x < width; x++)
        {
            for (int y = 0; y < layers; y++)
            {
                for (int z = 0; z < height; z++)
                {
                    if (grid[x, y, z].type == CellType.Stairs)
                    {
                        float rotY = grid[x, y, z].rotation;
                        Quaternion stairsRot = Quaternion.Euler(0, rotY, 0);
                        Vector3Int riseDir = Vector3Int.RoundToInt(stairsRot * Vector3.forward);

                        Vector3Int upperExit = new Vector3Int(x + riseDir.x, y + 1, z + riseDir.z);
                        Vector3 upperPos = new Vector3(upperExit.x * cellSize, upperExit.y * cellHeight, upperExit.z * cellSize);
                        if (Mathf.Abs(pos.y - upperPos.y) < 1.0f)
                        {
                            float dist = Vector2.Distance(new Vector2(pos.x, pos.z), new Vector2(upperPos.x, upperPos.z));
                            if (dist < 2.2f)
                            {
                                return true;
                            }
                        }

                        Vector3Int lowerEntrance = new Vector3Int(x - riseDir.x, y, z - riseDir.z);
                        Vector3 lowerPos = new Vector3(lowerEntrance.x * cellSize, lowerEntrance.y * cellHeight, lowerEntrance.z * cellSize);
                        if (Mathf.Abs(pos.y - lowerPos.y) < 1.0f)
                        {
                            float dist = Vector2.Distance(new Vector2(pos.x, pos.z), new Vector2(lowerPos.x, lowerPos.z));
                            if (dist < 2.2f)
                            {
                                return true;
                            }
                        }
                    }
                }
            }
        }
        return false;
    }
#endif

    private Room GetRoomById(int id)
    {
        return rooms.Find(r => r.id == id);
    }

    private GameObject GetFloorPrefabForCell(int x, int y, int z)
    {
        if (x < 0 || x >= width || z < 0 || z >= height) return floorPrefab;
        DungeonCell cell = grid[x, y, z];
        if (cell.type == CellType.Room)
        {
            Room r = GetRoomById(cell.roomId);
            if (r != null)
            {
                if (r.theme == "GoldTreasury" && z == r.z + r.h / 2)
                {
#if UNITY_EDITOR
                    return LoadPrefab("floor_wood_large_dark");
#endif
                }
                if (r.theme == "LivingQuarters" && y == r.y + 1)
                {
#if UNITY_EDITOR
                    return LoadPrefab("floor_wood_large_dark");
#endif
                }
                if (r.theme == "SewerDock" && y == r.y && x == r.x)
                {
#if UNITY_EDITOR
                    return LoadPrefab("floor_tile_big_grate");
#endif
                }
                if (r.theme == "JailCells" && z == r.z)
                {
#if UNITY_EDITOR
                    return LoadPrefab("floor_dirt_large");
#endif
                }
                if (r.theme == "Cellar" && (x == r.x || x == r.x + 2))
                {
#if UNITY_EDITOR
                    return LoadPrefab("floor_dirt_large");
#endif
                }
                if (r.theme == "MineShaft" && y == r.y + 1)
                {
#if UNITY_EDITOR
                    return LoadPrefab("floor_dirt_large_rocky");
#endif
                }
            }
        }
        return (dungeonTheme == DungeonTheme.GothicRuins || (dungeonTheme == DungeonTheme.Mixed && y % 2 == 0)) ? gothicFloorPrefab : floorPrefab;
    }

    private GameObject GetCeilingPrefabForCell(int x, int y, int z)
    {
        return (dungeonTheme == DungeonTheme.GothicRuins || (dungeonTheme == DungeonTheme.Mixed && y % 2 == 0)) ? gothicCeilingPrefab : ceilingPrefab;
    }

    private GameObject GetWallPrefabForCell(int x, int y, int z)
    {
        if (x < 0 || x >= width || z < 0 || z >= height) return wallPrefab;
        DungeonCell cell = grid[x, y, z];
        if (cell.type == CellType.Room)
        {
            Room r = GetRoomById(cell.roomId);
            if (r != null)
            {
                if (r.theme == "LivingQuarters" && y == r.y + 1)
                {
#if UNITY_EDITOR
                    return LoadPrefab("wall_scaffold");
#endif
                }
                if (r.theme == "MageLibrary")
                {
#if UNITY_EDITOR
                    // Variety decoration shelves wall
                    return LoadPrefab("wall_inset_shelves");
#endif
                }
            }
        }
        return (dungeonTheme == DungeonTheme.GothicRuins || (dungeonTheme == DungeonTheme.Mixed && y % 2 == 0)) ? gothicWallPrefab : wallPrefab;
    }

    private GameObject GetDoorwayPrefabForCell(int x, int y, int z)
    {
        return (dungeonTheme == DungeonTheme.GothicRuins || (dungeonTheme == DungeonTheme.Mixed && y % 2 == 0)) ? gothicDoorwayPrefab : doorwayPrefab;
    }

    public string GetGridLayoutAsString()
    {
        System.Text.StringBuilder sb = new System.Text.StringBuilder();
        for (int y = 0; y < layers; y++)
        {
            sb.AppendLine($"=== LAYER {y} LAYOUT ===");
            for (int z = height - 1; z >= 0; z--)
            {
                for (int x = 0; x < width; x++)
                {
                    if (grid == null) return "Grid is null";
                    CellType type = grid[x, y, z].type;
                    if (type == CellType.Room) sb.Append("R");
                    else if (type == CellType.Corridor) sb.Append("C");
                    else if (type == CellType.Doorway) sb.Append("D");
                    else sb.Append(".");
                }
                sb.AppendLine();
            }
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
