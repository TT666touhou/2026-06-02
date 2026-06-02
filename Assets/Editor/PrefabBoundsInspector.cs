using UnityEngine;
using UnityEditor;
using System.Text;
using System.IO;
using System.Collections.Generic;

[InitializeOnLoad]
public static class PrefabBoundsInspector
{
    static PrefabBoundsInspector()
    {
        // Delay call to ensure Unity is fully loaded and database is ready
        EditorApplication.delayCall += InspectAllBounds;
    }

    [MenuItem("Tools/Inspect All Prefab Bounds")]
    public static void InspectAllBounds()
    {
        string folderPath = "Assets/Prefabs";
        if (!Directory.Exists(folderPath))
        {
            Debug.LogWarning($"Directory not found: {folderPath}");
            return;
        }

        string[] files = Directory.GetFiles(folderPath, "*.prefab", SearchOption.AllDirectories);

        StringBuilder sb = new StringBuilder();
        sb.AppendLine("=== ALL PREFAB BOUNDS REPORT ===\n");

        foreach (string file in files)
        {
            string assetPath = file.Replace("\\", "/");
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(assetPath);
            if (prefab == null) continue;

            GameObject instance = PrefabUtility.InstantiatePrefab(prefab) as GameObject;
            if (instance == null) continue;
            instance.transform.position = Vector3.zero;
            instance.transform.rotation = Quaternion.identity;
            instance.transform.localScale = Vector3.one;

            Renderer[] renderers = instance.GetComponentsInChildren<Renderer>();
            if (renderers.Length == 0)
            {
                Object.DestroyImmediate(instance);
                continue;
            }

            Bounds combined = renderers[0].bounds;
            foreach (var r in renderers)
                combined.Encapsulate(r.bounds);

            string name = Path.GetFileNameWithoutExtension(assetPath);
            sb.AppendLine($"{name}:");
            sb.AppendLine($"  Size    : {combined.size.x:F3} x {combined.size.y:F3} x {combined.size.z:F3}");
            sb.AppendLine($"  Center  : {combined.center.x:F3}, {combined.center.y:F3}, {combined.center.z:F3}");
            sb.AppendLine($"  Min     : {combined.min.x:F3}, {combined.min.y:F3}, {combined.min.z:F3}");
            sb.AppendLine($"  Max     : {combined.max.x:F3}, {combined.max.y:F3}, {combined.max.z:F3}");
            sb.AppendLine();

            Object.DestroyImmediate(instance);
        }

        string report = sb.ToString();
        string reportPath = "Assets/prefab_bounds_report_all.txt";
        File.WriteAllText(reportPath, report);
        Debug.Log($"[PrefabBoundsInspector] Auto-inspected bounds of {files.Length} prefabs and saved to {reportPath}");
    }
}
