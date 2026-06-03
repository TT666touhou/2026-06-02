using UnityEngine;
using UnityEditor;
using System.IO;

public static class KayKitPrefabGenerator
{
    [MenuItem("Tools/Generate KayKit Prefabs")]
    public static void GeneratePrefabs()
    {
        // 0. Ensure Tags and Layers exist in Project Settings
        AddTag("Chest");
        AddTag("Mimic");
        AddTag("Interactable");
        AddTag("Loot");
        AddTag("Light");

        AddLayer(8, "Environment");
        AddLayer(9, "Furniture");
        AddLayer(10, "Props");
        AddLayer(11, "Interactable");
        AddLayer(12, "Decoration");

        // 1. Create Material
        string matFolder = "Assets/ThirdParty/KayKit Dungeon/Materials";
        if (!Directory.Exists(matFolder))
        {
            Directory.CreateDirectory(matFolder);
        }
        
        string matPath = matFolder + "/dungeon_material.mat";
        Material mat = AssetDatabase.LoadAssetAtPath<Material>(matPath);
        if (mat == null)
        {
            mat = new Material(Shader.Find("Standard"));
            AssetDatabase.CreateAsset(mat, matPath);
        }
        
        Texture2D tex = AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/ThirdParty/KayKit Dungeon/Textures/dungeon_texture.png");
        if (tex != null)
        {
            mat.mainTexture = tex;
            // Set default material properties for a nice retro/matte look (low gloss/specular)
            mat.SetFloat("_Glossiness", 0.0f);
            mat.SetFloat("_Metallic", 0.0f);
            EditorUtility.SetDirty(mat);
        }
        else
        {
            Debug.LogError("Could not find dungeon_texture.png under Assets/ThirdParty/KayKit Dungeon/Textures/!");
        }
        
        AssetDatabase.SaveAssets();
        
        // 2. Create Prefabs output folder
        string prefabFolder = "Assets/Prefabs/KayKit Dungeon";
        if (!Directory.Exists(prefabFolder))
        {
            Directory.CreateDirectory(prefabFolder);
        }
        
        // Find all FBX files in the models directory
        string modelsFolder = "Assets/ThirdParty/KayKit Dungeon/Models";
        if (!Directory.Exists(modelsFolder))
        {
            Debug.LogError($"Models directory not found: {modelsFolder}");
            if (!Application.isBatchMode)
            {
                EditorUtility.DisplayDialog("Error", $"Models folder not found at {modelsFolder}!", "OK");
            }
            return;
        }

        string[] fbxFiles = Directory.GetFiles(modelsFolder, "*.fbx", SearchOption.AllDirectories);
        if (fbxFiles.Length == 0)
        {
            Debug.LogError($"No FBX model files found in {modelsFolder}!");
            if (!Application.isBatchMode)
            {
                EditorUtility.DisplayDialog("Error", "No FBX model files found in Models folder!", "OK");
            }
            return;
        }

        Debug.Log($"[KayKitPrefabGenerator] Found {fbxFiles.Length} model files to convert into Prefabs.");

        for (int i = 0; i < fbxFiles.Length; i++)
        {
            string file = fbxFiles[i];
            string assetPath = file.Replace("\\", "/");
            string prefabName = Path.GetFileNameWithoutExtension(assetPath);

            // Display progress bar in Editor
            if (!Application.isBatchMode)
            {
                EditorUtility.DisplayProgressBar(
                    "Generating KayKit Prefabs",
                    $"Processing model ({i + 1}/{fbxFiles.Length}): {prefabName}",
                    (float)i / fbxFiles.Length
                );
            }

            GameObject modelAsset = AssetDatabase.LoadAssetAtPath<GameObject>(assetPath);
            if (modelAsset == null)
            {
                Debug.LogError($"Failed to load model asset at: {assetPath}");
                continue;
            }

            // Instantiate the model asset in the scene
            GameObject instance = Object.Instantiate(modelAsset);
            instance.name = prefabName;

            // 3. Set material on all MeshRenderers recursively
            MeshRenderer[] renderers = instance.GetComponentsInChildren<MeshRenderer>(true);
            foreach (var renderer in renderers)
            {
                renderer.sharedMaterial = mat;
            }

            // 4. Remove all existing BoxColliders, MeshColliders, and Rigidbodies first to prevent duplicates/residues
            BoxCollider[] existingBoxes = instance.GetComponentsInChildren<BoxCollider>(true);
            foreach (var box in existingBoxes)
            {
                Object.DestroyImmediate(box);
            }

            MeshCollider[] existingMeshes = instance.GetComponentsInChildren<MeshCollider>(true);
            foreach (var mc in existingMeshes)
            {
                Object.DestroyImmediate(mc);
            }

            Rigidbody[] existingRbs = instance.GetComponentsInChildren<Rigidbody>(true);
            foreach (var rb in existingRbs)
            {
                Object.DestroyImmediate(rb);
            }

            // 5. Add Colliders recursively according to custom user specifications
            string nameLower = prefabName.ToLower();
            bool isBanner = nameLower.Contains("banner");
            bool isSpecialColliderObj = (nameLower == "barrel_small_stack") || 
                                         nameLower.Contains("chest") || 
                                         nameLower.Contains("mimic") || 
                                         nameLower.Contains("table");

            if (!isBanner && !isSpecialColliderObj)
            {
                MeshFilter[] meshFilters = instance.GetComponentsInChildren<MeshFilter>(true);
                foreach (var filter in meshFilters)
                {
                    // Skip empty meshes
                    if (filter.sharedMesh == null) continue;

                    MeshCollider mc = filter.gameObject.GetComponent<MeshCollider>();
                    if (mc == null)
                    {
                        mc = filter.gameObject.AddComponent<MeshCollider>();
                    }
                    mc.sharedMesh = filter.sharedMesh;
                }
            }
            else if (nameLower == "barrel_small_stack")
            {
                // barrel_small_stack uses 3 corresponding shapes (BoxColliders) to align with stack geometry
                BoxCollider b1 = instance.AddComponent<BoxCollider>();
                b1.center = new Vector3(-0.42f, 0.5f, 0f);
                b1.size = new Vector3(0.9f, 1.0f, 0.9f);

                BoxCollider b2 = instance.AddComponent<BoxCollider>();
                b2.center = new Vector3(0.42f, 0.5f, 0f);
                b2.size = new Vector3(0.9f, 1.0f, 0.9f);

                BoxCollider b3 = instance.AddComponent<BoxCollider>();
                b3.center = new Vector3(0f, 1.25f, 0f);
                b3.size = new Vector3(0.9f, 0.9f, 0.9f);
            }
            else if (nameLower.Contains("chest") || nameLower.Contains("mimic"))
            {
                // Chests use hollow structures made of multiple box colliders for both base and lid
                MeshFilter parentFilter = instance.GetComponent<MeshFilter>();
                if (parentFilter != null && parentFilter.sharedMesh != null)
                {
                    BuildHollowBox(instance, parentFilter.sharedMesh.bounds, 0.08f);
                }

                foreach (Transform child in instance.transform)
                {
                    if (child.name.ToLower().Contains("lid"))
                    {
                        MeshFilter childFilter = child.GetComponent<MeshFilter>();
                        if (childFilter != null && childFilter.sharedMesh != null)
                        {
                            BuildHollowLid(child.gameObject, childFilter.sharedMesh.bounds, 0.08f);
                        }
                    }
                }
            }
            else if (nameLower.Contains("table"))
            {
                // Tables use compound colliders: tabletop + 4 legs, with decorations handled separately
                MeshFilter mf = instance.GetComponent<MeshFilter>();
                if (mf != null && mf.sharedMesh != null)
                {
                    BuildTableColliders(instance, mf.sharedMesh.bounds, 0.10f, 0.12f);
                }
            }

            // 6. Dynamic light creation for torches/candles (2A)
            if (nameLower.Contains("torch_lit") || nameLower.Contains("torch_mounted") || 
                nameLower.Contains("candle_lit") || nameLower.Contains("candle_thin_lit"))
            {
                Transform existingLight = instance.transform.Find("Point Light");
                GameObject lightGo;
                if (existingLight == null)
                {
                    lightGo = new GameObject("Point Light");
                    lightGo.transform.parent = instance.transform;
                }
                else
                {
                    lightGo = existingLight.gameObject;
                }

                Light lightComp = lightGo.GetComponent<Light>();
                if (lightComp == null)
                {
                    lightComp = lightGo.AddComponent<Light>();
                }

                lightComp.type = LightType.Point;
                lightComp.shadows = LightShadows.Soft;

                if (nameLower.Contains("torch"))
                {
                    lightComp.color = new Color(1f, 0.55f, 0.1f); // Warm flame orange
                    lightComp.range = 8.0f;
                    lightComp.intensity = 2.0f;
                    lightGo.transform.localPosition = new Vector3(0f, nameLower.Contains("mounted") ? 1.0f : 1.2f, 0f);
                }
                else // candle
                {
                    lightComp.color = new Color(1f, 0.63f, 0.16f); // Warm yellowish flame
                    lightComp.range = 3.5f;
                    lightComp.intensity = 0.8f;
                    lightGo.transform.localPosition = new Vector3(0f, 0.5f, 0f);
                }
            }

            // 7. Determine Tag and Layer
            string tagStr;
            int layerInt;
            GetTagAndLayerForName(prefabName, out tagStr, out layerInt);
            SetTagAndLayerRecursively(instance, tagStr, layerInt);

            // 8. Configure Rigidbody and Convex Mesh Colliders for dynamic physics props
            ConfigurePhysics(instance, prefabName, layerInt);

            // 9. Determine Static Flags
            bool isDynamic = IsDynamicProp(nameLower);
            bool isStatic = (layerInt == 8 || layerInt == 9) && !isDynamic; // Static if Environment/Furniture and NOT dynamic
            if (isStatic)
            {
                StaticEditorFlags flags = StaticEditorFlags.BatchingStatic | StaticEditorFlags.ContributeGI | 
                                          StaticEditorFlags.OccluderStatic | StaticEditorFlags.OccludeeStatic;
                SetStaticFlagsRecursively(instance, flags);
            }
            else
            {
                SetStaticFlagsRecursively(instance, 0);
            }

            // 9.5. Custom doorway physics setup to avoid runtime component addition overhead
            if (nameLower.Contains("doorway"))
            {
                Transform doorPanel = null;
                string rootName = instance.name;
                foreach (Transform child in instance.GetComponentsInChildren<Transform>(true))
                {
                    if (child == instance.transform) continue;
                    
                    string childName = child.name;
                    string childNameLower = childName.ToLower();
                    
                    // Door panel criteria: contains 'door', is not the root name itself, and is not a handle or frame
                    if (childNameLower.Contains("door") && 
                        childName != rootName && 
                        !childNameLower.Contains("handle") && 
                        !childNameLower.Contains("frame"))
                    {
                        doorPanel = child;
                        break;
                    }
                }

                if (doorPanel != null)
                {
                    // Clear static flags specifically for door panel and its children so it can rotate smoothly
                    SetStaticFlagsRecursively(doorPanel.gameObject, 0);

                    // Ensure doorframe (root) has a kinematic Rigidbody to anchor the hinge
                    Rigidbody frameRb = instance.GetComponent<Rigidbody>();
                    if (frameRb == null)
                    {
                        frameRb = instance.AddComponent<Rigidbody>();
                    }
                    frameRb.isKinematic = true;

                    // Remove existing MeshCollider on the door panel immediately in Edit Mode
                    MeshCollider existingMc = doorPanel.GetComponent<MeshCollider>();
                    if (existingMc != null)
                    {
                        Object.DestroyImmediate(existingMc);
                    }

                    // Add BoxCollider to the door panel
                    BoxCollider doorCollider = doorPanel.gameObject.GetComponent<BoxCollider>();
                    if (doorCollider == null)
                    {
                        doorCollider = doorPanel.gameObject.AddComponent<BoxCollider>();
                    }
                    MeshFilter mf = doorPanel.GetComponent<MeshFilter>();
                    if (mf != null && mf.sharedMesh != null)
                    {
                        doorCollider.center = mf.sharedMesh.bounds.center;
                        doorCollider.size = mf.sharedMesh.bounds.size;
                    }
                    else
                    {
                        doorCollider.center = new Vector3(0.49f, 1.06f, 0.04f);
                        doorCollider.size = new Vector3(1.01f, 2.07f, 0.09f);
                    }

                    // Add Rigidbody to the door panel for real physics
                    Rigidbody doorRb = doorPanel.gameObject.GetComponent<Rigidbody>();
                    if (doorRb == null)
                    {
                        doorRb = doorPanel.gameObject.AddComponent<Rigidbody>();
                    }
                    doorRb.mass = 15f;
                    doorRb.drag = 1.0f;
                    doorRb.angularDrag = 3.0f;
                    doorRb.useGravity = true;

                    // Add HingeJoint to the door panel
                    HingeJoint doorHinge = doorPanel.gameObject.GetComponent<HingeJoint>();
                    if (doorHinge == null)
                    {
                        doorHinge = doorPanel.gameObject.AddComponent<HingeJoint>();
                    }
                    doorHinge.connectedBody = frameRb;
                    doorHinge.anchor = Vector3.zero; // Pivot points are already at the hinge side
                    doorHinge.axis = Vector3.up;     // Y-axis rotation
                    doorHinge.useLimits = true;
                    JointLimits limits = new JointLimits();
                    limits.min = -120f;
                    limits.max = 120f;
                    limits.bounciness = 0.1f;
                    doorHinge.limits = limits;

                    // Ensure PhysicalDoor runtime script is present on the root to handle Interact triggers
                    PhysicalDoor physicalDoorScript = instance.GetComponent<PhysicalDoor>();
                    if (physicalDoorScript == null)
                    {
                        physicalDoorScript = instance.AddComponent<PhysicalDoor>();
                    }
                }
            }

            // 10. Save as Prefab
            string prefabPath = $"{prefabFolder}/{prefabName}.prefab";
            PrefabUtility.SaveAsPrefabAsset(instance, prefabPath);

            // Destroy the scene instance
            Object.DestroyImmediate(instance);
        }

        // Clear progress bar when finished
        if (!Application.isBatchMode)
        {
            EditorUtility.ClearProgressBar();
        }

        AssetDatabase.Refresh();
        Debug.Log($"[KayKitPrefabGenerator] Successfully converted {fbxFiles.Length} models into Prefabs in {prefabFolder}");

        if (!Application.isBatchMode)
        {
            EditorUtility.DisplayDialog(
                "Success",
                $"Successfully generated {fbxFiles.Length} KayKit prefabs with perfect MeshColliders, PointLights, Tags, Layers, and Static flags!",
                "OK"
            );
        }
    }

    private static void AddTag(string tag)
    {
        SerializedObject tagManager = new SerializedObject(AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/TagManager.asset")[0]);
        SerializedProperty tagsProp = tagManager.FindProperty("tags");
        
        bool exists = false;
        for (int i = 0; i < tagsProp.arraySize; i++)
        {
            if (tagsProp.GetArrayElementAtIndex(i).stringValue == tag)
            {
                exists = true;
                break;
            }
        }
        
        if (!exists)
        {
            tagsProp.InsertArrayElementAtIndex(tagsProp.arraySize);
            tagsProp.GetArrayElementAtIndex(tagsProp.arraySize - 1).stringValue = tag;
            tagManager.ApplyModifiedProperties();
        }
    }

    private static void AddLayer(int index, string name)
    {
        SerializedObject tagManager = new SerializedObject(AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/TagManager.asset")[0]);
        SerializedProperty layersProp = tagManager.FindProperty("layers");
        
        if (index >= 8 && index < layersProp.arraySize) // customizable user layers start at 8
        {
            SerializedProperty layerProp = layersProp.GetArrayElementAtIndex(index);
            if (string.IsNullOrEmpty(layerProp.stringValue) || layerProp.stringValue != name)
            {
                layerProp.stringValue = name;
                tagManager.ApplyModifiedProperties();
            }
        }
    }

    private static void GetTagAndLayerForName(string name, out string tag, out int layer)
    {
        string nameLower = name.ToLower();
        tag = "Untagged";
        layer = 0; // Default

        // Tag determination
        if (nameLower.Contains("chest") && !nameLower.Contains("mimic"))
        {
            tag = "Chest";
        }
        else if (nameLower.Contains("mimic"))
        {
            tag = "Mimic";
        }
        else if (nameLower.Contains("trunk") || nameLower.Contains("key") || nameLower.Contains("keyring"))
        {
            tag = "Interactable";
        }
        else if (nameLower.Contains("coin") || nameLower.Contains("book_"))
        {
            tag = "Loot";
        }
        else if (nameLower.Contains("torch_lit") || nameLower.Contains("candle_lit") || nameLower.Contains("candle_thin_lit"))
        {
            tag = "Light";
        }

        // Layer determination
        if (nameLower.Contains("floor") || nameLower.Contains("wall") || nameLower.Contains("ceiling") || 
            nameLower.Contains("column") || nameLower.Contains("pillar") || nameLower.Contains("stairs") || 
            nameLower.Contains("scaffold") || nameLower.Contains("rocks") || nameLower.Contains("rubble") || 
            nameLower.Contains("post"))
        {
            layer = 8; // Environment
        }
        else if (nameLower.Contains("table") || nameLower.Contains("chair") || nameLower.Contains("bed") || 
                 nameLower.Contains("bookcase") || nameLower.Contains("shelf") || nameLower.Contains("shelves") || 
                 nameLower.Contains("bar_") || nameLower.Contains("bartop") || nameLower.Contains("bench") || 
                 nameLower.Contains("stool"))
        {
            layer = 9; // Furniture
        }
        else if (nameLower.Contains("barrel") || nameLower.Contains("box") || nameLower.Contains("crate") || 
                 nameLower.Contains("keg") || nameLower.Contains("bucket") || nameLower.Contains("barrier"))
        {
            layer = 10; // Props
        }
        else if (nameLower.Contains("chest") || nameLower.Contains("mimic") || nameLower.Contains("trunk") || 
                 nameLower.Contains("key") || nameLower.Contains("keyring"))
        {
            layer = 11; // Interactable
        }
        else if (nameLower.Contains("banner") || nameLower.Contains("candle") || nameLower.Contains("torch") || 
                 nameLower.Contains("coin") || nameLower.Contains("book_") || nameLower.Contains("bottle") || 
                 nameLower.Contains("plate") || nameLower.Contains("sword") || nameLower.Contains("shield") || 
                 nameLower.Contains("pickaxe"))
        {
            layer = 12; // Decoration
        }
    }

    private static void SetTagAndLayerRecursively(GameObject go, string tag, int layer)
    {
        go.layer = layer;
        if (tag != "Untagged")
        {
            go.tag = tag;
        }
        foreach (Transform child in go.transform)
        {
            SetTagAndLayerRecursively(child.gameObject, tag, layer);
        }
    }

    private static void SetStaticFlagsRecursively(GameObject go, StaticEditorFlags flags)
    {
        string nameLower = go.name.ToLower();
        if (nameLower.Contains("door") || nameLower.Contains("gate"))
        {
            GameObjectUtility.SetStaticEditorFlags(go, 0);
            foreach (Transform child in go.transform)
            {
                SetStaticFlagsRecursively(child.gameObject, 0);
            }
            return;
        }

        GameObjectUtility.SetStaticEditorFlags(go, flags);
        foreach (Transform child in go.transform)
        {
            SetStaticFlagsRecursively(child.gameObject, flags);
        }
    }

    private static void BuildHollowBox(GameObject go, Bounds bounds, float thickness)
    {
        Vector3 min = bounds.min;
        Vector3 max = bounds.max;
        Vector3 size = bounds.size;
        Vector3 center = bounds.center;

        // 1. Bottom floor
        BoxCollider bottom = go.AddComponent<BoxCollider>();
        bottom.center = new Vector3(center.x, min.y + thickness / 2f, center.z);
        bottom.size = new Vector3(size.x, thickness, size.z);

        // 2. Left Wall
        BoxCollider left = go.AddComponent<BoxCollider>();
        left.center = new Vector3(min.x + thickness / 2f, center.y + thickness / 2f, center.z);
        left.size = new Vector3(thickness, size.y - thickness, size.z);

        // 3. Right Wall
        BoxCollider right = go.AddComponent<BoxCollider>();
        right.center = new Vector3(max.x - thickness / 2f, center.y + thickness / 2f, center.z);
        right.size = new Vector3(thickness, size.y - thickness, size.z);

        // 4. Front Wall
        BoxCollider front = go.AddComponent<BoxCollider>();
        front.center = new Vector3(center.x, center.y + thickness / 2f, max.z - thickness / 2f);
        front.size = new Vector3(size.x - 2f * thickness, size.y - thickness, thickness);

        // 5. Back Wall
        BoxCollider back = go.AddComponent<BoxCollider>();
        back.center = new Vector3(center.x, center.y + thickness / 2f, min.z + thickness / 2f);
        back.size = new Vector3(size.x - 2f * thickness, size.y - thickness, thickness);
    }

    private static void BuildHollowLid(GameObject go, Bounds bounds, float thickness)
    {
        Vector3 min = bounds.min;
        Vector3 max = bounds.max;
        Vector3 size = bounds.size;
        Vector3 center = bounds.center;

        // 1. Top roof
        BoxCollider top = go.AddComponent<BoxCollider>();
        top.center = new Vector3(center.x, max.y - thickness / 2f, center.z);
        top.size = new Vector3(size.x, thickness, size.z);

        // 2. Left Wall
        BoxCollider left = go.AddComponent<BoxCollider>();
        left.center = new Vector3(min.x + thickness / 2f, center.y - thickness / 2f, center.z);
        left.size = new Vector3(thickness, size.y - thickness, size.z);

        // 3. Right Wall
        BoxCollider right = go.AddComponent<BoxCollider>();
        right.center = new Vector3(max.x - thickness / 2f, center.y - thickness / 2f, center.z);
        right.size = new Vector3(thickness, size.y - thickness, size.z);

        // 4. Front Wall
        BoxCollider front = go.AddComponent<BoxCollider>();
        front.center = new Vector3(center.x, center.y - thickness / 2f, max.z - thickness / 2f);
        front.size = new Vector3(size.x - 2f * thickness, size.y - thickness, thickness);

        // 5. Back Wall
        BoxCollider back = go.AddComponent<BoxCollider>();
        back.center = new Vector3(center.x, center.y - thickness / 2f, min.z + thickness / 2f);
        back.size = new Vector3(size.x - 2f * thickness, size.y - thickness, thickness);
    }

    private static void BuildTableColliders(GameObject go, Bounds bounds, float topThickness, float legWidth)
    {
        Vector3 min = bounds.min;
        Vector3 max = bounds.max;
        Vector3 size = bounds.size;
        Vector3 center = bounds.center;

        // Standard clean table height in KayKit Dungeon is always 1.0f
        float tableHeight = 1.0f;
        if (size.y < 1.0f)
        {
            tableHeight = size.y;
        }

        // 1. Tabletop
        BoxCollider top = go.AddComponent<BoxCollider>();
        top.center = new Vector3(center.x, min.y + tableHeight - topThickness / 2f, center.z);
        top.size = new Vector3(size.x, topThickness, size.z);

        // 2. Leg Front-Left
        BoxCollider legFL = go.AddComponent<BoxCollider>();
        legFL.center = new Vector3(min.x + legWidth / 2f, min.y + (tableHeight - topThickness) / 2f, min.z + legWidth / 2f);
        legFL.size = new Vector3(legWidth, tableHeight - topThickness, legWidth);

        // 3. Leg Front-Right
        BoxCollider legFR = go.AddComponent<BoxCollider>();
        legFR.center = new Vector3(max.x - legWidth / 2f, min.y + (tableHeight - topThickness) / 2f, min.z + legWidth / 2f);
        legFR.size = new Vector3(legWidth, tableHeight - topThickness, legWidth);

        // 4. Leg Back-Left
        BoxCollider legBL = go.AddComponent<BoxCollider>();
        legBL.center = new Vector3(min.x + legWidth / 2f, min.y + (tableHeight - topThickness) / 2f, max.z - legWidth / 2f);
        legBL.size = new Vector3(legWidth, tableHeight - topThickness, legWidth);

        // 5. Leg Back-Right
        BoxCollider legBR = go.AddComponent<BoxCollider>();
        legBR.center = new Vector3(max.x - legWidth / 2f, min.y + (tableHeight - topThickness) / 2f, max.z - legWidth / 2f);
        legBR.size = new Vector3(legWidth, tableHeight - topThickness, legWidth);

        // 6. If there are decorations stacked on top of the table (like coins/food/plates)
        if (size.y > tableHeight + 0.05f)
        {
            float decorHeight = size.y - tableHeight;
            BoxCollider decorCol = go.AddComponent<BoxCollider>();
            decorCol.center = new Vector3(center.x, min.y + tableHeight + decorHeight / 2f, center.z);
            decorCol.size = new Vector3(size.x * 0.85f, decorHeight, size.z * 0.85f);
        }
    }

    private static bool IsDynamicProp(string nameLower)
    {
        return nameLower.Contains("barrel") || 
               nameLower.Contains("bench") || 
               nameLower.Contains("book") || 
               nameLower.Contains("bottle") || 
               nameLower.Contains("box") || 
               nameLower.Contains("candle") || 
               nameLower.Contains("chair") || 
               nameLower.Contains("chest") || 
               nameLower.Contains("mimic") || 
               nameLower.Contains("coin") || 
               nameLower.Contains("crate") || 
               nameLower.Contains("keg") || 
               nameLower.Contains("key") || 
               nameLower.Contains("pickaxe") || 
               nameLower.Contains("plate") || 
               nameLower.Contains("stool") || 
               nameLower.Contains("sword") || 
               nameLower.Contains("table") || 
               nameLower.Contains("torch") || 
               nameLower.Contains("trunk");
    }

    private static void ConfigurePhysics(GameObject root, string name, int layer)
    {
        string nameLower = name.ToLower();
        bool isDynamic = IsDynamicProp(nameLower);

        if (isDynamic)
        {
            Rigidbody rb = root.GetComponent<Rigidbody>();
            if (rb == null)
            {
                rb = root.AddComponent<Rigidbody>();
            }

            // Assign mass dynamically based on class specifications
            float mass = 1.0f;
            if (nameLower.Contains("barrel")) mass = 15.0f;
            else if (nameLower.Contains("bench")) mass = 12.0f;
            else if (nameLower.Contains("bookcase")) mass = 40.0f;
            else if (nameLower.Contains("book")) mass = 0.5f;
            else if (nameLower.Contains("bottle")) mass = 0.8f;
            else if (nameLower.Contains("box") || nameLower.Contains("crate")) mass = 10.0f;
            else if (nameLower.Contains("candle") || nameLower.Contains("key") || nameLower.Contains("coin") || nameLower.Contains("plate")) mass = 0.2f;
            else if (nameLower.Contains("chair") || nameLower.Contains("stool")) mass = 5.0f;
            else if (nameLower.Contains("chest") || nameLower.Contains("mimic") || nameLower.Contains("trunk")) mass = 25.0f;
            else if (nameLower.Contains("pickaxe") || nameLower.Contains("sword")) mass = 2.0f;
            else if (nameLower.Contains("table")) mass = 25.0f;
            else if (nameLower.Contains("torch")) mass = 1.0f;

            rb.mass = mass;

            // Make all MeshColliders on this object and its children Convex!
            MeshCollider[] meshColliders = root.GetComponentsInChildren<MeshCollider>(true);
            foreach (var mc in meshColliders)
            {
                mc.convex = true;
            }
        }
    }
}
