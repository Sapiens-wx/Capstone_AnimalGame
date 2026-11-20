using System;
using AnimalGame.MapTest;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace AnimalGame.Editor
{
    /// <summary>Explicitly captures the grassland surface as the reference group's vertical origin.</summary>
    public static class OfficialMapPlanHeightAlignment
    {
        [MenuItem("Animal Game/Official Map 01/Align Height References To Plains")]
        private static void AlignMenu() => AlignToPlains(OfficialMap01PlanBuilder.FindPlan());

        /// <summary>Records one height sample; later terrain edits never change the ruler automatically.</summary>
        public static void AlignToPlains(OfficialMapPlan plan)
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                throw new InvalidOperationException("Exit Play mode before aligning the height references.");
            if (!TrySamplePlainsGround(plan, out float heightMeters, out string issue))
                throw new InvalidOperationException(issue);

            Undo.RecordObject(plan, "Align height references to plains");
            var serializedPlan = new SerializedObject(plan);
            serializedPlan.FindProperty("referenceBaseHeightMeters").floatValue = heightMeters;
            serializedPlan.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(plan);
            EditorSceneManager.MarkSceneDirty(plan.gameObject.scene);
            SceneView.RepaintAll();
        }

        /// <summary>Samples below the main ruler, falling back to the grassland label if the ruler lies outside the terrain.</summary>
        public static bool TrySamplePlainsGround(OfficialMapPlan plan, out float heightMeters, out string issue)
        {
            heightMeters = 0f;
            if (plan == null)
                return Fail("The official map planning component is missing.", out issue);
            Scene scene = plan.gameObject.scene;
            if (!scene.IsValid() || !scene.isLoaded || scene.path != OfficialMap01PlanBuilder.ScenePath)
                return Fail("Height references can only align inside Assets/Scenes/GameMaps/Official_Map_01.unity.", out issue);
            if (!plan.HasMetricTransform(out issue))
                return false;
            if (!HasMetricTransform(plan.transform))
                return Fail("The plan hierarchy needs finite metric transforms before sampling its terrain.", out issue);

            OfficialMapRegion plains = plan.FindRegion("plains");
            if (plains == null || !IsFinite(plains.LabelPositionMeters.x) || !IsFinite(plains.LabelPositionMeters.y))
                return Fail("The plains region needs a finite label position for its height sample.", out issue);
            Vector3 fallbackSample = plan.CoordinateToWorld(plains.LabelPositionMeters);

            GameObject terrainRoot = null;
            foreach (GameObject root in scene.GetRootGameObjects())
            {
                if (root.name != OfficialPlainsTerrainBuilder.RootName)
                    continue;
                if (terrainRoot != null)
                    return Fail("Multiple official plains terrain roots were found; resolve the duplicate before aligning references.", out issue);
                terrainRoot = root;
            }
            if (terrainRoot == null)
                return Fail("The editable plains terrain is missing. Create or restore it before aligning the references.", out issue);

            Terrain[] terrains = terrainRoot.GetComponentsInChildren<Terrain>(true);
            for (int pass = 0; pass < 2; pass++)
            {
                Vector3 sampleWorld = pass == 0 && IsFinite(plan.ReferenceOriginMeters.x) && IsFinite(plan.ReferenceOriginMeters.y)
                    ? plan.CoordinateToWorld(plan.ReferenceOriginMeters) : fallbackSample;
                foreach (Terrain terrain in terrains)
                {
                    TerrainData data = terrain.terrainData;
                    if (terrain.gameObject.scene != scene || data == null)
                        continue;
                    Vector3 position = terrain.transform.position;
                    Vector3 size = data.size;
                    if (!IsFinite(position) || !IsFinite(size) || size.x <= 0f || size.y <= 0f || size.z <= 0f)
                        return Fail($"{terrain.name} has invalid terrain dimensions or position.", out issue);

                    const float edgeTolerance = 0.001f;
                    float localX = sampleWorld.x - position.x;
                    float localZ = sampleWorld.z - position.z;
                    if (localX < -edgeTolerance || localX > size.x + edgeTolerance
                        || localZ < -edgeTolerance || localZ > size.z + edgeTolerance)
                        continue;
                    if (!HasMetricTransform(terrain.transform))
                        return Fail($"{terrain.name} and its parents need rotation (0, 0, 0) and scale (1, 1, 1) to sample in metres.", out issue);

                    float localHeight = data.GetInterpolatedHeight(Mathf.Clamp01(localX / size.x), Mathf.Clamp01(localZ / size.z));
                    heightMeters = position.y + localHeight - plan.transform.position.y;
                    if (!IsFinite(heightMeters))
                        return Fail("The grassland terrain returned a non-finite ground height.", out issue);
                    issue = string.Empty;
                    return true;
                }
            }

            return Fail("No editable plains Terrain covers the plains label position. Move the label inside the terrain or restore the covering tile.", out issue);
        }

        private static bool HasMetricTransform(Transform transform)
        {
            for (Transform current = transform; current != null; current = current.parent)
            {
                Vector3 scale = current.localScale;
                Quaternion rotation = current.localRotation;
                if (!IsFinite(scale) || !IsFinite(current.localPosition)
                    || !IsFinite(rotation.x) || !IsFinite(rotation.y) || !IsFinite(rotation.z) || !IsFinite(rotation.w)
                    || Mathf.Abs(scale.x - 1f) > 0.0001f || Mathf.Abs(scale.y - 1f) > 0.0001f
                    || Mathf.Abs(scale.z - 1f) > 0.0001f
                    || Quaternion.Angle(rotation, Quaternion.identity) > 0.01f)
                    return false;
            }
            return true;
        }

        private static bool IsFinite(Vector3 value) => IsFinite(value.x) && IsFinite(value.y) && IsFinite(value.z);
        private static bool IsFinite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);

        private static bool Fail(string message, out string issue)
        {
            issue = message;
            return false;
        }
    }
}
