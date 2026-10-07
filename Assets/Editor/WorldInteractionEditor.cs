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
                DrawPropertiesExcluding(serializedObject, "m_Script", "kind", "box", "sprite", "colliderType", "colliderShape", "colliderCenter", "colliderRadius", "localSize",
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
                    DrawColliderSettings();
                if (climbable || climbKind.hasMultipleDifferentValues)
                    EditorGUILayout.PropertyField(serializedObject.FindProperty("colliderCenter"));
                EditorGUILayout.PropertyField(serializedObject.FindProperty("motionRoot"));
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

        private void DrawColliderSettings()
        {
            var type = serializedObject.FindProperty("colliderType");
            EditorGUILayout.PropertyField(type);
            bool mixed = type.hasMultipleDifferentValues;
            bool custom = mixed || type.enumValueIndex == (int)ColliderType.Custom;
            foreach (var sourceType in new[] { ColliderType.Sprite, ColliderType.Collider2D })
            {
                if (!mixed && type.enumValueIndex != (int)sourceType) continue;
                var source = serializedObject.FindProperty(sourceType == ColliderType.Sprite ? "sprite" : "box");
                EditorGUILayout.PropertyField(source);
                custom |= source.objectReferenceValue == null || source.hasMultipleDifferentValues;
            }
            if (custom || mixed || type.enumValueIndex == (int)ColliderType.Sprite)
            {
                var shape = serializedObject.FindProperty("colliderShape");
                EditorGUILayout.PropertyField(shape);
                if (custom)
                {
                    EditorGUILayout.PropertyField(serializedObject.FindProperty("colliderCenter"));
                    if (shape.hasMultipleDifferentValues || shape.enumValueIndex == (int)WorldInteractionColliderShape.Box)
                        EditorGUILayout.PropertyField(serializedObject.FindProperty("localSize"));
                    if (shape.hasMultipleDifferentValues || shape.enumValueIndex == (int)WorldInteractionColliderShape.Circle)
                        EditorGUILayout.PropertyField(serializedObject.FindProperty("colliderRadius"));
                }
            }
        }

        [DrawGizmo(GizmoType.Selected | GizmoType.Active)]
        private static void DrawCollider(WorldInteraction item, GizmoType gizmoType)
        {
            if (item is HeightMapObstacleFootprint || (item.Kind & WorldInteractionKind.Climbable) != 0) return;
            if (item.ColliderType == ColliderType.Sprite && item.SpriteSource == null) return;
            if (item.ColliderType == ColliderType.Collider2D && item.BoxSource == null) return;
            MapTestSceneController map = null;
            foreach (var candidate in Object.FindObjectsByType<MapTestSceneController>(FindObjectsSortMode.None))
                if (candidate.gameObject.scene == item.gameObject.scene) { map = candidate; break; }
            InteractionShape shape = item.GetShape(map);
            float z = item.ColliderType == ColliderType.Sprite ? item.SpriteSource.transform.position.z
                : item.ColliderType == ColliderType.Collider2D ? item.BoxSource.transform.position.z : item.transform.position.z;
            Vector3 ToWorld(Vector2 point)
            {
                Vector3 world = map != null && map.HasGeneratedMap ? map.MapPositionToWorld(point) : (Vector3)point;
                world.z = z;
                return world;
            }
            Color previous = Handles.color;
            Handles.color = Color.green;
            if (shape.IsBox)
            {
                for (int i = 0; i < 4; i++)
                    Handles.DrawLine(ToWorld(shape.Vertex(i)), ToWorld(shape.Vertex((i + 1) % 4)));
            }
            else
            {
                Vector3 last = ToWorld(shape.Center + Vector2.right * shape.Radius);
                for (int i = 1; i <= 64; i++)
                {
                    float angle = i * Mathf.PI * 2f / 64f;
                    Vector3 next = ToWorld(shape.Center + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * shape.Radius);
                    Handles.DrawLine(last, next);
                    last = next;
                }
            }
            Handles.color = previous;
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
