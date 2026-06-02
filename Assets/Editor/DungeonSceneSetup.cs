using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine.SceneManagement;
using System.IO;

public static class DungeonSceneSetup
{
    [MenuItem("Tools/Auto Setup Test Scene")]
    public static void SetupScene()
    {
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

        // 4. Find and assign structural prefabs
        string basePath = "Assets/Prefabs/PSX Bunkers v1.8.8/";
        generator.floorPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(basePath + "floor_1.prefab");
        generator.wallPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(basePath + "wall_1_plain.prefab");
        generator.ceilingPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(basePath + "floor_1.prefab");
        generator.doorwayPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(basePath + "doorway_2_plain.prefab");

        generator.tunnelStraight = AssetDatabase.LoadAssetAtPath<GameObject>(basePath + "tunnel_straight.prefab");
        generator.tunnelCorner = AssetDatabase.LoadAssetAtPath<GameObject>(basePath + "tunnel_ancle.prefab");
        generator.tunnelTJunction = AssetDatabase.LoadAssetAtPath<GameObject>(basePath + "tunnel_junction_three_way.prefab");
        generator.tunnelXJunction = AssetDatabase.LoadAssetAtPath<GameObject>(basePath + "tunnel_junction_four_way.prefab");

        if (generator.floorPrefab == null || generator.wallPrefab == null || generator.ceilingPrefab == null || 
            generator.doorwayPrefab == null || generator.tunnelStraight == null || generator.tunnelCorner == null || 
            generator.tunnelTJunction == null || generator.tunnelXJunction == null)
        {
            Debug.LogError("Failed to load some hybrid dungeon prefabs! Please check paths under Assets/Prefabs/PSX Bunkers v1.8.8/");
            return;
        }

        generator.width = 10;
        generator.height = 10;
        generator.cellSize = 6.0f;
        generator.roomDensity = 0.20f;
        generator.seed = 1337;
        generator.corridorStyle = GridDungeonGenerator.CorridorStyle.Mixed;

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

        // Add some local point lights to highlight the bunker retro feel!
        Random.InitState(generator.seed);
        int lightCount = 8;
        for (int i = 0; i < lightCount; i++)
        {
            GameObject pointLightGo = new GameObject($"PointLight_Warm_{i}");
            pointLightGo.transform.parent = genGo.transform;
            
            float xPos = Random.Range(1, generator.width - 1) * generator.cellSize + Random.Range(-2.0f, 2.0f);
            float zPos = Random.Range(1, generator.height - 1) * generator.cellSize + Random.Range(-2.0f, 2.0f);
            pointLightGo.transform.position = new Vector3(xPos, 1.8f, zPos);
            
            Light pointLight = pointLightGo.AddComponent<Light>();
            pointLight.type = LightType.Point;
            pointLight.color = new Color(1.0f, 0.55f, 0.15f); // Warm retro tungsten glow
            pointLight.range = 10.0f;
            pointLight.intensity = 2.5f;
        }

        float centerX = (generator.width * generator.cellSize) * 0.5f;
        float centerZ = (generator.height * generator.cellSize) * 0.5f;

        // 7. Find first Room floor to spawn the Player
        Vector3 playerSpawnPos = new Vector3(centerX, 0.5f, centerZ); // fallback
        if (generator.transform.childCount > 0)
        {
            for (int i = 0; i < generator.transform.childCount; i++)
            {
                Transform child = generator.transform.GetChild(i);
                if (child.name.Contains("floor"))
                {
                    playerSpawnPos = child.position + new Vector3(0, 0.5f, 0); // spawn slightly above floor
                    break;
                }
            }
        }

        // Setup Player GameObject
        GameObject playerGo = GameObject.Find("Player");
        if (playerGo != null)
        {
            Object.DestroyImmediate(playerGo);
        }
        playerGo = new GameObject("Player");
        playerGo.transform.position = playerSpawnPos;
        playerGo.transform.rotation = Quaternion.identity;

        // Add character controller and FPC script
        CharacterController charController = playerGo.AddComponent<CharacterController>();
        charController.center = new Vector3(0, 0.9f, 0);
        charController.height = 1.8f;
        charController.radius = 0.35f;
        charController.slopeLimit = 45f;
        charController.stepOffset = 0.3f;

        FirstPersonController fpc = playerGo.AddComponent<FirstPersonController>();
        fpc.walkSpeed = 4.0f;
        fpc.mouseSensitivity = 2.0f;
        fpc.pushForce = 5.0f; // push force for doors

        // Parent and position Main Camera
        GameObject mainCamera = GameObject.FindWithTag("MainCamera");
        if (mainCamera != null)
        {
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
        }

        // 8. Auto-capture Screenshots for AI visual validation
        CaptureEditorScreenshots(generator, centerX, centerZ, generator.cellSize);

        // 9. Save Scene
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene, scenePath);
        Debug.Log("Dungeon Scene Auto Setup and Screenshot Capture Completed Successfully!");
        if (!Application.isBatchMode)
        {
            EditorUtility.DisplayDialog("Success", "Dungeon Test Scene has been generated, saved, and visual screenshots have been exported! Open 'Assets/Scenes/DungeonTest.unity' to view it.", "OK");
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
