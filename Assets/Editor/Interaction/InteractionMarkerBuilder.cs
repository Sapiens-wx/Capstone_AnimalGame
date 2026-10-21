using System;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

namespace AnimalGame.Interaction.Editor
{
    /// <summary>Explicit creation of shared geometry and an opt-in, reusable world-space marker.</summary>
    public static class InteractionMarkerBuilder
    {
        public const string PrefabPath = "Assets/Prefabs/Interaction/InteractionMarker.prefab";
        public const string MaterialPath = "Assets/Materials/InteractionMarker.mat";
        public const string MeshDirectory = "Assets/Arts/UI/InteractionMarker";
        private const int StepsPerArc = 24;

        [MenuItem("Animal Game/Interaction/Create Shared Marker Assets")]
        public static void Build() => EnsureAssets();

        public static GameObject EnsureAssets()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                throw new InvalidOperationException("Exit Play Mode before creating interaction marker assets.");
            EnsureFolder("Assets/Prefabs/Interaction");
            EnsureFolder("Assets/Materials");
            EnsureFolder(MeshDirectory);
            Mesh outer = EnsureMesh(MeshDirectory + "/OuterRing.asset", "Interaction Outer Ring", .5f, .45f, 15f);
            Mesh inner = EnsureMesh(MeshDirectory + "/InnerRing.asset", "Interaction Inner Ring", .3f, .25f, -30f);
            Material material = AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);
            if (material == null)
            {
                Shader shader = AssetDatabase.LoadAssetAtPath<Shader>("Assets/Shaders/InteractionMarker.shader");
                if (shader == null) throw new InvalidOperationException("Interaction marker shader is missing.");
                material = new Material(shader) { name = "Interaction Marker" };
                material.SetColor("_Color", new Color(212f / 255f, 184f / 255f, 84f / 255f, 1f));
                AssetDatabase.CreateAsset(material, MaterialPath);
            }
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
            if (prefab == null)
            {
                var root = new GameObject("Interaction Marker");
                try
                {
                    MeshRenderer outerRenderer = AddRing(root.transform, "Outer Ring", outer, material);
                    MeshRenderer innerRenderer = AddRing(root.transform, "Inner Ring", inner, material);
                    root.AddComponent<InteractionMarker>().Configure(outerRenderer, innerRenderer);
                    prefab = PrefabUtility.SaveAsPrefabAsset(root, PrefabPath, out bool saved);
                    if (!saved) throw new InvalidOperationException("Unable to save the reusable interaction marker.");
                }
                finally { Object.DestroyImmediate(root); }
            }
            if (prefab.GetComponent<InteractionMarker>() == null)
                throw new InvalidOperationException("The existing marker prefab has no InteractionMarker component.");
            InteractionMarker marker = prefab.GetComponent<InteractionMarker>();
            if (!marker.OuterRing.enabled || !marker.InnerRing.enabled)
            {
                // Keep serialized defaults ready; runtime owner state is never persisted into the base prefab.
                GameObject editable = PrefabUtility.LoadPrefabContents(PrefabPath);
                try
                {
                    editable.GetComponent<InteractionMarker>().SetSuppressed(false);
                    PrefabUtility.SaveAsPrefabAsset(editable, PrefabPath);
                }
                finally { PrefabUtility.UnloadPrefabContents(editable); }
                prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
            }
            AssetDatabase.SaveAssetIfDirty(outer);
            AssetDatabase.SaveAssetIfDirty(inner);
            AssetDatabase.SaveAssetIfDirty(material);
            return prefab;
        }

        private static MeshRenderer AddRing(Transform parent, string name, Mesh mesh, Material material)
        {
            var child = new GameObject(name);
            child.transform.SetParent(parent, false);
            child.AddComponent<MeshFilter>().sharedMesh = mesh;
            MeshRenderer renderer = child.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = material;
            renderer.sortingOrder = 25;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            renderer.lightProbeUsage = LightProbeUsage.Off;
            renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
            renderer.motionVectorGenerationMode = MotionVectorGenerationMode.ForceNoMotion;
            return renderer;
        }

        private static Mesh EnsureMesh(string path, string name, float radius, float inset, float startDegrees)
        {
            Mesh existing = AssetDatabase.LoadAssetAtPath<Mesh>(path);
            if (existing != null) return existing;
            int verticesPerArc = (StepsPerArc + 1) * 2;
            var vertices = new Vector3[verticesPerArc * 4];
            var colors = new Color[vertices.Length];
            var triangles = new int[StepsPerArc * 6 * 4];
            for (int arc = 0; arc < 4; arc++)
            {
                int start = arc * verticesPerArc;
                for (int step = 0; step <= StepsPerArc; step++)
                {
                    float radians = (startDegrees + arc * 90f + step * (60f / StepsPerArc)) * Mathf.Deg2Rad;
                    var direction = new Vector3(Mathf.Cos(radians), Mathf.Sin(radians), 0f);
                    vertices[start + step * 2] = direction * radius;
                    vertices[start + step * 2 + 1] = direction * inset;
                    colors[start + step * 2] = colors[start + step * 2 + 1] = Color.white;
                    if (step == StepsPerArc) continue;
                    int triangle = (arc * StepsPerArc + step) * 6;
                    int vertex = start + step * 2;
                    triangles[triangle] = vertex;
                    triangles[triangle + 1] = vertex + 1;
                    triangles[triangle + 2] = vertex + 2;
                    triangles[triangle + 3] = vertex + 2;
                    triangles[triangle + 4] = vertex + 1;
                    triangles[triangle + 5] = vertex + 3;
                }
            }
            var mesh = new Mesh { name = name, vertices = vertices, colors = colors, triangles = triangles };
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            AssetDatabase.CreateAsset(mesh, path);
            return mesh;
        }

        private static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            int separator = path.LastIndexOf('/');
            string parent = path.Substring(0, separator);
            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, path.Substring(separator + 1));
        }
    }
}
