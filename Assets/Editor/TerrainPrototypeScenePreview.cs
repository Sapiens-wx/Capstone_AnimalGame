using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;

namespace AnimalGame.Editor
{
    /// <summary>
    /// URP 17 forces Scene View to use the default renderer, ignoring camera overrides.
    /// Use a disposable pipeline copy while editing this terrain scene, then restore
    /// the project's original quality override before saving, playing or switching scenes.
    /// The persisted project pipeline continues to default to its 2D renderer.
    /// </summary>
    [InitializeOnLoad]
    internal static class TerrainPrototypeScenePreview
    {
        private static UniversalRenderPipelineAsset preview;
        private static RenderPipelineAsset previousOverride;

        static TerrainPrototypeScenePreview()
        {
            EditorApplication.update += Update;
            AssemblyReloadEvents.beforeAssemblyReload += Restore;
            EditorApplication.quitting += Restore;
            EditorApplication.playModeStateChanged += OnPlayModeChanged;
            EditorSceneManager.activeSceneChangedInEditMode += OnSceneChanged;
            EditorSceneManager.sceneSaving += OnSceneSaving;
        }

        private static void OnPlayModeChanged(PlayModeStateChange state)
        {
            if (state == PlayModeStateChange.ExitingEditMode) Restore();
        }

        private static void OnSceneChanged(Scene previous, Scene next) => Restore();
        private static void OnSceneSaving(Scene scene, string path) => Restore();

        private static void Update()
        {
            // Preview belongs to the authoring scene, regardless of whether its
            // original prototype terrain has been renamed, replaced or deleted.
            bool shouldPreview = !EditorApplication.isPlayingOrWillChangePlaymode
                && !EditorApplication.isCompiling && !EditorApplication.isUpdating
                && SceneManager.GetActiveScene().path == TerrainPrototypeBuilder.ScenePath;
            if (!shouldPreview)
            {
                Restore();
                return;
            }
            if (preview != null)
            {
                if (QualitySettings.renderPipeline == preview) return;

                // Reimporting QualitySettings (or changing quality) can replace the
                // override while this temporary instance is still alive. Rebuild
                // from the newly active pipeline and remember its current override.
                Restore();
            }
            var source = GraphicsSettings.currentRenderPipeline as UniversalRenderPipelineAsset;
            if (source == null) return;
            var sourceData = new SerializedObject(source);
            var renderers = sourceData.FindProperty("m_RendererDataList");
            int index = -1;
            for (int i = 0; i < renderers.arraySize; i++)
                if (AssetDatabase.GetAssetPath(renderers.GetArrayElementAtIndex(i).objectReferenceValue)
                    == "Assets/Settings/TerrainPrototypeRenderer.asset") index = i;
            if (index < 0) return;

            previousOverride = QualitySettings.renderPipeline;
            preview = Object.Instantiate(source);
            preview.name = "Terrain Scene Preview (temporary)";
            preview.hideFlags = HideFlags.HideAndDontSave;
            var settings = new SerializedObject(preview);
            settings.FindProperty("m_DefaultRendererIndex").intValue = index;
            settings.ApplyModifiedPropertiesWithoutUndo();
            preview.shadowDistance = 180f;
            QualitySettings.renderPipeline = preview;
            SceneView.RepaintAll();
        }

        internal static void Restore()
        {
            if (preview == null) return;
            if (QualitySettings.renderPipeline == preview)
                QualitySettings.renderPipeline = previousOverride;
            Object.DestroyImmediate(preview);
            preview = null;
            previousOverride = null;
        }
    }

    internal sealed class TerrainPrototypePreviewSaveGuard : AssetModificationProcessor
    {
        private static string[] OnWillSaveAssets(string[] paths)
        {
            TerrainPrototypeScenePreview.Restore();
            return paths;
        }
    }
}
