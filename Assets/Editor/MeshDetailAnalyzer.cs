using UnityEngine;
using UnityEditor;
using System.IO;
using System.Text;

public static class MeshDetailAnalyzer
{
    [MenuItem("Tools/Analyze All Meshes")]
    public static void AnalyzeMeshes()
    {
        string modelsFolder = "Assets/ThirdParty/KayKit Dungeon/Models";
        if (!Directory.Exists(modelsFolder))
        {
            Debug.LogError($"Models directory not found: {modelsFolder}");
            return;
        }

        string[] fbxFiles = Directory.GetFiles(modelsFolder, "*.fbx", SearchOption.AllDirectories);
        StringBuilder sb = new StringBuilder();
        sb.AppendLine("# MESH DETAIL ANALYSIS REPORT");
        sb.AppendLine("This report details the exact vertices bounds, pivot offsets, and variations of all models.\n");

        foreach (string file in fbxFiles)
        {
            string assetPath = file.Replace("\\", "/");
            string modelName = Path.GetFileNameWithoutExtension(assetPath);
            GameObject model = AssetDatabase.LoadAssetAtPath<GameObject>(assetPath);
            if (model == null) continue;

            sb.AppendLine($"## {modelName}");
            
            // Analyze MeshFilters
            MeshFilter[] meshFilters = model.GetComponentsInChildren<MeshFilter>(true);
            sb.AppendLine($"MeshFilters Count: {meshFilters.Length}");
            
            foreach (var mf in meshFilters)
            {
                if (mf.sharedMesh == null) continue;
                Mesh mesh = mf.sharedMesh;
                Bounds bounds = mesh.bounds;
                sb.AppendLine($"  - SubMesh: {mf.name}");
                sb.AppendLine($"    Vertex Count: {mesh.vertexCount}");
                sb.AppendLine($"    Local Bounds Size: {bounds.size.x:F4} x {bounds.size.y:F4} x {bounds.size.z:F4}");
                sb.AppendLine($"    Local Bounds Center: {bounds.center.x:F4}, {bounds.center.y:F4}, {bounds.center.z:F4}");
                sb.AppendLine($"    Local Bounds Min: {bounds.min.x:F4}, {bounds.min.y:F4}, {bounds.min.z:F4}");
                sb.AppendLine($"    Local Bounds Max: {bounds.max.x:F4}, {bounds.max.y:F4}, {bounds.max.z:F4}");
            }
            sb.AppendLine();
        }

        string outputPath = "Assets/mesh_detail_report.txt";
        File.WriteAllText(outputPath, sb.ToString());
        Debug.Log($"[MeshDetailAnalyzer] Mesh analysis complete. Report written to: {outputPath}");
    }
}
