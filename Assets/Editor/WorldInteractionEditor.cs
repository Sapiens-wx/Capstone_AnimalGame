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
                EditorGUILayout.HelpBox("Blocks Traversal controls hard collision. Sight Blocking independently controls animal cover; Match Traversal preserves the original behaviour. Add a separate WorldInteraction for grabbing or climbing.", MessageType.Info);
                DrawPropertiesExcluding(serializedObject, "m_Script", "kind", "box", "sprite", "localCenter", "localSize",
                    "requiredHands", "recyclable", "size", "pushSpeedMultiplier",
                    "climbableEntrySpeedMultiplier", "climbableEntryBlendDuration", "climbableCameraMultiplier",
                    "climbableRumbleMultiplier", "climbableLandingDurationMultiplier",
                    "climbableAffectsWholeArea", "climbableUseBodyOverlap");
            }
            else
            {
                EditorGUILayout.PropertyField(serializedObject.FindProperty("kind"));
                var climbKind = serializedObject.FindProperty("kind");
                bool climbable = (climbKind.intValue & (int)WorldInteractionKind.Climbable) != 0;
                if (climbable || climbKind.hasMultipleDifferentValues)
                {
                    foreach (string name in new[] { "climbableRadius", "slopeStrength01", "topRadiusRatio01",
                        "climbableEntrySpeedMultiplier", "climbableEntryBlendDuration", "climbableCameraMultiplier",
                        "climbableRumbleMultiplier", "climbableLandingDurationMultiplier",
                        "climbableAffectsWholeArea", "climbableUseBodyOverlap" })
                        if (name == "climbableEntrySpeedMultiplier")
                            EditorGUILayout.PropertyField(serializedObject.FindProperty(name), new GUIContent("Climbable Speed Multiplier"));
                        else EditorGUILayout.PropertyField(serializedObject.FindProperty(name));
                }
                if (!climbable || climbKind.hasMultipleDifferentValues)
                {
                    EditorGUILayout.PropertyField(serializedObject.FindProperty("box"));
                    EditorGUILayout.PropertyField(serializedObject.FindProperty("sprite"));
                    EditorGUILayout.PropertyField(serializedObject.FindProperty("localSize"));
                }
                EditorGUILayout.PropertyField(serializedObject.FindProperty("localCenter"));
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

        [DrawGizmo(GizmoType.Selected | GizmoType.Active)]
        private static void DrawClimbable(WorldInteraction item, GizmoType gizmoType)
        {
            if ((item.Kind & WorldInteractionKind.Climbable) == 0) return;
            MapTestSceneController map = Object.FindFirstObjectByType<MapTestSceneController>();
            if (map != null && map.gameObject.scene != item.gameObject.scene) map = null;
            InteractionShape shape = item.GetShape(map);
            if (shape.IsBox) return;
            Vector3 center = map != null && map.HasGeneratedMap
                ? map.MapPositionToWorld(shape.Center) : (Vector3)shape.Center;
            float sx = map != null && map.HasGeneratedMap ? map.MapMetersToWorldDistance(Vector2.right, 1f) : 1f;
            float sy = map != null && map.HasGeneratedMap ? map.MapMetersToWorldDistance(Vector2.up, 1f) : 1f;
            Color previous = Handles.color;
            foreach (float ratio in new[] { 1f, item.TopRadiusRatio01 })
            {
                Handles.color = ratio == 1f ? Color.cyan : Color.green;
                Vector3 last = center + new Vector3(shape.Radius * ratio * sx, 0f);
                for (int i = 1; i <= 64; i++)
                {
                    float angle = i * Mathf.PI * 2f / 64f;
                    Vector3 next = center + new Vector3(Mathf.Cos(angle) * sx, Mathf.Sin(angle) * sy) * shape.Radius * ratio;
                    Handles.DrawLine(last, next);
                    last = next;
                }
            }
            Handles.color = previous;
        }
    }
}
