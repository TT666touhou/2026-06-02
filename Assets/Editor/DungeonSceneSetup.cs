using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine.SceneManagement;
using System.IO;
using System.Collections.Generic;

public static class DungeonSceneSetup
{
    [MenuItem("Tools/Set Grid Snap (1, 1, 1)")]
    public static void SetGridSnap()
    {
        EditorSnapSettings.gridSnapEnabled = true;
        EditorSnapSettings.gridSize = new Vector3(1f, 1f, 1f);
        EditorSnapSettings.move = new Vector3(1f, 1f, 1f);
        
        // Automatically make the SceneView grid visible
        if (SceneView.lastActiveSceneView != null)
        {
            SceneView.lastActiveSceneView.showGrid = true;
            SceneView.lastActiveSceneView.Repaint();
        }
        
        Debug.Log("Grid Snapping has been enabled, Move snapping increments have been set to (1, 1, 1), and grid visibility has been turned ON!");
    }

    [MenuItem("Tools/Create Empty Manual Scene")]
    public static void CreateEmptyManualScene()
    {
        if (EditorApplication.isPlaying)
        {
            Debug.LogError("Cannot create scene during Play Mode!");
            return;
        }

        string scenePath = "Assets/Scenes/DungeonManualSetup.unity";
        if (!Directory.Exists("Assets/Scenes"))
        {
            Directory.CreateDirectory("Assets/Scenes");
        }

        Scene scene = EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);
        EditorSceneManager.SaveScene(scene, scenePath);
        
        // Auto-enable grid settings and visibility for the new scene
        SetGridSnap();
        
        Debug.Log($"Empty scene created and saved to {scenePath}. Open it to start manual setup!");
        if (!Application.isBatchMode)
        {
            EditorUtility.DisplayDialog("Success", $"Successfully created and opened empty scene at: {scenePath}\n\nGrid snap and visibility have been automatically enabled!", "OK");
        }
    }

    [MenuItem("Tools/Auto Setup Test Scene")]
    public static void SetupScene()
    {
        if (EditorApplication.isPlaying)
        {
            Debug.LogError("Cannot run Auto Setup during Play Mode! Please exit Play Mode and run this tool again.");
            if (!Application.isBatchMode)
            {
                EditorUtility.DisplayDialog("Play Mode Active", "Please exit Play Mode before running the Auto Setup tool!", "OK");
            }
            return;
        }

        // 1. Create or Open Scene
        string scenePath = "Assets/Scenes/DungeonTest.unity";
        Scene scene;
        
        // Ensure Scenes directory exists
        if (!Directory.Exists("Assets/Scenes"))
        {
            Directory.CreateDirectory("Assets/Scenes");
        }

        if (File.Exists(scenePath))
        {
            scene = EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Single);
        }
        else
        {
            scene = EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);
            EditorSceneManager.SaveScene(scene, scenePath);
        }

        // 2. Clear existing generator if any
        GameObject existingGen = GameObject.Find("DungeonGenerator");
        if (existingGen != null)
        {
            Object.DestroyImmediate(existingGen);
        }

        // 3. Create DungeonGenerator GameObject
        GameObject genGo = new GameObject("DungeonGenerator");
        GridDungeonGenerator generator = genGo.AddComponent<GridDungeonGenerator>();

        // 4. Find and assign structural prefabs from KayKit Dungeon
        string kaykitPath = "Assets/Prefabs/KayKit Dungeon/";
        generator.floorPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(kaykitPath + "floor_tile_large.prefab");
        generator.wallPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(kaykitPath + "wall.prefab");
        generator.ceilingPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(kaykitPath + "ceiling_tile.prefab");
        generator.doorwayPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(kaykitPath + "wall_doorway.prefab");

        generator.tunnelStraight = null;
        generator.tunnelCorner = null;
        generator.tunnelTJunction = null;
        generator.tunnelXJunction = null;

        // Assign KayKit prefabs to both Gothic and Bunker slots to ensure consistent look
        generator.gothicFloorPrefab = generator.floorPrefab;
        generator.gothicWallPrefab = generator.wallPrefab;
        generator.gothicCeilingPrefab = generator.ceilingPrefab;
        generator.gothicDoorwayPrefab = generator.doorwayPrefab;
        
        generator.stairsPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(kaykitPath + "stairs_walled.prefab");
        generator.bunkerStairsPrefab = generator.stairsPrefab;
        generator.pillarPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(kaykitPath + "wall_pillar.prefab");
        generator.bunkerPillarPrefab = generator.pillarPrefab;

        if (generator.floorPrefab == null || generator.wallPrefab == null || generator.ceilingPrefab == null || 
            generator.doorwayPrefab == null || generator.gothicFloorPrefab == null || generator.gothicWallPrefab == null || 
            generator.gothicCeilingPrefab == null || generator.gothicDoorwayPrefab == null || 
            generator.stairsPrefab == null || generator.bunkerStairsPrefab == null ||
            generator.pillarPrefab == null || generator.bunkerPillarPrefab == null)
        {
            Debug.LogError("Failed to load some KayKit Dungeon prefabs! Please make sure you have run the prefab generator tool under Tools -> Generate KayKit Prefabs.");
            return;
        }

        generator.width = 12;
        generator.height = 12;
        generator.layers = 2; // 2 layers for staircase verticality
        generator.cellSize = 4.0f; // KayKit modular grid size is 4.0m
        generator.cellHeight = 4.0f; // KayKit walls are 4.0m high
        generator.prefabScale = Vector3.one; // No scaling needed for native 4m grid!
        generator.roomsPerLayer = 3;
        generator.minRoomSize = 2;
        generator.maxRoomSize = 3;
        generator.useRandomSeed = true;
        generator.dungeonTheme = GridDungeonGenerator.DungeonTheme.Mixed; // Mixed theme uses same prefabs now
        generator.corridorStyle = GridDungeonGenerator.CorridorStyle.SquareCorridor;

        // 5. Generate the dungeon
        generator.GenerateDungeon();
        
        string gridStr = generator.GetGridLayoutAsString();
        File.WriteAllText("Assets/dungeon_grid.txt", gridStr);
        Debug.Log("Dungeon grid layout saved to Assets/dungeon_grid.txt:\n" + gridStr);

        // 6. Setup Lighting and Camera for nice look
        GameObject dirLight = GameObject.Find("Directional Light");
        if (dirLight != null)
        {
            Light lightComponent = dirLight.GetComponent<Light>();
            if (lightComponent != null)
            {
                lightComponent.color = new Color(0.12f, 0.15f, 0.25f); // Deep blue night sky ambient light
                lightComponent.intensity = 0.3f;
                dirLight.transform.rotation = Quaternion.Euler(55, -45, 0);
            }
        }

        // Add some local point lights to highlight the dungeon retro feel on all layers!
        Random.InitState(generator.seed);
        int lightCountPerLayer = 6;
        for (int y = 0; y < generator.layers; y++)
        {
            for (int i = 0; i < lightCountPerLayer; i++)
            {
                GameObject pointLightGo = new GameObject($"PointLight_Warm_L{y}_{i}");
                pointLightGo.transform.parent = genGo.transform;
                
                float xPos = Random.Range(1, generator.width - 1) * generator.cellSize + Random.Range(-2.0f, 2.0f);
                float zPos = Random.Range(1, generator.height - 1) * generator.cellSize + Random.Range(-2.0f, 2.0f);
                float yPos = y * generator.cellHeight + 1.8f;
                pointLightGo.transform.position = new Vector3(xPos, yPos, zPos);
                
                Light pointLight = pointLightGo.AddComponent<Light>();
                pointLight.type = LightType.Point;
                pointLight.color = (y % 2 == 0) ? new Color(0.85f, 0.65f, 0.45f) : new Color(1.0f, 0.55f, 0.15f); // warmer flame tone vs tungsten retro glow
                pointLight.range = 10.0f;
                pointLight.intensity = 3.0f;
            }
        }

        float centerX = (generator.width * generator.cellSize) * 0.5f;
        float centerZ = (generator.height * generator.cellSize) * 0.5f;

        // 7. Find first Room floor to spawn the Player
        Vector3 playerSpawnPos = new Vector3(centerX, 1.0f, centerZ); // fallback
        bool spawnedAtRoomCenter = false;
        
        var roomsField = typeof(GridDungeonGenerator).GetField("rooms", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        if (roomsField != null)
        {
            var roomsList = (System.Collections.IList)roomsField.GetValue(generator);
            if (roomsList != null && roomsList.Count > 0)
            {
                var startingRoom = roomsList[0];
                var xField = startingRoom.GetType().GetField("x");
                var yField = startingRoom.GetType().GetField("y");
                var zField = startingRoom.GetType().GetField("z");
                
                if (xField != null && yField != null && zField != null)
                {
                    int rx = (int)xField.GetValue(startingRoom);
                    int ry = (int)yField.GetValue(startingRoom);
                    int rz = (int)zField.GetValue(startingRoom);
                    
                    float cellSize = generator.cellSize;
                    float cellHeight = generator.cellHeight;
                    
                    // Spawn in DividingHall's north-middle cell (rx + 1, ry, rz + 2) which is completely clear of decorations
                    playerSpawnPos = new Vector3(
                        (rx + 1) * cellSize,
                        ry * cellHeight + 1.0f,
                        (rz + 2) * cellSize
                    );
                    spawnedAtRoomCenter = true;
                    Debug.Log($"[DungeonSceneSetup] Spawned Player at safe DividingHall cell: {playerSpawnPos}");
                }
            }
        }

        if (!spawnedAtRoomCenter && generator.transform.childCount > 0)
        {
            for (int i = 0; i < generator.transform.childCount; i++)
            {
                Transform child = generator.transform.GetChild(i);
                if (child.name.Contains("floor") && !child.name.Contains("foundation"))
                {
                    playerSpawnPos = child.position + new Vector3(0, 1.0f, 0); // spawn slightly higher
                    break;
                }
            }
        }

        // Setup Player GameObject
        GameObject playerGo = GameObject.Find("Player");
        if (playerGo != null)
        {
            // Detach camera first to prevent it from being destroyed with the player!
            Camera playerCam = playerGo.GetComponentInChildren<Camera>();
            if (playerCam != null)
            {
                playerCam.transform.parent = null;
            }
            Object.DestroyImmediate(playerGo);
        }
        playerGo = new GameObject("Player");
        playerGo.transform.position = playerSpawnPos;
        playerGo.transform.rotation = Quaternion.identity;

        // Add character controller and FPC script
        CharacterController charController = playerGo.AddComponent<CharacterController>();
        charController.center = new Vector3(0, 0.9f, 0);
        charController.height = 1.8f;
        charController.radius = 0.25f;
        charController.slopeLimit = 45f;
        charController.stepOffset = 0.3f;

        FirstPersonController fpc = playerGo.AddComponent<FirstPersonController>();
        fpc.walkSpeed = 4.0f;
        fpc.mouseSensitivity = 2.0f;
        fpc.pushForce = 5.0f; // push force for doors

        // Parent and position Main Camera robustly
        GameObject mainCamera = GameObject.FindWithTag("MainCamera");
        if (mainCamera == null)
        {
            mainCamera = GameObject.Find("Main Camera");
        }
        if (mainCamera == null)
        {
            Camera anyCam = Object.FindAnyObjectByType<Camera>();
            if (anyCam != null)
            {
                mainCamera = anyCam.gameObject;
            }
        }
        if (mainCamera == null)
        {
            mainCamera = new GameObject("Main Camera");
            mainCamera.tag = "MainCamera";
            mainCamera.AddComponent<Camera>();
            mainCamera.AddComponent<AudioListener>();
        }

        mainCamera.transform.parent = playerGo.transform;
        mainCamera.transform.localPosition = new Vector3(0, 1.4f, 0); // eye-level height
        mainCamera.transform.localRotation = Quaternion.identity;
        
        Camera cam = mainCamera.GetComponent<Camera>();
        if (cam != null)
        {
            cam.backgroundColor = new Color(0.02f, 0.02f, 0.04f);
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.farClipPlane = 100f;
        }

        // 8. Auto-capture Screenshots for AI visual validation
        CaptureEditorScreenshots(generator, centerX, centerZ, generator.cellSize);

        // 9. Save Scene
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene, scenePath);
        Debug.Log("Dungeon Scene Auto Setup and Screenshot Capture Completed Successfully!");

        // 10. Run Inspection Report
        DungeonSceneInspector.InspectDungeon();

        if (!Application.isBatchMode)
        {
            EditorUtility.DisplayDialog("Success", "Dungeon Test Scene has been generated, saved, and visual screenshots/reports have been exported! Open 'Assets/Scenes/DungeonTest.unity' to view it.", "OK");
        }
    }

    private static void CaptureEditorScreenshots(GridDungeonGenerator generator, float centerX, float centerZ, float cellSize)
    {
        // Find or create temporary camera
        GameObject tempCamGo = new GameObject("TempCaptureCamera");
        Camera cam = tempCamGo.AddComponent<Camera>();
        cam.backgroundColor = new Color(0.02f, 0.02f, 0.04f);
        cam.clearFlags = CameraClearFlags.SolidColor;
        cam.farClipPlane = 200f;
        cam.nearClipPlane = 0.1f;

        // Create warm spotlight on camera to help view interior details
        GameObject camLightGo = new GameObject("TempCamLight");
        camLightGo.transform.parent = tempCamGo.transform;
        camLightGo.transform.localPosition = Vector3.zero;
        Light cl = camLightGo.AddComponent<Light>();
        cl.type = LightType.Spot;
        cl.range = 30f;
        cl.intensity = 3.0f;
        cl.spotAngle = 60f;

        // 1. Perspective View
        tempCamGo.transform.position = new Vector3(centerX, 30.0f, centerZ - 30.0f);
        tempCamGo.transform.rotation = Quaternion.Euler(45.0f, 0f, 0f);
        cam.orthographic = false;
        cam.fieldOfView = 60f;
        SaveCamRender(cam, "dungeon_perspective.png");

        // 2. Top-down View
        tempCamGo.transform.position = new Vector3(centerX, 60.0f, centerZ);
        tempCamGo.transform.rotation = Quaternion.Euler(90.0f, 0f, 0f);
        cam.orthographic = true;
        cam.orthographicSize = 35.0f;
        SaveCamRender(cam, "dungeon_topdown.png");

        // 3. First-person Eye Level inside room/tunnel
        Vector3 fpPos = new Vector3(centerX, 1.6f, centerZ); // fallback
        if (generator.transform.childCount > 0)
        {
            for (int i = 0; i < generator.transform.childCount; i++)
            {
                Transform child = generator.transform.GetChild(i);
                if (child.name.Contains("floor") || child.name.Contains("tunnel"))
                {
                    fpPos = child.position + new Vector3(0, 1.6f, 0);
                    break;
                }
            }
        }
        tempCamGo.transform.position = fpPos;
        tempCamGo.transform.rotation = Quaternion.Euler(10.0f, 45.0f, 0f);
        cam.orthographic = false;
        cam.fieldOfView = 75f;
        SaveCamRender(cam, "dungeon_inside.png");

        Object.DestroyImmediate(tempCamGo);
    }

    private static void SaveCamRender(Camera cam, string filename)
    {
        int width = 1024;
        int height = 576;
        RenderTexture rt = new RenderTexture(width, height, 24);
        cam.targetTexture = rt;
        
        Texture2D screenShot = new Texture2D(width, height, TextureFormat.RGB24, false);
        cam.Render();
        
        RenderTexture.active = rt;
        screenShot.ReadPixels(new Rect(0, 0, width, height), 0, 0);
        screenShot.Apply();
        
        cam.targetTexture = null;
        RenderTexture.active = null;
        Object.DestroyImmediate(rt);
        
        byte[] bytes = screenShot.EncodeToPNG();
        Object.DestroyImmediate(screenShot);
        
        string brainDir = "C:/Users/88698/.gemini/antigravity-ide/brain/de671271-394d-4fcf-8e8a-43ab3a9b592c";
        if (Directory.Exists(brainDir))
        {
            string savePath = Path.Combine(brainDir, filename);
            File.WriteAllBytes(savePath, bytes);
            Debug.Log($"[DungeonSceneSetup] Successfully captured & saved screenshot to {savePath}");
        }
    }
}

public static class DungeonSceneInspector
{
    private static void CreateErrorMarker(GameObject root, Vector3 position, string name, Color color, Vector3 size)
    {
        GameObject marker = GameObject.CreatePrimitive(PrimitiveType.Cube);
        marker.name = name;
        marker.transform.parent = root.transform;
        marker.transform.position = position;
        marker.transform.localScale = size;
        
        // Remove collider
        var collider = marker.GetComponent<Collider>();
        if (collider != null) Object.DestroyImmediate(collider);
        
        // Set color with transparency
        var renderer = marker.GetComponent<Renderer>();
        if (renderer != null)
        {
            Shader shader = Shader.Find("Sprites/Default");
            if (shader == null) shader = Shader.Find("Standard");
            
            Material mat = new Material(shader);
            mat.color = new Color(color.r, color.g, color.b, 0.4f);
            
            if (shader.name == "Standard")
            {
                mat.SetFloat("_Mode", 3f);
                mat.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
                mat.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
                mat.SetInt("_ZWrite", 0);
                mat.DisableKeyword("_ALPHATEST_ON");
                mat.EnableKeyword("_ALPHABLEND_ON");
                mat.DisableKeyword("_ALPHAPREMULTIPLY_ON");
                mat.renderQueue = 3000;
            }
            
            renderer.material = mat;
        }
    }

    private static Transform GetDungeonEntityRoot(Transform t, Transform dungeonRoot)
    {
        Transform current = t;
        while (current != null && current.parent != dungeonRoot && current.parent != null)
        {
            current = current.parent;
        }
        return current;
    }

    [MenuItem("Tools/Inspect Generated Dungeon")]
    public static void InspectDungeon()
    {
        GameObject genGo = GameObject.Find("DungeonGenerator");
        if (genGo == null)
        {
            Debug.LogError("[DungeonInspector] Could not find DungeonGenerator GameObject in the scene.");
            if (!Application.isBatchMode)
            {
                EditorUtility.DisplayDialog("Error", "Could not find DungeonGenerator GameObject in the scene. Please generate the dungeon first.", "OK");
            }
            return;
        }

        GridDungeonGenerator generator = genGo.GetComponent<GridDungeonGenerator>();
        if (generator == null)
        {
            Debug.LogError("[DungeonInspector] DungeonGenerator GameObject does not have GridDungeonGenerator component.");
            return;
        }

        // Use Reflection to read private fields of GridDungeonGenerator
        var gridField = typeof(GridDungeonGenerator).GetField("grid", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        if (gridField == null)
        {
            Debug.LogError("[DungeonInspector] Failed to retrieve 'grid' field via Reflection.");
            return;
        }

        var grid = (GridDungeonGenerator.DungeonCell[,,])gridField.GetValue(generator);
        if (grid == null)
        {
            Debug.LogError("[DungeonInspector] Grid is null. Please generate the dungeon layout first.");
            return;
        }

        int width = generator.width;
        int layers = generator.layers;
        int height = generator.height;
        float cellSize = generator.cellSize;
        float cellHeight = generator.cellHeight;

        // Get all transforms and renderers
        Transform[] transforms = genGo.GetComponentsInChildren<Transform>(true);
        Renderer[] renderers = genGo.GetComponentsInChildren<Renderer>(true);

        // Setup visual errors root
        GameObject errorsRoot = GameObject.Find("__DungeonErrors__");
        if (errorsRoot != null)
        {
            Object.DestroyImmediate(errorsRoot);
        }
        errorsRoot = new GameObject("__DungeonErrors__");

        System.Text.StringBuilder sb = new System.Text.StringBuilder();
        sb.AppendLine("=== ADVANCED DUNGEON SCENE INSPECTION REPORT ===");
        sb.AppendLine($"Report generated: {System.DateTime.Now}");
        sb.AppendLine($"Total GameObjects scanned: {transforms.Length}");
        sb.AppendLine($"Total Renderers scanned: {renderers.Length}");
        sb.AppendLine();

        int exactDuplicates = 0;
        int overlappingWalls = 0;
        int overlappingFloors = 0;
        int voidHoleGaps = 0;
        int floorHoleGaps = 0;
        int ceilingHoleGaps = 0;
        int walkwayHoleGaps = 0;

        List<string> duplicateLogs = new List<string>();
        List<string> floorOverlapLogs = new List<string>();
        List<string> wallOverlapLogs = new List<string>();
        List<string> voidHoleLogs = new List<string>();
        List<string> floorHoleLogs = new List<string>();
        List<string> ceilingHoleLogs = new List<string>();
        List<string> walkwayHoleLogs = new List<string>();

        // 1. Check for Exact Duplicates (position & rotation & name)
        for (int i = 0; i < transforms.Length; i++)
        {
            for (int j = i + 1; j < transforms.Length; j++)
            {
                Transform t1 = transforms[i];
                Transform t2 = transforms[j];

                if (t1 == null || t2 == null || t1 == genGo.transform || t2 == genGo.transform) continue;

                float posDist = Vector3.Distance(t1.position, t2.position);
                float rotAngle = Quaternion.Angle(t1.rotation, t2.rotation);

                if (posDist < 0.001f && rotAngle < 1f && t1.gameObject.name == t2.gameObject.name)
                {
                    exactDuplicates++;
                    duplicateLogs.Add($"Exact Duplicate GameObject: '{t1.name}' at {t1.position.ToString("F3")} matches '{t2.name}' at {t2.position.ToString("F3")}");
                    CreateErrorMarker(errorsRoot, t1.position, $"Duplicate_{t1.name}", Color.yellow, new Vector3(1.2f, 1.2f, 1.2f));
                }
            }
        }

        // 2. Check for Overlapping Floors/Ceilings & Parallel Walls (Z-Fighting)
        for (int i = 0; i < renderers.Length; i++)
        {
            for (int j = i + 1; j < renderers.Length; j++)
            {
                Renderer r1 = renderers[i];
                Renderer r2 = renderers[j];

                if (r1 == null || r2 == null) continue;

                // Skip if they belong to the same top-level prefab generated object
                if (GetDungeonEntityRoot(r1.transform, genGo.transform) == GetDungeonEntityRoot(r2.transform, genGo.transform))
                {
                    continue;
                }

                Vector3 p1 = r1.transform.position;
                Vector3 p2 = r2.transform.position;

                bool isFloor1 = r1.gameObject.name.ToLower().Contains("floor") || r1.gameObject.name.ToLower().Contains("ceiling");
                bool isFloor2 = r2.gameObject.name.ToLower().Contains("floor") || r2.gameObject.name.ToLower().Contains("ceiling");

                if (isFloor1 && isFloor2)
                {
                    // Check if centers are close in XZ plane (< 1.0m) and Y difference is < 5mm
                    float xzDist = Vector2.Distance(new Vector2(p1.x, p1.z), new Vector2(p2.x, p2.z));
                    float yDiff = Mathf.Abs(p1.y - p2.y);
                    if (xzDist < 1.0f && yDiff < 0.005f)
                    {
                        overlappingFloors++;
                        floorOverlapLogs.Add($"Floor/Ceiling Overlap (Z-Fighting): '{r1.gameObject.name}' at {p1.ToString("F3")} overlaps '{r2.gameObject.name}' at {p2.ToString("F3")} (Y-diff: {yDiff * 1000f:F1}mm)");
                        CreateErrorMarker(errorsRoot, (p1 + p2) * 0.5f, $"FloorOverlap_{r1.name}_{r2.name}", Color.blue, new Vector3(3f, 0.15f, 3f));
                    }
                    continue;
                }

                bool isWall1 = r1.gameObject.name.ToLower().Contains("wall") || r1.gameObject.name.ToLower().Contains("doorway") || r1.gameObject.name.ToLower().Contains("arc");
                bool isWall2 = r2.gameObject.name.ToLower().Contains("wall") || r2.gameObject.name.ToLower().Contains("doorway") || r2.gameObject.name.ToLower().Contains("arc");

                if (isWall1 && isWall2)
                {
                    // Check if parallel
                    float rotDotF = Mathf.Abs(Vector3.Dot(r1.transform.forward, r2.transform.forward));
                    float rotDotR = Mathf.Abs(Vector3.Dot(r1.transform.right, r2.transform.right));
                    bool isParallel = rotDotF > 0.99f || rotDotR > 0.99f;

                    if (isParallel)
                    {
                        Vector3 dir = (p2 - p1);
                        float distToPlane = Mathf.Abs(Vector3.Dot(dir, r1.transform.forward));
                        float distToPlane2 = Mathf.Abs(Vector3.Dot(dir, r1.transform.right));
                        float minPlaneDist = Mathf.Min(distToPlane, distToPlane2);

                        // If on same plane (diff < 5mm) and center distance is < 1.0m
                        if (minPlaneDist < 0.005f && Vector3.Distance(p1, p2) < 1.0f)
                        {
                            overlappingWalls++;
                            string parent1 = r1.transform.parent != null ? r1.transform.parent.name : "null";
                            string parent2 = r2.transform.parent != null ? r2.transform.parent.name : "null";
                            wallOverlapLogs.Add($"Parallel Wall Overlap (Z-Fighting): '{r1.gameObject.name}' (parent: {parent1}) at {p1.ToString("F3")} overlaps '{r2.gameObject.name}' (parent: {parent2}) at {p2.ToString("F3")} (Plane-diff: {minPlaneDist * 1000f:F1}mm)");
                            CreateErrorMarker(errorsRoot, (p1 + p2) * 0.5f, $"WallOverlap_{r1.name}_{r2.name}", Color.red, new Vector3(0.3f, 3.5f, 3.5f));
                        }
                    }
                }
            }
        }

        // 3. Grid-based Gap and Hole Inspection
        Vector2Int[] dirs = {
            new Vector2Int(0, 1),   // North
            new Vector2Int(0, -1),  // South
            new Vector2Int(1, 0),   // East
            new Vector2Int(-1, 0)   // West
        };

        for (int y = 0; y < layers; y++)
        {
            for (int x = 0; x < width; x++)
            {
                for (int z = 0; z < height; z++)
                {
                    var cell = grid[x, y, z];
                    if (cell.type == GridDungeonGenerator.CellType.Empty) continue;

                    Vector3 cellCenter = new Vector3(x * cellSize, y * cellHeight, z * cellSize);

                    // Skip circular sci-fi tunnels for boundary check (self-contained meshes)
                    bool isCellCircularTunnel = false;
                    if (cell.type == GridDungeonGenerator.CellType.Corridor)
                    {
                        if (generator.dungeonTheme == GridDungeonGenerator.DungeonTheme.Mixed && y % 2 == 0)
                        {
                            isCellCircularTunnel = false;
                        }
                        else if (generator.corridorStyle == GridDungeonGenerator.CorridorStyle.CircularTunnel)
                        {
                            isCellCircularTunnel = true;
                        }
                        else if (generator.corridorStyle == GridDungeonGenerator.CorridorStyle.Mixed)
                        {
                            int cellHash = (x * 73856093) ^ (z * 19349663) ^ (y * 83492791) ^ generator.seed;
                            isCellCircularTunnel = (System.Math.Abs(cellHash) % 2 == 0);
                        }
                    }

                    if (isCellCircularTunnel)
                    {
                        // Circular tunnels are self-enclosing, skip boundary wall verification
                        continue;
                    }

                    // A. Floor Verification
                    if (cell.hasFloor)
                    {
                        bool hasFloorMesh = false;
                        Vector3 floorCenter = cellCenter;
                        foreach (var r in renderers)
                        {
                            if (r == null) continue;
                            if (r.gameObject.name.ToLower().Contains("floor"))
                            {
                                float distSq = r.bounds.SqrDistance(floorCenter);
                                if (distSq < 0.05f)
                                {
                                    hasFloorMesh = true;
                                    break;
                                }
                            }
                        }
                        if (!hasFloorMesh)
                        {
                            floorHoleGaps++;
                            floorHoleLogs.Add($"Floor Hole: Cell ({x}, {y}, {z}) of type {cell.type} has hasFloor=true but no floor geometry is instantiated nearby.");
                            CreateErrorMarker(errorsRoot, floorCenter, $"FloorHole_Cell_{x}_{y}_{z}", Color.blue, new Vector3(4f, 0.2f, 4f));
                        }
                    }

                    // B. Ceiling Verification
                    if (cell.hasCeiling)
                    {
                        bool hasCeilingMesh = false;
                        Vector3 ceilingCenter = cellCenter + new Vector3(0, cellHeight, 0);
                        foreach (var r in renderers)
                        {
                            if (r == null) continue;
                            if (r.gameObject.name.ToLower().Contains("ceiling") || r.gameObject.name.ToLower().Contains("floor"))
                            {
                                float distSq = r.bounds.SqrDistance(ceilingCenter);
                                if (distSq < 0.05f)
                                {
                                    hasCeilingMesh = true;
                                    break;
                                }
                            }
                        }
                        if (!hasCeilingMesh)
                        {
                            ceilingHoleGaps++;
                            ceilingHoleLogs.Add($"Ceiling Hole: Cell ({x}, {y}, {z}) of type {cell.type} has hasCeiling=true but no ceiling geometry is instantiated nearby.");
                            CreateErrorMarker(errorsRoot, ceilingCenter, $"CeilingHole_Cell_{x}_{y}_{z}", Color.cyan, new Vector3(4f, 0.2f, 4f));
                        }
                    }

                    // C. Wall boundary verification facing Empty
                    foreach (var dir in dirs)
                    {
                        int nx = x + dir.x;
                        int nz = z + dir.y;

                        bool neighborIsEmpty = false;
                        if (nx < 0 || nx >= width || nz < 0 || nz >= height)
                        {
                            neighborIsEmpty = true;
                        }
                        else
                        {
                            neighborIsEmpty = (grid[nx, y, nz].type == GridDungeonGenerator.CellType.Empty);
                        }

                        if (neighborIsEmpty)
                        {
                            // There must be a wall separating the cell from empty space
                            Vector3 boundaryCenter = cellCenter + new Vector3(dir.x * cellSize * 0.5f, cellHeight * 0.5f, dir.y * cellSize * 0.5f);
                            bool hasWallMesh = false;
                            foreach (var r in renderers)
                            {
                                if (r == null) continue;
                                string rName = r.gameObject.name.ToLower();
                                if (rName.Contains("wall") || rName.Contains("doorway") || rName.Contains("arc") || rName.Contains("tunnel"))
                                {
                                    float distSq = r.bounds.SqrDistance(boundaryCenter);
                                    if (distSq < 0.05f)
                                    {
                                        hasWallMesh = true;
                                        break;
                                    }
                                }
                            }
                            if (!hasWallMesh)
                            {
                                voidHoleGaps++;
                                voidHoleLogs.Add($"Void Wall Hole: Cell ({x}, {y}, {z}) facing Empty direction {dir} has no enclosing boundary wall. Boundary position: {boundaryCenter.ToString("F2")}");
                                Vector3 size = (dir.x != 0) ? new Vector3(0.2f, 4f, 4f) : new Vector3(4f, 4f, 0.2f);
                                CreateErrorMarker(errorsRoot, boundaryCenter, $"VoidHole_Cell_{x}_{y}_{z}_Dir_{dir}", Color.magenta, size);
                            }
                        }
                    }

                    // D. Fall Hazard / Walkway Gaps (checking adjacent floorless cells like stairs upper cell)
                    if (cell.hasFloor)
                    {
                        foreach (var dir in dirs)
                        {
                            int nx = x + dir.x;
                            int nz = z + dir.y;

                            if (nx >= 0 && nx < width && nz >= 0 && nz < height)
                            {
                                var neighbor = grid[nx, y, nz];
                                // If neighbor has no floor (like stairs upper cell)
                                if (neighbor.type != GridDungeonGenerator.CellType.Empty && !neighbor.hasFloor)
                                {
                                    // Check if target cell (x, y, z) is the upper exit of the stairs at (nx, y-1, nz)
                                    bool isExit = IsStairsUpperExit(grid, nx, y - 1, nz, x, y, z, width, layers, height);
                                    if (!isExit)
                                    {
                                        // There MUST be a wall separating the room/corridor from the floorless upper stairs cell!
                                        Vector3 boundaryCenter = cellCenter + new Vector3(dir.x * cellSize * 0.5f, cellHeight * 0.5f, dir.y * cellSize * 0.5f);
                                        bool hasWallMesh = false;
                                        foreach (var r in renderers)
                                        {
                                            if (r == null) continue;
                                            string rName = r.gameObject.name.ToLower();
                                            if (rName.Contains("wall") || rName.Contains("doorway") || rName.Contains("arc") || rName.Contains("barrier"))
                                            {
                                                // Check XZ plane distance (ignore Y axis differences for short barriers/decorations)
                                                Vector3 closestPoint = r.bounds.ClosestPoint(boundaryCenter);
                                                float xzDistSq = Mathf.Pow(closestPoint.x - boundaryCenter.x, 2) + Mathf.Pow(closestPoint.z - boundaryCenter.z, 2);
                                                if (xzDistSq < 0.05f)
                                                {
                                                    hasWallMesh = true;
                                                    break;
                                                }
                                            }
                                        }
                                        if (!hasWallMesh)
                                        {
                                            walkwayHoleGaps++;
                                            walkwayHoleLogs.Add($"Walkway Fall Hazard: Playable cell ({x}, {y}, {z}) has floor, but neighbor cell ({nx}, {y}, {nz}) is floorless (above stairs) and there is no wall separating them. Boundary: {boundaryCenter.ToString("F2")}");
                                            Vector3 hazardSize = (dir.x != 0) ? new Vector3(0.2f, 4f, 4f) : new Vector3(4f, 4f, 0.2f);
                                            CreateErrorMarker(errorsRoot, boundaryCenter, $"WalkwayHazard_Cell_{x}_{y}_{z}_Dir_{dir}", new Color(1.0f, 0.5f, 0f), hazardSize);
                                        }
                                    }
                                }
                            }
                        }
                    }
                }
            }
        }

        // Clean up errors parent if there are no errors
        if (exactDuplicates == 0 && overlappingFloors == 0 && overlappingWalls == 0 && voidHoleGaps == 0 && floorHoleGaps == 0 && ceilingHoleGaps == 0 && walkwayHoleGaps == 0)
        {
            if (errorsRoot != null)
            {
                Object.DestroyImmediate(errorsRoot);
            }
        }

        // Write report
        sb.AppendLine("--- 1. EXACT DUPLICATE GAMEOBJECTS (Z-Fighting & Perf hit) ---");
        sb.AppendLine($"Count: {exactDuplicates}");
        foreach (var log in duplicateLogs) sb.AppendLine("  " + log);
        sb.AppendLine();

        sb.AppendLine("--- 2. OVERLAPPING FLOORS & CEILINGS ---");
        sb.AppendLine($"Count: {overlappingFloors}");
        foreach (var log in floorOverlapLogs) sb.AppendLine("  " + log);
        sb.AppendLine();

        sb.AppendLine("--- 3. OVERLAPPING PARALLEL WALLS ---");
        sb.AppendLine($"Count: {overlappingWalls}");
        foreach (var log in wallOverlapLogs) sb.AppendLine("  " + log);
        sb.AppendLine();

        sb.AppendLine("--- 4. VOID WALL HOLES (Holes exposing skybox/void) ---");
        sb.AppendLine($"Count: {voidHoleGaps}");
        foreach (var log in voidHoleLogs) sb.AppendLine("  " + log);
        sb.AppendLine();

        sb.AppendLine("--- 5. MISSING FLOORS ---");
        sb.AppendLine($"Count: {floorHoleGaps}");
        foreach (var log in floorHoleLogs) sb.AppendLine("  " + log);
        sb.AppendLine();

        sb.AppendLine("--- 6. MISSING CEILINGS ---");
        sb.AppendLine($"Count: {ceilingHoleGaps}");
        foreach (var log in ceilingHoleLogs) sb.AppendLine("  " + log);
        sb.AppendLine();

        sb.AppendLine("--- 7. WALKWAY FALL HAZARDS (Holes looking into vertical shafts) ---");
        sb.AppendLine($"Count: {walkwayHoleGaps}");
        foreach (var log in walkwayHoleLogs) sb.AppendLine("  " + log);
        sb.AppendLine();

        string report = sb.ToString();
        string reportPath = "Assets/dungeon_inspection_report.txt";
        File.WriteAllText(reportPath, report);
        Debug.Log($"[DungeonInspector] Detailed inspection complete. Saved report to: {reportPath}");

        if (!Application.isBatchMode)
        {
            EditorUtility.DisplayDialog("Inspection Complete", 
                $"Dungeon Scene Inspected!\n\n" +
                $"- Exact Duplicates: {exactDuplicates}\n" +
                $"- Floor Overlaps: {overlappingFloors}\n" +
                $"- Wall Overlaps: {overlappingWalls}\n" +
                $"- Void Wall Holes: {voidHoleGaps}\n" +
                $"- Floor Holes: {floorHoleGaps}\n" +
                $"- Walkway Fall Hazards: {walkwayHoleGaps}\n\n" +
                $"Details saved to: {reportPath}", "OK");
        }
    }

    private static bool IsStairsUpperExit(GridDungeonGenerator.DungeonCell[,,] grid, int stairsX, int stairsY, int stairsZ, int targetX, int targetY, int targetZ, int width, int layers, int height)
    {
        if (grid == null) return false;
        if (stairsX < 0 || stairsX >= width || stairsY < 0 || stairsY >= layers || stairsZ < 0 || stairsZ >= height) return false;
        if (grid[stairsX, stairsY, stairsZ].type != GridDungeonGenerator.CellType.Stairs) return false;
        float rotY = grid[stairsX, stairsY, stairsZ].rotation;
        Quaternion stairsRot = Quaternion.Euler(0, rotY, 0);
        Vector3Int riseDir = Vector3Int.RoundToInt(stairsRot * Vector3.forward);
        return (targetX == stairsX + riseDir.x && targetY == stairsY + 1 && targetZ == stairsZ + riseDir.z);
    }
}
