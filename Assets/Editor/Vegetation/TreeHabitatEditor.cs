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

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Appearance", EditorStyles.boldLabel);
            DrawScale(tree.transform.Find(TreePlacementScale.LeavesGroup), "Leaf Scale");
            DrawScale(tree.transform.Find(TreePlacementScale.TrunkGroup), "Trunk Scale");
        }

        private static void DrawScale(Transform group, string label)
        {
            if (group == null)
                return;

            EditorGUI.BeginChangeCheck();
            float scale = EditorGUILayout.FloatField(label, group.localScale.x);
            if (!EditorGUI.EndChangeCheck())
                return;

            Undo.RecordObject(group, "Change " + label);
            group.localScale = Vector3.one * Mathf.Max(0f, scale);
            PrefabUtility.RecordPrefabInstancePropertyModifications(group);
        }
    }
}
