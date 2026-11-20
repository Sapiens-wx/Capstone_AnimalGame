using AnimalGame.Animals;
using AnimalGame.MapTest;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace AnimalGame.Editor
{
    [CustomEditor(typeof(PileatedWoodpeckerBehaviour))]
    public sealed class PileatedWoodpeckerBehaviourEditor
        : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();

            var behaviour = (PileatedWoodpeckerBehaviour)target;
            EditorGUILayout.Space();
            EditorGUILayout.LabelField(
                "Scene Tree Binding",
                EditorStyles.boldLabel);

            if (behaviour.BirthTree == null)
            {
                EditorGUILayout.HelpBox(
                    "No birth tree is bound. At runtime the woodpecker will "
                    + "fall back to the nearest valid tree, but binding a "
                    + "scene tree chooses its initial perch. Daily travel uses the whole map.",
                    MessageType.Warning);
            }
            else
            {
                EditorGUILayout.HelpBox(
                    $"Birth tree: {behaviour.BirthTree.name} "
                    + $"({behaviour.BirthTree.HealthState})\n"
                    + "Initial perch only. Flight targets may be anywhere on the map; escape is away from threats.",
                    MessageType.Info);
            }

            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Bind Nearest Tree"))
                    BindNearestTree(behaviour);

                using (new EditorGUI.DisabledScope(
                           behaviour.BirthTree == null))
                {
                    if (GUILayout.Button("Snap To Birth Perch"))
                        SnapToBirthPerch(behaviour);
                }
            }

            using (new EditorGUI.DisabledScope(
                       behaviour.BirthTree == null))
            {
                if (GUILayout.Button("Select Birth Tree"))
                {
                    Selection.activeObject = behaviour.BirthTree.gameObject;
                    EditorGUIUtility.PingObject(
                        behaviour.BirthTree.gameObject);
                }
            }

            EditorGUILayout.Space();
            EditorGUILayout.HelpBox(
                "Scene gizmos\n"
                + "Daily tree selection uses the whole map.\n"
                + "White dotted line: bird-to-birth-tree binding\n"
                + "Cyan: current perch; orange: active flight target",
                MessageType.None);
        }

        private static void BindNearestTree(
            PileatedWoodpeckerBehaviour behaviour)
        {
            Undo.RecordObject(behaviour, "Bind Nearest Woodpecker Tree");
            if (!behaviour.BindNearestTree(false))
            {
                EditorUtility.DisplayDialog(
                    "No Tree Habitat Found",
                    "Add a healthy or dead tree prefab to this scene before "
                    + "binding the woodpecker.",
                    "OK");
                return;
            }

            MarkDirty(behaviour);
        }

        private static void SnapToBirthPerch(
            PileatedWoodpeckerBehaviour behaviour)
        {
            Undo.RecordObject(
                behaviour.transform,
                "Snap Woodpecker To Birth Perch");
            Undo.RecordObject(behaviour, "Snap Woodpecker To Birth Perch");
            if (!behaviour.SnapToBirthPerch())
            {
                EditorUtility.DisplayDialog(
                    "Cannot Find A Valid Perch",
                    "The bound tree and woodpecker must belong to a generated "
                    + "map before a map-space perch can be calculated.",
                    "OK");
                return;
            }

            MarkDirty(behaviour);
            EditorUtility.SetDirty(behaviour.transform);
        }

        private static void MarkDirty(
            PileatedWoodpeckerBehaviour behaviour)
        {
            EditorUtility.SetDirty(behaviour);
            if (behaviour.gameObject.scene.IsValid())
                EditorSceneManager.MarkSceneDirty(behaviour.gameObject.scene);
        }
    }

    public static class PileatedWoodpeckerBehaviourGizmoDrawer
    {
        private static readonly Color BindingColour =
            new Color(0.92f, 0.98f, 1f, 0.82f);
        private static readonly Color CurrentTreeColour =
            new Color(0.2f, 0.88f, 1f, 0.95f);
        private static readonly Color TargetTreeColour =
            new Color(1f, 0.62f, 0.14f, 0.95f);

        [DrawGizmo(GizmoType.Selected | GizmoType.Active)]
        private static void DrawWoodpeckerTreeState(
            PileatedWoodpeckerBehaviour behaviour,
            GizmoType gizmoType)
        {
            if (behaviour == null)
                return;

            TreeHabitat birthTree = behaviour.BirthTree;

            if (birthTree != null)
            {
                Vector3 centre = birthTree.TrunkWorldPosition;
                centre.z = behaviour.transform.position.z;
                Handles.color = BindingColour;
                Handles.DrawDottedLine(
                    behaviour.transform.position,
                    centre,
                    6f);

                Handles.Label(
                    centre,
                    $"  Birth tree / {birthTree.HealthState}",
                    CreateLabelStyle(BindingColour));
            }

            DrawTreeLink(
                behaviour,
                behaviour.CurrentTree,
                CurrentTreeColour,
                "Current perch");
            DrawTreeLink(
                behaviour,
                behaviour.TargetTree,
                TargetTreeColour,
                "Flight target");
        }

        private static void DrawTreeLink(
            PileatedWoodpeckerBehaviour behaviour,
            TreeHabitat tree,
            Color colour,
            string label)
        {
            if (tree == null || tree == behaviour.BirthTree)
                return;

            Vector3 target = tree.TrunkWorldPosition;
            target.z = behaviour.transform.position.z;
            Handles.color = colour;
            Handles.DrawAAPolyLine(
                3f,
                behaviour.transform.position,
                target);
            Handles.Label(target, $"  {label}", CreateLabelStyle(colour));
        }

        private static GUIStyle CreateLabelStyle(Color colour)
        {
            var style = new GUIStyle(EditorStyles.boldLabel)
            {
                fontSize = 11,
                padding = new RectOffset(3, 3, 1, 1)
            };
            style.normal.textColor = colour;
            return style;
        }
    }
}
