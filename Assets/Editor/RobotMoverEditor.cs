using AnimalGame.RobotMap;
using UnityEditor;
using UnityEngine;

namespace AnimalGame.Editor
{
    [CustomEditor(typeof(RobotMover)), CanEditMultipleObjects]
    public sealed class RobotMoverEditor : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            serializedObject.Update();
            var shape = serializedObject.FindProperty("collisionShape");
            EditorGUILayout.PropertyField(shape);
            EditorGUILayout.PropertyField(serializedObject.FindProperty("colliderCenter"));
            if (shape.hasMultipleDifferentValues || shape.enumValueIndex == (int)CollisionShape.Box)
                EditorGUILayout.PropertyField(serializedObject.FindProperty("localSize"));
            if (shape.hasMultipleDifferentValues || shape.enumValueIndex != (int)CollisionShape.Box)
                EditorGUILayout.PropertyField(serializedObject.FindProperty("colliderRadius"));
            if (shape.hasMultipleDifferentValues || shape.enumValueIndex == (int)CollisionShape.Capsule)
                EditorGUILayout.PropertyField(serializedObject.FindProperty("colliderLength"));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("collisionSkin"));
            DrawPropertiesExcluding(serializedObject, "m_Script", "collisionShape", "colliderCenter",
                "localSize", "colliderRadius", "colliderLength", "collisionSkin");
            serializedObject.ApplyModifiedProperties();
        }

        [DrawGizmo(GizmoType.Selected)]
        private static void DrawBody(RobotMover mover, GizmoType type)
        {
            var shape = mover.GetCollisionShape(null);
            Color previous = Handles.color;
            Handles.color = Color.green;
            Vector3 World(Vector2 p) => new Vector3(p.x, p.y, mover.transform.position.z);
            if (shape.IsBox)
                for (int i = 0; i < 4; i++)
                    Handles.DrawLine(World(shape.Vertex(i)), World(shape.Vertex((i + 1) % 4)));
            else
            {
                Vector2 axis = shape.B - shape.A;
                float angle = axis.sqrMagnitude > 0f ? Mathf.Atan2(axis.y, axis.x) : Mathf.PI * .5f;
                Vector2 normal = new Vector2(Mathf.Sin(angle), -Mathf.Cos(angle)) * shape.Radius;
                Handles.DrawLine(World(shape.A + normal), World(shape.B + normal));
                Handles.DrawLine(World(shape.A - normal), World(shape.B - normal));
                for (int cap = 0; cap < 2; cap++)
                {
                    Vector2 center = cap == 0 ? shape.B : shape.A;
                    float start = angle - Mathf.PI * .5f + cap * Mathf.PI;
                    Vector2 last = center + new Vector2(Mathf.Cos(start), Mathf.Sin(start)) * shape.Radius;
                    for (int i = 1; i <= 32; i++)
                    {
                        float theta = start + i * Mathf.PI / 32f;
                        Vector2 next = center + new Vector2(Mathf.Cos(theta), Mathf.Sin(theta)) * shape.Radius;
                        Handles.DrawLine(World(last), World(next));
                        last = next;
                    }
                }
            }
            Handles.color = previous;
        }
    }
}
