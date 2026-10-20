using System;
using System.Collections.Generic;
using System.Linq;
using AnimalGame.World;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace AnimalGame.Demos.WasteRobot.Editor
{
    /// <summary>Explicit Edit Mode checks; temporal rendering is verified by the separate Play Mode smoke runner.</summary>
    public static class WasteRobotTransitionValidation
    {
        [MenuItem("Animal Game/Demos/Waste Robot/Validate Transitions")]
        public static void Run()
        {
            Require(!EditorApplication.isPlayingOrWillChangePlaymode, "Exit Play Mode before transition validation.");
            GameObject asset = AssetDatabase.LoadAssetAtPath<GameObject>(WasteRobotDemoBuilder.PrefabPath);
            Require(asset != null, "Build the Waste Robot prefab first.");
            WasteRobotTransitionEffects[] components = asset.GetComponentsInChildren<WasteRobotTransitionEffects>(true);
            Require(components.Length == 1 && components[0].gameObject == asset && components[0].enabled,
                "The reusable prefab must contain exactly one enabled root transition component.");
            WasteRobotTransitionEffects defaults = components[0];
            Require(defaults.FadeEnabled && defaults.ZoomEnabled, "Fade and interior zoom must default to enabled.");
            Require(Near(defaults.InteriorCameraSize, 6.5f) && Near(defaults.FadeOutDuration, .18f)
                && Near(defaults.BlackHoldDuration, .04f) && Near(defaults.FadeInDuration, .22f)
                && Near(defaults.ZoomDuration, .3f), "Saved transition parameters differ from the approved defaults.");

            Scene previous = SceneManager.GetActiveScene();
            Scene preview = EditorSceneManager.NewPreviewScene();
            try
            {
                var root = (GameObject)PrefabUtility.InstantiatePrefab(asset, preview);
                WasteRobotBase machine = root.GetComponent<WasteRobotBase>();
                WasteRobotTransitionEffects effects = root.GetComponent<WasteRobotTransitionEffects>();
                Require(machine != null && effects != null, "The prefab has no local controller/effect pair.");
                effects.FadeEnabled = false;
                Require(!effects.FadeEnabled && effects.ZoomEnabled, "Disabling fade also disabled zoom.");
                effects.FadeEnabled = true;
                effects.ZoomEnabled = false;
                Require(effects.FadeEnabled && !effects.ZoomEnabled, "Disabling zoom also disabled fade.");

                effects.FadeEnabled = false;
                foreach (bool inside in new[] { true, false, true, false })
                {
                    machine.SetInside(inside);
                    Require(machine.InteriorRoot.activeSelf == inside && machine.ExteriorRoot.activeSelf != inside,
                        "Effects must not delay the authoritative collision/presentation-root switch.");
                    GameObject hidden = inside ? machine.ExteriorRoot : machine.InteriorRoot;
                    Require(hidden.GetComponentsInChildren<WorldInteraction>(true).All(wall => !wall.Available),
                        "A hidden collision root remains available while effects are disabled.");
                }
                machine.Bind(null);
                Require(!effects.IsTransitioning && effects.FadeAlpha <= .0001f,
                    "Rebinding the machine must not start or retain a fade.");
                Require(effects.FadeOverlayObject == null || !effects.FadeOverlayObject.activeInHierarchy,
                    "Edit Mode rebinding retained an active black overlay.");
                Require(effects.OutgoingVisualObject == null || !effects.OutgoingVisualObject.activeInHierarchy,
                    "Edit Mode rebinding retained an outgoing-view proxy.");
                Require(effects.OutgoingVisualObject == null
                    || effects.OutgoingVisualObject.GetComponentsInChildren<WorldInteraction>(true).Length == 0,
                    "Outgoing visual proxies must never contain WorldInteraction collision components.");
                effects.enabled = false;
                Require(effects.FadeAlpha <= .0001f && !effects.IsTransitioning,
                    "Disabling the transition component did not release its fade state.");
                Object.DestroyImmediate(root);
                Debug.Log("Waste Robot transition validation PASS (4 groups): prefab ownership/default parameters; independent feature switches; immediate collision roots; binding/disable cleanup. Temporal fades, black frames and camera composition require TransitionSmoke.");
            }
            finally
            {
                EditorSceneManager.ClosePreviewScene(preview);
                if (previous.IsValid() && previous.isLoaded && SceneManager.GetActiveScene() != previous)
                    SceneManager.SetActiveScene(previous);
                WorldInteractionQuery.InvalidateSpatialIndex();
            }
        }

        private static bool Near(float a, float b) => Mathf.Abs(a - b) < .0001f;
        private static void Require(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException("Waste Robot transition validation: " + message);
        }
    }
}
