using UnityEngine;
using UnityEditor;
using System.IO;
using System.Collections.Generic;

public class DungeonPrefabCreator : EditorWindow
{
    private string sourceFolder = "Assets/ThirdParty/PSX Bunkers v1.8.8";
    private string destinationFolder = "Assets/Prefabs/Bunkers";
    private bool addMeshCollider = true;

    [MenuItem("Tools/Dungeon Prefab Creator")]
    public static void ShowWindow()
    {
        GetWindow<DungeonPrefabCreator>("Prefab Creator");
    }

    private void OnGUI()
    {
        GUILayout.Label("Batch Convert FBX to Prefabs", EditorStyles.boldLabel);
        
        sourceFolder = EditorGUILayout.TextField("Single Source Folder", sourceFolder);
        destinationFolder = EditorGUILayout.TextField("Single Dest Folder", destinationFolder);
        addMeshCollider = EditorGUILayout.Toggle("Add Mesh Collider", addMeshCollider);

        GUILayout.Space(10);

        if (GUILayout.Button("Convert Single Folder"))
        {
            ConvertSingleFolder();
        }

        GUILayout.Space(15);
        GUILayout.Label("Bulk Process (Assets/ThirdParty -> Assets/Prefabs)", EditorStyles.boldLabel);

        if (GUILayout.Button("Convert All 3D Folders in ThirdParty"))
        {
            ProcessAllFolders();
        }
    }

    private void ConvertSingleFolder()
    {
        if (!Directory.Exists(sourceFolder))
        {
            Debug.LogError("Source folder does not exist: " + sourceFolder);
            return;
        }

        string[] fbxFiles = Directory.GetFiles(sourceFolder, "*.fbx", SearchOption.AllDirectories);
        string[] glbFiles = Directory.GetFiles(sourceFolder, "*.glb", SearchOption.AllDirectories);
        string[] blendFiles = Directory.GetFiles(sourceFolder, "*.blend", SearchOption.AllDirectories);

        int count = CreatePrefabsForFolder(sourceFolder, destinationFolder, fbxFiles, glbFiles, blendFiles);

        AssetDatabase.Refresh();
        Debug.Log($"Successfully created {count} prefabs in {destinationFolder}");
        EditorUtility.DisplayDialog("Success", $"Created {count} prefabs successfully!", "OK");
    }

    private void ProcessAllFolders()
    {
        string parentSource = "Assets/ThirdParty";
        string parentDest = "Assets/Prefabs";

        if (!Directory.Exists(parentSource))
        {
            Debug.LogError("Parent source folder does not exist: " + parentSource);
            return;
        }

        string[] subfolders = Directory.GetDirectories(parentSource);
        int totalCreated = 0;

        foreach (string subfolder in subfolders)
        {
            string folderName = Path.GetFileName(subfolder);
            
            // Gather all 3D files in this specific folder
            string[] fbxFiles = Directory.GetFiles(subfolder, "*.fbx", SearchOption.AllDirectories);
            string[] glbFiles = Directory.GetFiles(subfolder, "*.glb", SearchOption.AllDirectories);
            string[] blendFiles = Directory.GetFiles(subfolder, "*.blend", SearchOption.AllDirectories);

            // Skip folders with no 3D models (like audio packs or pure textures)
            if (fbxFiles.Length == 0 && glbFiles.Length == 0 && blendFiles.Length == 0)
            {
                continue;
            }

            string destFolder = Path.Combine(parentDest, folderName).Replace("\\", "/");
            totalCreated += CreatePrefabsForFolder(subfolder, destFolder, fbxFiles, glbFiles, blendFiles);
        }

        AssetDatabase.Refresh();
        Debug.Log($"Batch process completed. Created {totalCreated} prefabs in total.");
        EditorUtility.DisplayDialog("Success", $"Created {totalCreated} prefabs across all 3D folders!", "OK");
    }

    private int CreatePrefabsForFolder(string src, string dest, string[] fbxFiles, string[] glbFiles, string[] blendFiles)
    {
        if (!Directory.Exists(dest))
        {
            Directory.CreateDirectory(dest);
        }

        int count = 0;
        List<string> modelPaths = new List<string>();
        modelPaths.AddRange(fbxFiles);
        modelPaths.AddRange(glbFiles);
        modelPaths.AddRange(blendFiles);

        foreach (string modelPath in modelPaths)
        {
            GameObject modelAsset = AssetDatabase.LoadAssetAtPath<GameObject>(modelPath);
            if (modelAsset == null) continue;

            GameObject tempGo = PrefabUtility.InstantiatePrefab(modelAsset) as GameObject;
            if (tempGo == null) continue;

            if (addMeshCollider)
            {
                MeshRenderer[] renderers = tempGo.GetComponentsInChildren<MeshRenderer>();
                foreach (var renderer in renderers)
                {
                    if (renderer.gameObject.GetComponent<Collider>() == null)
                    {
                        MeshFilter filter = renderer.gameObject.GetComponent<MeshFilter>();
                        if (filter != null && filter.sharedMesh != null)
                        {
                            MeshCollider mc = renderer.gameObject.AddComponent<MeshCollider>();
                            mc.sharedMesh = filter.sharedMesh;
                        }
                    }
                }
            }

            string fileName = Path.GetFileNameWithoutExtension(modelPath);
            string prefabPath = Path.Combine(dest, fileName + ".prefab").Replace("\\", "/");

            PrefabUtility.SaveAsPrefabAsset(tempGo, prefabPath);
            DestroyImmediate(tempGo);
            count++;
        }

        return count;
    }
}
