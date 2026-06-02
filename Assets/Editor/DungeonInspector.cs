using UnityEngine;
using UnityEditor;
using System.Text;
using System.IO;

public static class DungeonInspector
{
    [MenuItem("Tools/Dump Doorway Prefab Structure")]
    public static void DumpStructure()
    {
        string path = "Assets/Prefabs/PSX Bunkers v1.8.8/doorway_2_plain.prefab";
        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
        if (prefab == null)
        {
            Debug.LogError($"Prefab not found at: {path}");
            return;
        }

        StringBuilder sb = new StringBuilder();
        sb.AppendLine($"=== PREFAB STRUCTURE: {prefab.name} ===");
        DumpChild(prefab.transform, sb, 0);

        string reportPath = "Assets/doorway_prefab_structure.txt";
        File.WriteAllText(reportPath, sb.ToString());
        Debug.Log($"[DungeonInspector] Successfully dumped structure to {reportPath}:\n{sb.ToString()}");
    }

    private static void DumpChild(Transform t, StringBuilder sb, int indent)
    {
        string indentStr = new string(' ', indent * 2);
        sb.AppendLine($"{indentStr}- Name: {t.name}");
        sb.AppendLine($"{indentStr}  Pos : {t.localPosition.x:F3}, {t.localPosition.y:F3}, {t.localPosition.z:F3}");
        sb.AppendLine($"{indentStr}  Rot : {t.localEulerAngles.x:F3}, {t.localEulerAngles.y:F3}, {t.localEulerAngles.z:F3}");
        sb.AppendLine($"{indentStr}  Scale: {t.localScale.x:F3}, {t.localScale.y:F3}, {t.localScale.z:F3}");
        
        MeshFilter mf = t.GetComponent<MeshFilter>();
        if (mf != null && mf.sharedMesh != null)
        {
            Bounds b = mf.sharedMesh.bounds;
            sb.AppendLine($"{indentStr}  MeshBounds: Center={b.center.ToString()}, Size={b.size.ToString()}");
        }

        for (int i = 0; i < t.childCount; i++)
        {
            DumpChild(t.GetChild(i), sb, indent + 1);
        }
    }
}
