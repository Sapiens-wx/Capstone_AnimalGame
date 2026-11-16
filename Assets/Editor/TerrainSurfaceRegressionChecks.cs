using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using AnimalGame.MapTest;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

public static class TerrainSurfaceRegressionChecks
{
    [MenuItem("Animal Game/Validation/Run Terrain Surface Checks")]
    public static void Run()
    {
        CheckPaintingAndValidation();
        CheckTileBoundaryPainting();
        CheckLegacySerialization();
        CheckDeferredSave();
        CheckPainterTileSelection();
        Debug.Log("Terrain surface checks PASS: painting, validation, legacy preservation, deferred Save, water preservation, Painter tile selection and stroke bounds.");
    }

    public static void RunBatch()
    {
        try { Run(); EditorApplication.Exit(0); }
        catch (Exception error) { Debug.LogException(error); EditorApplication.Exit(1); }
    }

    private static void CheckDeferredSave()
    {
        const string folder = "Assets/__TerrainSurfaceSaveCheck";
        Require(!AssetDatabase.IsValidFolder(folder), "Surface save check folder already exists.");
        HeightMapSurfacePainterWindow window = null;
        try
        {
            AssetDatabase.CreateFolder("Assets", "__TerrainSurfaceSaveCheck");
            var source = ScriptableObject.CreateInstance<HeightMapLevelAsset>();
            Set(source, "surfaceMaskResolution", 64);
            Set(source, "surfaceBakeResolution", 256);
            Set(source, "surfaceClosedContourAlphaEnabled", false);
            Set(source, "surfacePalette", new List<TerrainSurfaceDefinition>
            {
                new(TerrainKind.Rock, "Rock", Texture2D.whiteTexture, Color.white, 1f, 8f)
            });
            // Leave the legacy version unset to verify that normalization stays temporary.
            string path = folder + "/Level.asset";
            AssetDatabase.CreateAsset(source, path);
            AssetDatabase.SaveAssetIfDirty(source);
            string original = File.ReadAllText(path);
            int originalHash = source.CalculateSurfaceAuthoringHash();
            window = ScriptableObject.CreateInstance<HeightMapSurfacePainterWindow>();
            const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
            Type type = typeof(HeightMapSurfacePainterWindow);
            void Call(string name, params object[] args) => type.GetMethod(name, flags).Invoke(window, args);
            Call("SetLevelAsset", source);
            Call("FillType", TerrainKind.Rock);
            var working = (HeightMapLevelAsset)type.GetField("levelAsset", flags).GetValue(window);
            Require((working.hideFlags & HideFlags.NotEditable) == 0,
                "The temporary surface must allow editing its serialized palette and settings.");
            Require(working.SampleSurfaceType(Vector2.zero) == TerrainKind.Rock,
                "Fill did not change the temporary surface.");
            Undo.PerformUndo();
            Require(working.SampleSurfaceType(Vector2.zero) == TerrainKind.Unknown,
                "Undo did not restore the temporary surface.");
            Undo.PerformRedo();
            Require(working.SampleSurfaceType(Vector2.zero) == TerrainKind.Rock,
                "Redo did not restore the temporary fill.");
            Require(source.CalculateSurfaceAuthoringHash() == originalHash
                && File.ReadAllText(path) == original && source.BakedSurfaceVisual == null,
                "Painting or Undo/Redo changed or baked the source before Save.");

            // Surface Save must preserve independently edited water settings.
            Set(source, "maximumStaticWaterDepthMeters", 7f);
            Call("SaveSurface");
            Require(source.BakedSurfaceVisual != null && File.Exists(AssetDatabase.GetAssetPath(source.BakedSurfaceVisual)),
                "Save did not generate the runtime surface texture.");
            Require(source.SampleSurfaceType(Vector2.zero) == TerrainKind.Rock,
                "Save lost the terrain type during legacy normalization.");
            using (var saved = new SerializedObject(source))
                Require(saved.FindProperty("maximumStaticWaterDepthMeters").floatValue == 7f,
                    "Surface Save overwrote water settings.");
            string committed = File.ReadAllText(path);
            Call("FillType", TerrainKind.Unknown);
            Object.DestroyImmediate(window);
            window = null;
            Require(File.ReadAllText(path) == committed && source.SampleSurfaceType(Vector2.zero) == TerrainKind.Rock,
                "Closing Surface Painter committed unsaved edits.");
        }
        finally
        {
            if (window != null)
                Object.DestroyImmediate(window);
            AssetDatabase.DeleteAsset(folder);
        }
    }

    private static void CheckPainterTileSelection()
    {
        const string folder = "Assets/__PainterTileSelectionCheck";
        Require(!AssetDatabase.IsValidFolder(folder), "Painter selection check folder already exists.");
        var root = new GameObject("Painter Tile Selection Check") { hideFlags = HideFlags.HideInHierarchy };
        root.SetActive(false);
        var definition = ScriptableObject.CreateInstance<TiledLevelAsset>();
        EditorWindow window = null;
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
        try
        {
            AssetDatabase.CreateFolder("Assets", "__PainterTileSelectionCheck");
            var height = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            height.SetPixels(Enumerable.Repeat(new Color(0f, 0f, 0f, 1f), 4).ToArray());
            height.Apply();
            AssetDatabase.CreateAsset(height, folder + "/Height.asset");
            HeightMapLevelAsset CreateTile(string name)
            {
                var tile = ScriptableObject.CreateInstance<HeightMapLevelAsset>();
                tile.name = name;
                Set(tile, "heightMap", height);
                Set(tile, "normalizeSourceRange", false);
                Set(tile, "surfaceMaskResolution", 64);
                Set(tile, "surfaceBakeResolution", 256);
                Set(tile, "surfaceClosedContourAlphaEnabled", false);
                Set(tile, "staticWaterResolution", 64);
                Set(tile, "surfacePalette", new List<TerrainSurfaceDefinition>
                {
                    new(TerrainKind.Rock, "Rock", height, Color.white, 1f, 8f)
                });
                tile.EnsureSurfaceAuthoringData();
                tile.SetBakedSurfaceVisual(null);
                AssetDatabase.CreateAsset(tile, folder + "/" + name + ".asset");
                AssetDatabase.SaveAssetIfDirty(tile);
                return tile;
            }
            var left = CreateTile("Left");
            var right = CreateTile("Right");
            Vector2 size = left.MapSizeMeters;
            Set(definition, "tileSizeMeters", size);
            Set(definition, "presentation", left);
            Set(definition, "tiles", new List<TiledLevelAsset.Tile>
            {
                new(Vector2Int.zero, left), new(Vector2Int.right, right)
            });
            Require(definition.Validate(out string mapError), mapError);
            var map = root.AddComponent<MapTestSceneController>();
            Set(map, "levelAsset", definition);
            Set(map, "editorHeightResolution", 16);
            typeof(MapTestSceneController).GetMethod("RebuildGeneratedMap", flags).Invoke(map, null);
            Require(map.HasGeneratedMap, "The Painter selection test map did not generate.");

            foreach (Type type in new[] { typeof(HeightMapSurfacePainterWindow), typeof(HeightMapStaticWaterPainterWindow) })
            {
                window = (EditorWindow)ScriptableObject.CreateInstance(type);
                object Call(string method, params object[] args) => type.GetMethod(method, flags).Invoke(window, args);
                HeightMapLevelAsset Working() => (HeightMapLevelAsset)type.GetField("levelAsset", flags).GetValue(window);
                HeightMapPainterEditState State() => (HeightMapPainterEditState)type.GetField("editState", flags).GetValue(window);
                Call("SetLevelAsset", new object[] { null });
                Require((bool)Call("SelectTileForStroke", left), "The first click without a selection must only select a tile.");
                Require(!(bool)Call("SelectTileForStroke", left), "A click on the selected tile must allow painting.");
                string before = File.ReadAllText(AssetDatabase.GetAssetPath(left));
                DateTime unchangedWriteTime = File.GetLastWriteTimeUtc(AssetDatabase.GetAssetPath(left));
                Require(!State().HasChanges, "A newly selected tile must start clean.");
                Call("SelectTileForStroke", right);
                Call("SelectTileForStroke", left);
                Require(File.GetLastWriteTimeUtc(AssetDatabase.GetAssetPath(left)) == unchangedWriteTime,
                    "Switching an unchanged tile wrote the asset.");
                Vector2 nearEdge = new(size.x - 1f, size.y * 0.5f);
                if (type == typeof(HeightMapSurfacePainterWindow))
                {
                    Call("BeginStroke", nearEdge, Working().GetSurfaceDefinition(TerrainKind.Rock));
                    int painted = Working().CalculateSurfaceAuthoringHash();
                    Call("PaintStrokeTo", new Vector2(size.x + 10f, size.y * 0.5f));
                    Require(Working().CalculateSurfaceAuthoringHash() == painted,
                        "Dragging into another tile continued painting the selected tile.");
                    var copies = (Dictionary<HeightMapLevelAsset, HeightMapLevelAsset>)type.GetField("workingTiles", flags).GetValue(window);
                    Require(copies[right].GetOrCreateSurfaceTypeMap().All(value => value == 0),
                        "A surface brush overlapping the tile edge painted the neighboring tile.");
                }
                else
                {
                    Call("BeginStroke", nearEdge, false);
                    object[] inside = { (Vector2)map.MapPositionToWorld(size * 0.5f), Vector2.zero, 0f };
                    Require((bool)Call("TrySampleTilePosition", inside),
                        "Water painting rejected a position inside the selected tile.");
                    object[] sample = { (Vector2)map.MapPositionToWorld(new Vector2(size.x + 10f, size.y * 0.5f)), Vector2.zero, 0f };
                    Require(!(bool)Call("TrySampleTilePosition", sample),
                        "Water painting accepted a position outside the selected tile.");
                }
                Call("EndStroke");
                Require(State().HasChanges, "A changed stroke must mark the Painter dirty.");
                Undo.PerformUndo();
                Require(!State().HasChanges, "Undo to the initial state must clear the dirty state.");
                Call("SelectTileForStroke", right);
                Require(File.GetLastWriteTimeUtc(AssetDatabase.GetAssetPath(left)) == unchangedWriteTime,
                    "Switching after undo to the initial state saved the asset.");
                Call("SelectTileForStroke", left);
                if (type == typeof(HeightMapSurfacePainterWindow))
                    Call("BeginStroke", nearEdge, Working().GetSurfaceDefinition(TerrainKind.Rock));
                else
                    Call("BeginStroke", nearEdge, false);
                Call("EndStroke");
                Require(File.ReadAllText(AssetDatabase.GetAssetPath(left)) == before,
                    "Painting saved the tile before switching or pressing Save.");
                Require((bool)Call("SelectTileForStroke", right), "A different-tile click must be consumed as selection.");
                Require((HeightMapLevelAsset)type.GetField("sourceLevelAsset", flags).GetValue(window) == right
                    && !(bool)type.GetField("strokeActive", flags).GetValue(window),
                    "The first click on a new tile started painting or failed to select it.");
                Require(File.ReadAllText(AssetDatabase.GetAssetPath(left)) != before,
                    "Switching did not save the previous tile's edits.");
                Require(!(bool)Call("SelectTileForStroke", right), "The next click must allow painting the selected tile.");
                Require(right.SampleSurfaceType(Vector2.zero) == TerrainKind.Unknown
                    && right.SampleStaticWaterDepth(Vector2.zero) == 0f,
                    "Selecting a new tile painted it.");
                Vector2 center = size * 0.5f;
                if (type == typeof(HeightMapSurfacePainterWindow))
                {
                    Call("BeginStroke", center + Vector2.right * size.x, Working().GetSurfaceDefinition(TerrainKind.Rock));
                    Require(Working().SampleSurfaceType(center) == TerrainKind.Rock,
                        "The next click did not paint the selected surface tile.");
                }
                else
                {
                    Call("BeginStroke", center, false);
                    Require(Working().SampleStaticWaterDepth(center) > 0f,
                        "The next click did not paint the selected water tile.");
                }
                Call("EndStroke");
                Call(type == typeof(HeightMapSurfacePainterWindow) ? "SaveSurface" : "SaveWater");
                Require(!State().HasChanges, "A successful Save must establish a clean save point.");
                Undo.PerformUndo();
                Require(State().HasChanges, "Undo away from a saved state must mark the Painter dirty.");
                Undo.PerformRedo();
                Require(!State().HasChanges, "Redo back to the saved state must clear the dirty state.");
                Undo.PerformUndo();
                if (type == typeof(HeightMapSurfacePainterWindow))
                    Call("BeginStroke", center + Vector2.right * size.x, Working().GetSurfaceDefinition(TerrainKind.Rock));
                else
                    Call("BeginStroke", center, false);
                Call("EndStroke");
                Require(State().HasChanges, "A new edit after undo must not reuse the saved revision.");
                Object.DestroyImmediate(window);
                window = null;
            }
        }
        finally
        {
            if (window != null) Object.DestroyImmediate(window);
            Object.DestroyImmediate(root);
            Object.DestroyImmediate(definition);
            AssetDatabase.DeleteAsset(folder);
        }
    }

    private static void CheckPaintingAndValidation()
    {
        var level = ScriptableObject.CreateInstance<HeightMapLevelAsset>();
        try
        {
            var palette = new List<TerrainSurfaceDefinition>();
            foreach (TerrainKind terrain in Enum.GetValues(typeof(TerrainKind)))
            {
                if (terrain != TerrainKind.Unknown)
                    palette.Add(new TerrainSurfaceDefinition(terrain, terrain.ToString(), null, Color.white, 1f, 8f));
            }
            Set(level, "surfacePalette", palette);
            Set(level, "surfaceMaskResolution", 64);
            Set(level, "surfaceDataVersion", 1);
            level.EnsureSurfaceAuthoringData();
            Require(level.TryValidateSurfacePalette(out _), "A unique shared palette must be valid.");
            var serialized = new SerializedObject(level);
            Require(serialized.FindProperty("surfacePalette").GetArrayElementAtIndex(0)
                .FindPropertyRelative("terrain").propertyType == SerializedPropertyType.Enum,
                "The palette must expose an enum selector.");

            foreach (TerrainKind terrain in Enum.GetValues(typeof(TerrainKind)))
            {
                level.FillSurfaceType(terrain);
                Require(level.GetOrCreateSurfaceTypeMap().All(value => value == (byte)terrain),
                    $"Fill failed for {terrain}.");
            }
            level.FillSurfaceType(TerrainKind.Unknown);
            Require(level.PaintSurfaceType(Vector2.zero, 10f, TerrainKind.Rock), "Rock brush did not paint.");
            Require(level.GetOrCreateSurfaceTypeMap()[0] == (byte)TerrainKind.Rock, "Rock brush stored the wrong terrain.");
            Require(level.PaintSurfaceType(Vector2.zero, 10f, TerrainKind.Unknown), "Erase brush did not erase.");
            Require(level.GetOrCreateSurfaceTypeMap()[0] == 0, "Erased terrain must be Unknown.");
            Require(!level.FillSurfaceType((TerrainKind)255), "Undefined terrain was accepted.");

            palette.Add(new TerrainSurfaceDefinition(TerrainKind.Rock, "Duplicate", null, Color.white, 1f, 8f));
            byte[] before = (byte[])level.GetOrCreateSurfaceTypeMap().Clone();
            level.EnsureSurfaceAuthoringData();
            Require(palette[palette.Count - 1].Terrain == TerrainKind.Rock, "Validation silently reassigned a terrain kind.");
            Require(!level.TryValidateSurfacePalette(out _), "Duplicate terrain was accepted.");
            Require(!level.FillSurfaceType(TerrainKind.Grass), "An invalid palette was allowed to paint.");
            Require(before.SequenceEqual(level.GetOrCreateSurfaceTypeMap()), "Invalid painting changed the terrain mask.");
        }
        finally { Object.DestroyImmediate(level); }
    }

    private static void CheckTileBoundaryPainting()
    {
        var leftTile = ScriptableObject.CreateInstance<HeightMapLevelAsset>();
        var rightTile = ScriptableObject.CreateInstance<HeightMapLevelAsset>();
        try
        {
            const TerrainKind terrain = TerrainKind.Rock;
            const float radius = 6f;
            var brush = new TerrainSurfaceDefinition(terrain, "Brush Rock", Texture2D.whiteTexture, Color.red, 0.4f, 7f);
            Set(brush, "transitionMode", TerrainSurfaceTransitionMode.Noisy);
            Set(brush, "transitionWidthMultiplier", 1.5f);
            Set(brush, "noiseStrengthMultiplier", 0.25f);
            var existing = new TerrainSurfaceDefinition(terrain, "Local Rock", Texture2D.whiteTexture, Color.green, 0.8f, 9f);
            Set(leftTile, "surfacePalette", new List<TerrainSurfaceDefinition> { existing });
            Set(leftTile, "surfaceMaskResolution", 64);
            Set(rightTile, "surfaceMaskResolution", 64);

            Vector2 tileSize = leftTile.MapSizeMeters;
            var leftCentre = new Vector2(tileSize.x - 1f, tileSize.y * 0.5f);
            Vector2 rightCentre = leftCentre - Vector2.right * tileSize.x;
            var rightEdge = new Vector2(0f, rightCentre.y);
            int changeCount = 0;
            int emptyHash = rightTile.CalculateSurfaceAuthoringHash();
            Require(!rightTile.PaintSurfaceType(rightCentre - Vector2.right * 10f, radius, terrain, brush, () => changeCount++),
                "A brush that misses the tile reported a change.");
            Require(!rightTile.PaintSurfaceType(rightEdge, radius, TerrainKind.Unknown, null, () => changeCount++),
                "Erasing a blank tile reported a change.");
            Require(changeCount == 0 && rightTile.CalculateSurfaceAuthoringHash() == emptyHash,
                "A no-op brush altered the blank tile's authoring data.");

            Require(leftTile.PaintSurfaceType(leftCentre, radius, terrain, brush, () => changeCount++)
                && rightTile.PaintSurfaceType(rightCentre, radius, terrain, brush, () =>
                {
                    Require(rightTile.CalculateSurfaceAuthoringHash() == emptyHash,
                        "The change callback must run before allocating the mask or copying the palette entry.");
                    changeCount++;
                }), "The same dab did not reach both sides of the tile boundary.");
            Require(leftTile.SampleSurfaceType(leftCentre) == terrain && rightTile.SampleSurfaceType(rightEdge) == terrain,
                "The brush left an unpainted edge at the tile boundary.");
            Require(ReferenceEquals(leftTile.GetSurfaceDefinition(terrain), existing),
                "The brush replaced the destination tile's configured palette entry.");
            TerrainSurfaceDefinition copied = rightTile.GetSurfaceDefinition(terrain);
            Require(copied != null && !ReferenceEquals(copied, brush)
                && copied.AppendConfigurationHash(0) == brush.AppendConfigurationHash(0),
                "The blank destination tile did not copy the complete brush definition.");
            Require(!rightTile.PaintSurfaceType(rightCentre, radius, terrain, brush, () => changeCount++) && changeCount == 2,
                "An already painted dab reported another change.");
        }
        finally
        {
            Object.DestroyImmediate(leftTile);
            Object.DestroyImmediate(rightTile);
        }
    }

    private static void CheckLegacySerialization()
    {
        var source = ScriptableObject.CreateInstance<HeightMapLevelAsset>();
        string path = AssetDatabase.GenerateUniqueAssetPath("Assets/TerrainSurfaceLegacyCheck.asset");
        try
        {
            Require(AssetDatabase.TryGetGUIDAndLocalFileIdentifier(MonoScript.FromScriptableObject(source),
                out string guid, out long scriptFileId), "Could not resolve the map script for the legacy asset.");
            // Use the old field name and byte encoding, as saved by existing maps.
            string mask = string.Concat(Enumerable.Repeat("06030700", 1024));
            File.WriteAllText(path,
                "%YAML 1.1\n%TAG !u! tag:unity3d.com,2011:\n--- !u!114 &11400000\nMonoBehaviour:\n" +
                "  m_ObjectHideFlags: 0\n  m_Script: {fileID: " + scriptFileId + ", guid: " + guid + ", type: 3}\n" +
                "  m_Name: Legacy Terrain Check\n  surfacePalette:\n" +
                "  - terrainId: 6\n    displayName: Legacy Rock\n" +
                "  - terrainId: 3\n    displayName: Legacy Grass\n" +
                "  - terrainId: 7\n    displayName: Legacy Water\n" +
                "  surfaceMaskResolution: 64\n  surfaceDataVersion: 1\n  surfaceMask: " + mask + "\n");
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
            var imported = AssetDatabase.LoadAssetAtPath<HeightMapLevelAsset>(path);
            Require(imported != null, "Legacy map failed to import.");
            Require(imported.SurfacePalette.Select(entry => entry.Terrain).SequenceEqual(
                new[] { TerrainKind.Rock, TerrainKind.Grass, TerrainKind.Water }), "Legacy palette migration changed terrain meanings.");
            byte[] before = (byte[])imported.GetOrCreateSurfaceTypeMap().Clone();
            imported.EnsureSurfaceAuthoringData();
            Require(before.SequenceEqual(imported.GetOrCreateSurfaceTypeMap()), "Legacy painted mask changed during migration.");
            Require(before.Length == 4096 && before[0] == (byte)TerrainKind.Rock
                && before[1] == (byte)TerrainKind.Grass && before[2] == (byte)TerrainKind.Water && before[3] == 0,
                "Legacy mask no longer matches the shared terrain enum.");
        }
        finally
        {
            AssetDatabase.DeleteAsset(path);
            Object.DestroyImmediate(source);
        }
    }

    private static void Set(object target, string field, object value) => target.GetType()
        .GetField(field, BindingFlags.Instance | BindingFlags.NonPublic).SetValue(target, value);

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
