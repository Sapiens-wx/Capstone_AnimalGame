using AnimalGame.MapTest;
using AnimalGame.World;
using UnityEditor;

namespace AnimalGame.Editor
{
    [CustomEditor(typeof(WorldInteraction), true), CanEditMultipleObjects]
    public sealed class WorldInteractionEditor : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            serializedObject.Update();
            if (target is HeightMapObstacleFootprint)
            {
                EditorGUILayout.HelpBox("Legacy map footprint: Collision. Add a separate WorldInteraction component for grabbing.", MessageType.Info);
                DrawPropertiesExcluding(serializedObject, "m_Script", "kind", "box", "sprite", "localCenter", "localSize",
                    "requiredHands", "recyclable", "size", "onGrabbed", "onReleased", "onRecycled");
            }
            else
            {
                EditorGUILayout.PropertyField(serializedObject.FindProperty("kind"));
                EditorGUILayout.PropertyField(serializedObject.FindProperty("box"));
                EditorGUILayout.PropertyField(serializedObject.FindProperty("sprite"));
                EditorGUILayout.PropertyField(serializedObject.FindProperty("localCenter"));
                EditorGUILayout.PropertyField(serializedObject.FindProperty("localSize"));
                var kind = serializedObject.FindProperty("kind");
                if (kind.hasMultipleDifferentValues || kind.enumValueIndex == (int)WorldInteractionKind.Grabbable)
                {
                    foreach (string name in new[] { "requiredHands", "recyclable", "size", "onGrabbed", "onReleased", "onRecycled" })
                        EditorGUILayout.PropertyField(serializedObject.FindProperty(name));
                }
            }
            serializedObject.ApplyModifiedProperties();
        }
    }
}
