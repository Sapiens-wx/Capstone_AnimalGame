using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace AnimalGame.Editor
{
    internal static class OfficialMapWaterPreview
    {
        private const string ShaderPath = "Assets/Maps/Official_Map_01/Hydrology/Editor/OfficialWaterPreview.shader";
        private const string VisibilityKey = "AnimalGame.OfficialMap01.WaterPreview";
        private static readonly int ColorId = Shader.PropertyToID("_Color");
        private static readonly Dictionary<Terrain, Mesh> Meshes = new Dictionary<Terrain, Mesh>();
        private static readonly HashSet<Terrain> Dirty = new HashSet<Terrain>();
        private static Material fill;
        private static Material shore;

        static OfficialMapWaterPreview()
        {
            AssemblyReloadEvents.beforeAssemblyReload += Release;
            EditorSceneManager.activeSceneChangedInEditMode += (_, _) => ClearMeshes();
            Undo.undoRedoPerformed += UndoRedo;
            TerrainCallbacks.heightmapChanged += (terrain, _, _) =>
            {
                if (IsAuthoringScene(terrain.gameObject.scene)) Dirty.Add(terrain);
            };
        }

        internal static bool Visible
        {
            get => SessionState.GetBool(VisibilityKey, true);
            set
            {
                if (Visible == value) return;
                SessionState.SetBool(VisibilityKey, value);
                SceneView.RepaintAll();
            }
        }

        internal static bool IsAuthoringScene(Scene scene) => scene.path == OfficialPlainsGameplay.AuthoringScenePath;

        internal static void Draw(Scene scene)
        {
            if (!IsAuthoringScene(scene) || !Visible || EditorApplication.isPlayingOrWillChangePlaymode) return;
            OfficialMapWaterLayout layout = OfficialMapWaterLayout.Load();
            if (layout == null) return;
            GetMaterials(out Material waterFill, out Material shoreline);
            foreach (Terrain terrain in OfficialMapWaterTerrain.Tiles)
            {
                if (!terrain.isActiveAndEnabled) continue;
                bool changed = Dirty.Remove(terrain);
                if (!Meshes.TryGetValue(terrain, out Mesh mesh) || changed)
                {
                    UnityEngine.Object.DestroyImmediate(mesh);
                    mesh = OfficialMapWaterMesh.Build(terrain, layout);
                    Meshes[terrain] = mesh;
                }
                if (mesh == null) continue;
                if (waterFill.SetPass(0)) Graphics.DrawMeshNow(mesh, Matrix4x4.identity, 0);
                if (shoreline.SetPass(0)) Graphics.DrawMeshNow(mesh, Matrix4x4.identity, 1);
            }
        }

        internal static void MarkChanged(int x, int z)
        {
            int size = OfficialMapWaterTerrain.TileSize;
            Vector2Int origin = OfficialMapWaterTerrain.TileOriginAt(x, z);
            MarkTile(origin);
            bool onWest = x == origin.x;
            bool onSouth = z == origin.y;
            if (onWest) MarkTile(origin + new Vector2Int(-size, 0));
            if (onSouth) MarkTile(origin + new Vector2Int(0, -size));
            if (onWest && onSouth) MarkTile(origin - Vector2Int.one * size);
        }

        internal static void Export()
        {
            string parent = EditorUtility.OpenFolderPanel("选择水域导出位置", Path.GetFullPath("output"), "");
            if (string.IsNullOrEmpty(parent)) return;
            string destination = Path.Combine(parent, "Official_Map_01_Water_" + DateTime.Now.ToString("yyyyMMdd_HHmmss_fff"));
            try
            {
                OfficialMapWaterExport.Write(destination, OfficialMapWaterLayout.Load());
                EditorUtility.RevealInFinder(destination);
            }
            catch (IOException error)
            {
                Debug.LogException(error);
                EditorUtility.DisplayDialog("水域导出失败", error.Message, "确定");
            }
        }

        internal static void GetMaterials(out Material waterFill, out Material shoreline)
        {
            if (fill == null)
            {
                var shader = AssetDatabase.LoadAssetAtPath<Shader>(ShaderPath);
                fill = MakeMaterial(shader, "Water preview fill", new Color(.08f, .56f, .80f, .70f));
                shore = MakeMaterial(shader, "Water preview shore", new Color(.40f, .83f, .95f, .85f));
                shore.renderQueue = fill.renderQueue + 1;
            }
            waterFill = fill;
            shoreline = shore;
        }

        private static Material MakeMaterial(Shader shader, string name, Color color)
        {
            var material = new Material(shader) { name = name, hideFlags = HideFlags.HideAndDontSave };
            material.SetColor(ColorId, color);
            return material;
        }

        private static void MarkTile(Vector2Int origin)
        {
            if (OfficialMapWaterTerrain.TryTile(origin, out Terrain terrain)) Dirty.Add(terrain);
        }

        private static void UndoRedo()
        {
            if (!IsAuthoringScene(SceneManager.GetActiveScene())) return;
            OfficialMapWaterLayout layout = OfficialMapWaterLayout.Load();
            if (layout == null) return;
            layout.Reload();
            AssetDatabase.SaveAssetIfDirty(layout);
            ClearMeshes();
            SceneView.RepaintAll();
        }

        private static void ClearMeshes()
        {
            foreach (Mesh mesh in Meshes.Values) UnityEngine.Object.DestroyImmediate(mesh);
            Meshes.Clear();
            Dirty.Clear();
        }

        private static void Release()
        {
            ClearMeshes();
            UnityEngine.Object.DestroyImmediate(fill);
            UnityEngine.Object.DestroyImmediate(shore);
        }
    }
}
