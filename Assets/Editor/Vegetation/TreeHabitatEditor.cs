using AnimalGame.MapTest;
using UnityEditor;
using UnityEngine;

namespace AnimalGame.Editor
{
    [CustomEditor(typeof(TreeHabitat))]
    public sealed class TreeHabitatEditor : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();
            var tree = (TreeHabitat)target;
            if (!tree.IsDead && (tree.CrownRadiiMeters.x <= 0f || tree.CrownRadiiMeters.y <= 0f))
                EditorGUILayout.HelpBox("No crown area is authored. Birds use the safe trunk ring until Crown Radii Metres is configured.", MessageType.Warning);
        }
    }
}
