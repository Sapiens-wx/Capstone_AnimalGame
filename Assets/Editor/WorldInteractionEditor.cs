using AnimalGame.MapTest;
using AnimalGame.World;
using UnityEditor;
using UnityEngine;

namespace AnimalGame.Editor
{
    [InitializeOnLoad, CustomEditor(typeof(WorldInteraction), true), CanEditMultipleObjects]
    public sealed class WorldInteractionEditor : UnityEditor.Editor
    {
        static WorldInteractionEditor()
        {
            // Editor events, not Update polling. Unity-owned geometry sources may
            // be referenced from outside their hierarchy, so invalidate on these edits.
            Undo.postprocessModifications += OnModified;
            Undo.undoRedoPerformed += WorldInteractionQuery.InvalidateSpatialIndex;
        }

        private static UndoPropertyModification[] OnModified(UndoPropertyModification[] changes)
        {
            foreach (var change in changes)
            {
                Object changed = change.currentValue.target;
                if (changed is Transform || changed is BoxCollider2D || changed is SpriteRenderer || changed is MapTestSceneController)
                {
                    WorldInteractionQuery.InvalidateSpatialIndex();
                    break;
                }
            }
            return changes;
        }

        public override void OnInspectorGUI()
        {
            serializedObject.Update();
            if (target is HeightMapObstacleFootprint)
            {
                EditorGUILayout.HelpBox("Legacy map footprint: Collision. Add a separate WorldInteraction component for grabbing.", MessageType.Info);
                DrawPropertiesExcluding(serializedObject, "m_Script", "kind", "box", "sprite", "localCenter", "localSize",
                    "requiredHands", "recyclable", "size", "pushSpeedMultiplier");
            }
            else
            {
                EditorGUILayout.PropertyField(serializedObject.FindProperty("kind"));
                EditorGUILayout.PropertyField(serializedObject.FindProperty("box"));
                EditorGUILayout.PropertyField(serializedObject.FindProperty("sprite"));
                EditorGUILayout.PropertyField(serializedObject.FindProperty("localCenter"));
                EditorGUILayout.PropertyField(serializedObject.FindProperty("localSize"));
                var kind = serializedObject.FindProperty("kind");
                if (kind.hasMultipleDifferentValues || (kind.intValue & (int)WorldInteractionKind.Pushable) != 0)
                {
                    EditorGUILayout.PropertyField(serializedObject.FindProperty("pushSpeedMultiplier"));
                    if (!kind.hasMultipleDifferentValues
                        && (kind.intValue & (int)(WorldInteractionKind.Collision | WorldInteractionKind.BodyCollision)) == 0)
                        EditorGUILayout.HelpBox("Pushable also needs Collision or BodyCollision on this component.", MessageType.Warning);
                }
                if (kind.hasMultipleDifferentValues || (kind.intValue & (int)WorldInteractionKind.Grabbable) != 0)
                {
                    foreach (string name in new[] { "grabResistance", "requiredHands", "recyclable", "size" })
                        EditorGUILayout.PropertyField(serializedObject.FindProperty(name));
                }
            }
            serializedObject.ApplyModifiedProperties();
        }
    }
}
