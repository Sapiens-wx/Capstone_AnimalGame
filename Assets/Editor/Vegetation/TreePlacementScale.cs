using AnimalGame.MapTest;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace AnimalGame.Editor
{
    [InitializeOnLoad]
    internal static class TreePlacementScale
    {
        internal const string LeavesGroup = "Canopy";
        internal const string TrunkGroup = "Solid Core";

        private static TreeHabitat[] treesAfterUndo;

        static TreePlacementScale()
        {
            Undo.undoRedoEvent += (in UndoRedoInfo info) =>
            {
                treesAfterUndo = info.isRedo ? null : Object.FindObjectsByType<TreeHabitat>(
                    FindObjectsInactive.Include, FindObjectsSortMode.None);
            };
            ObjectChangeEvents.changesPublished += OnChangesPublished;
        }

        private static void OnChangesPublished(ref ObjectChangeEventStream changes)
        {
            // Deletion undo restores authored scales. A genuine placement can arrive
            // in the same event batch, so only ignore trees already present after undo.
            var restored = treesAfterUndo;
            treesAfterUndo = null;
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                return;

            for (int i = 0; i < changes.length; i++)
            {
                if (changes.GetEventType(i) != ObjectChangeKind.CreateGameObjectHierarchy)
                    continue;

                changes.GetCreateGameObjectHierarchyEvent(i, out var created);
                if (!created.scene.IsValid() || EditorSceneManager.IsPreviewScene(created.scene))
                    continue;

                var root = EditorUtility.InstanceIDToObject(created.instanceId) as GameObject;
                if (root == null)
                    continue;

                foreach (var tree in root.GetComponentsInChildren<TreeHabitat>(true))
                {
                    if (restored != null && System.Array.IndexOf(restored, tree) >= 0)
                        continue;

                    // Redo restores the original instance ID but can restore a snapshot
                    // from before this callback. Reproduce the same two samples on redo.
                    var random = new System.Random(tree.GetInstanceID());
                    RandomizeGroup(tree.transform.Find(LeavesGroup), random, 0.7f, 1.3f);
                    RandomizeGroup(tree.transform.Find(TrunkGroup), random, 0.85f, 1.15f);
                }
            }
        }

        private static void RandomizeGroup(Transform group, System.Random random, float min, float max)
        {
            if (group == null)
                return;

            // Always sample from the prefab so duplicating a tree never compounds its scale.
            var source = PrefabUtility.GetCorrespondingObjectFromSource(group);
            var baseScale = source != null ? source.localScale : Vector3.one;
            group.localScale = baseScale * Mathf.Lerp(min, max, (float)random.NextDouble());
            PrefabUtility.RecordPrefabInstancePropertyModifications(group);
        }
    }
}
