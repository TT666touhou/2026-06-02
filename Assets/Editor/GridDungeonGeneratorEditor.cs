using UnityEngine;
using UnityEditor;

[CustomEditor(typeof(GridDungeonGenerator))]
public class GridDungeonGeneratorEditor : Editor
{
    public override void OnInspectorGUI()
    {
        // Draw the default inspector fields
        DrawDefaultInspector();

        GridDungeonGenerator generator = (GridDungeonGenerator)target;

        GUILayout.Space(15);

        // Styling for buttons
        GUIStyle genButtonStyle = new GUIStyle(GUI.skin.button);
        genButtonStyle.fontSize = 13;
        genButtonStyle.fontStyle = FontStyle.Bold;
        genButtonStyle.normal.textColor = Color.white;
        
        // Render Generate Button
        GUI.backgroundColor = new Color(0.18f, 0.55f, 0.34f); // Forest Green
        if (GUILayout.Button("Generate Dungeon Scene", genButtonStyle, GUILayout.Height(35)))
        {
            generator.GenerateDungeon();
            // Record undo action for Editor Mode
            Undo.RegisterCreatedObjectUndo(generator.gameObject, "Generate Dungeon");
        }

        // Render Clear Button
        GUI.backgroundColor = new Color(0.7f, 0.13f, 0.13f); // Crimson Red
        if (GUILayout.Button("Clear Generated Objects", genButtonStyle, GUILayout.Height(25)))
        {
            generator.ClearDungeon();
        }
        
        // Reset color
        GUI.backgroundColor = Color.white;
    }
}
