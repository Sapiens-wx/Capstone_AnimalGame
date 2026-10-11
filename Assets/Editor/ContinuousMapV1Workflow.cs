using System;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine.SceneManagement;

namespace AnimalGame.Editor
{
    public static class ContinuousMapV1Workflow
    {
        [MenuItem("Animal Game/Terrain Grid V1/Export and Deploy Continuous 500m Map")]
        public static void ExportAndDeploy()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                throw new InvalidOperationException("Exit Play Mode before exporting the continuous map.");
            for (int index = 0; index < SceneManager.sceneCount; index++)
            {
                Scene scene = SceneManager.GetSceneAt(index);
                if (scene.isDirty && scene.path != TerrainGridV1Builder.ScenePath
                    && scene.path != ContinuousMapV1SceneSetup.ScenePath)
                    throw new InvalidOperationException("Save the modified scene before switching to the test: " + scene.path);
            }
            TerrainGridV1Builder.OpenAuthoringScene();
            // SetNeighbors links are editor state, so restore them after loading the source scene.
            TerrainGridV1Builder.Reconnect(false);
            TerrainGridV1CombinedExporter.Export();
            Scene authoring = SceneManager.GetActiveScene();
            if (authoring.isDirty && !EditorSceneManager.SaveScene(authoring))
                throw new InvalidOperationException("Could not save the authored terrain scene.");
            Scene target = SceneManager.GetSceneByPath(ContinuousMapV1SceneSetup.ScenePath);
            if (target.IsValid() && target.isLoaded && target.isDirty
                && !EditorSceneManager.SaveScene(target))
                throw new InvalidOperationException("Could not save the target scene before deployment.");
            EditorSceneManager.OpenScene(ContinuousMapV1SceneSetup.ScenePath, OpenSceneMode.Single);
            ContinuousMapV1SceneSetup.Deploy();
        }
    }
}
