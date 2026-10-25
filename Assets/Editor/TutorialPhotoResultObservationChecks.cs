using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using AnimalGame.Animals;
using AnimalGame.RobotMap;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;
using Object = UnityEngine.Object;

namespace AnimalGame.Editor
{
    /// <summary>Checks native result identity and album notifications without modifying scene or library assets.</summary>
    public static class TutorialPhotoResultObservationChecks
    {
        private const BindingFlags Fields = BindingFlags.Instance | BindingFlags.NonPublic;
        private static int checks;

        [MenuItem("Animal Game/Validation/Run Tutorial Photo Result Observation Checks")]
        public static void Run()
        {
            checks = 0;
            Require(!EditorApplication.isPlayingOrWillChangePlaymode, "Result observation checks run in Edit Mode.");
            Scene scene = EditorSceneManager.NewPreviewScene();
            GameObject root = null;
            var album = (List<SavedAnimalPhotoRecord>)typeof(PhotoAlbumService)
                .GetField("SavedPhotos", BindingFlags.Static | BindingFlags.NonPublic).GetValue(null);
            int originalAlbumCount = album.Count;
            try
            {
                root = new GameObject("Photo result observation fixture");
                root.SetActive(false);
                SceneManager.MoveGameObjectToScene(root, scene);
                PhotoResultUI ui = root.AddComponent<PhotoResultUI>();
                PhotoResultView view = root.AddComponent<PhotoResultView>();
                Set(view, "saveLabel", new Label());
                Set(ui, "resultView", view);
                AnimalPhotoSubject first = Subject(root.transform, "Teaching woodpecker");
                AnimalPhotoSubject second = Subject(root.transform, "Other woodpecker");
                PhotoResultSnapshot firstResult = Snapshot("Teaching woodpecker");
                PhotoResultSnapshot secondResult = Snapshot("Other woodpecker");
                int shown = 0, saved = 0;
                AnimalPhotoSubject notifiedSubject = null;
                PhotoResultSnapshot notifiedResult = null;
                ui.ResultShown += (subject, result) => shown++;
                ui.PhotoSaved += (subject, result) =>
                {
                    saved++;
                    notifiedSubject = subject;
                    notifiedResult = result;
                    Require(ui.HasSavedDisplayedResult, "Save observers see the committed saved state.");
                    Require(album.Count == originalAlbumCount + saved, "Save notification follows the real album append.");
                    Require(ReferenceEquals(album[album.Count - 1].Snapshot, result), "Save notification carries the exact stored snapshot.");
                };

                Require(!ui.IsVisible && !ui.IsResultVisible && ui.DisplayedResult == null
                    && ui.DisplayedSubject == null && !ui.HasSavedDisplayedResult,
                    "A fresh presenter has no displayed result or subject.");
                Require(!ui.IsSubjectInCaptureFrame(null) && !ui.IsSubjectInCaptureFrame(first),
                    "Missing targets or a missing native camera frame cannot satisfy framing.");
                Call(ui, "SaveDisplayedResult");
                Require(saved == 0 && album.Count == originalAlbumCount, "No displayed photograph cannot create a save or notification.");

                Set(ui, "displayedResult", firstResult);
                Set(ui, "displayedSubject", first);
                Set(ui, "visible", true);
                Require(ui.IsVisible && ui.IsResultVisible && ReferenceEquals(ui.DisplayedSubject, first)
                    && ReferenceEquals(ui.DisplayedResult, firstResult), "Observation preserves the selected instance and immutable snapshot.");
                Call(ui, "SaveDisplayedResult");
                Require(saved == 1 && ReferenceEquals(notifiedSubject, first)
                    && ReferenceEquals(notifiedResult, firstResult), "Saving the teaching subject reports that exact subject, not only its species.");
                Call(ui, "SaveDisplayedResult");
                Require(saved == 1 && album.Count == originalAlbumCount + 1, "Repeated save input neither appends nor notifies twice.");

                Call(ui, "HideResult", false);
                Require(!ui.IsResultVisible && ui.DisplayedSubject == null && ui.DisplayedResult == null
                    && !ui.HasSavedDisplayedResult, "Closing clears displayed identity and saved observation together.");
                Set(ui, "displayedResult", secondResult);
                Set(ui, "displayedSubject", second);
                Set(ui, "visible", true);
                Call(ui, "SaveDisplayedResult");
                Require(saved == 2 && ReferenceEquals(notifiedSubject, second)
                    && !ReferenceEquals(notifiedSubject, first), "Another animal of the same species retains a distinct instance on the next save.");

                Set(ui, "pendingResult", firstResult);
                Set(ui, "pendingSubject", first);
                ui.Initialize(null, null, null, null);
                Require(Get(ui, "pendingResult") == null && Get(ui, "pendingSubject") == null
                    && ui.DisplayedSubject == null && ui.DisplayedResult == null,
                    "Reinitialization removes stale pending and displayed identity.");

                Set(ui, "pendingResult", firstResult);
                Set(ui, "pendingSubject", first);
                Set(ui, "resultView", null);
                Call(ui, "ShowPendingResult");
                Require(shown == 0 && Get(ui, "pendingResult") == null && Get(ui, "pendingSubject") == null
                    && !ui.IsResultVisible, "A missing result view cannot emit ResultShown or retain an orphaned subject.");
                Set(ui, "resultView", view);
                Set(ui, "pendingResult", firstResult);
                Set(ui, "pendingSubject", first);
                Call(ui, "OnDisable");
                Require(Get(ui, "pendingSubject") == null && ui.DisplayedSubject == null
                    && !ui.HasSavedDisplayedResult, "Disabling the presenter clears all result identity state.");
            }
            finally
            {
                if (album.Count > originalAlbumCount)
                    album.RemoveRange(originalAlbumCount, album.Count - originalAlbumCount);
                if (root != null) Object.DestroyImmediate(root);
                EditorSceneManager.ClosePreviewScene(scene);
            }
            Require(album.Count == originalAlbumCount, "Checks restore the original in-memory album.");
            string output = Path.GetFullPath("output/validation/tutorial-photo");
            Directory.CreateDirectory(output);
            File.WriteAllText(Path.Combine(output, "photo-result-observation.txt"),
                "PASS: " + checks + " result identity and saved-photo observation assertions.");
            Debug.Log("TUTORIAL PHOTO RESULT OBSERVATION CHECKS PASSED: " + checks + " assertions.");
        }

        public static void RunBatch()
        {
            try { Run(); EditorApplication.Exit(0); }
            catch (Exception error) { Debug.LogException(error); EditorApplication.Exit(1); }
        }

        private static AnimalPhotoSubject Subject(Transform parent, string name)
        {
            var item = new GameObject(name);
            item.SetActive(false);
            item.transform.SetParent(parent, false);
            return item.AddComponent<AnimalPhotoSubject>();
        }

        private static PhotoResultSnapshot Snapshot(string name) => new PhotoResultSnapshot(
            "pileated_woodpecker", name, name, "Dryocopus pileatus", "Tutorial", Color.yellow,
            1, 2, 3, null, Vector2.zero, 0f, 1f, new DateTime(2026, 1, 1),
            AnimalSpecies.PileatedWoodpecker, AnimalState.Daily, true, true);

        private static void Set(object target, string name, object value) =>
            target.GetType().GetField(name, Fields).SetValue(target, value);
        private static object Get(object target, string name) =>
            target.GetType().GetField(name, Fields).GetValue(target);
        private static void Call(object target, string name, params object[] args) =>
            target.GetType().GetMethod(name, Fields).Invoke(target, args);
        private static void Require(bool condition, string message)
        {
            checks++;
            if (!condition) throw new InvalidOperationException(message);
        }
    }
}
