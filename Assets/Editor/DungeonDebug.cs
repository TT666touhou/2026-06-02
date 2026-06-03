using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;
using System.Text;
using System.IO;

public static class DungeonDebug
{
    [MenuItem("Tools/Debug Player Collision")]
    public static void DebugPlayerCollision()
    {
        EditorSceneManager.OpenScene("Assets/Scenes/DungeonTest.unity");
        GameObject player = GameObject.Find("Player");
        if (player == null)
        {
            Debug.LogError("Player GameObject not found in scene!");
            return;
        }

        Vector3 playerPos = player.transform.position;
        Debug.Log($"Player Spawning Position: {playerPos}");

        float radius = 0.25f;
        float height = 1.8f;
        Vector3 point1 = playerPos + new Vector3(0, radius, 0);
        Vector3 point2 = playerPos + new Vector3(0, height - radius, 0);

        StringBuilder sb = new StringBuilder();
        sb.AppendLine($"=== PLAYER SPAWN DEBUG REPORT ===");
        sb.AppendLine($"Player Position: {playerPos}");
        sb.AppendLine($"Capsule Point1: {point1}, Point2: {point2}, Radius: {radius}");
        sb.AppendLine();

        Collider[] colliders = Object.FindObjectsOfType<Collider>(true);
        int overlapCount = 0;

        foreach (var col in colliders)
        {
            if (col.gameObject == player) continue;
            if (col.transform.IsChildOf(player.transform)) continue;

            Bounds playerBounds = new Bounds(playerPos + new Vector3(0, height / 2.0f, 0), new Vector3(radius * 2.0f, height, radius * 2.0f));
            if (col.bounds.Intersects(playerBounds))
            {
                Vector3 closestPoint = col.ClosestPoint(playerPos + new Vector3(0, height / 2.0f, 0));
                float dist = Vector3.Distance(closestPoint, playerPos + new Vector3(0, height / 2.0f, 0));
                
                sb.AppendLine($"Potential overlapping object: {col.gameObject.name} (Tag: {col.gameObject.tag}, Layer: {col.gameObject.layer})");
                sb.AppendLine($"  Position: {col.transform.position}");
                sb.AppendLine($"  Collider Bounds Min: {col.bounds.min} Max: {col.bounds.max}");
                sb.AppendLine($"  Distance to player center: {dist:F3}m");
                sb.AppendLine();
                overlapCount++;
            }
        }

        sb.AppendLine($"Total overlapping colliders: {overlapCount}");
        string reportPath = "Assets/player_collision_debug.txt";
        File.WriteAllText(reportPath, sb.ToString());
        Debug.Log($"[DungeonDebug] Saved collision report to: {reportPath}");
        
        if (!Application.isBatchMode)
        {
            EditorUtility.DisplayDialog("Collision Debug", $"Total overlapping colliders: {overlapCount}\nReport saved to {reportPath}", "OK");
        }
    }
}
