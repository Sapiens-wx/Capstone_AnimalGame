using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using AnimalGame.MapTest;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

public static class HeightMapNavigatorChecks
{
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;

    public static void Run()
    {
        CheckCoordinates();
        CheckVisibilityAndOverview();
        Debug.Log("Navigator checks PASS: viewport and thumbnail coordinates, drag mapping, tile padding, hidden-tile overview capture, state restoration and Play Mode release.");
    }

    private static void CheckCoordinates()
    {
        var go = new GameObject("Navigator Camera Check") { hideFlags = HideFlags.HideAndDontSave };
        try
        {
            Camera camera = go.AddComponent<Camera>();
            camera.enabled = false;
            camera.orthographic = true;
            camera.aspect = 2f;
            camera.orthographicSize = 5f;
            camera.transform.position = new Vector3(20f, 30f, -10f);
            Require(HeightMapNavigatorWindow.TryGetViewRect(camera, out Rect view), "Orthographic view was rejected.");
            Require(Vector2.Distance(view.center, new Vector2(20f, 30f)) < 0.001f
                && Vector2.Distance(view.size, new Vector2(20f, 10f)) < 0.001f,
                "The viewport rectangle does not match the camera.");
            camera.orthographicSize = 10f;
            Require(HeightMapNavigatorWindow.TryGetViewRect(camera, out Rect zoomed)
                && Vector2.Distance(zoomed.size, view.size * 2f) < 0.001f, "Zoom did not update the viewport.");
            camera.orthographic = false;
            Require(!HeightMapNavigatorWindow.TryGetViewRect(camera, out _), "Perspective view must not cull tiles.");
            Rect world = new(0, 0, 100, 50);
            Rect image = new(10, 20, 400, 200);
            Rect marker = HeightMapNavigatorWindow.WorldToImageRect(new Rect(25, 10, 20, 15), world, image);
            Require(Vector2.Distance(marker.position, new Vector2(110, 120)) < 0.001f
                && Vector2.Distance(marker.size, new Vector2(80, 60)) < 0.001f, "Thumbnail marker mapping is incorrect.");
            Vector3 delta = HeightMapNavigatorWindow.DragOffset(new Vector2(40, 20), image, world);
            Require(Vector3.Distance(delta, new Vector3(10, -5, 0)) < 0.001f, "Dragging uses the wrong scale or Y direction.");
            Vector3 pivot = new(35, 17.5f, 8);
            Require(HeightMapNavigatorWindow.DragStartPivot(marker.center, image, world, marker, pivot) == pivot,
                "Clicking inside the view rectangle moved the camera before dragging.");
            Vector3 recentered = HeightMapNavigatorWindow.DragStartPivot(new Vector2(310, 170), image, world, marker, pivot);
            Require(Vector3.Distance(recentered, new Vector3(75, 12.5f, 8)) < 0.001f,
                "Clicking outside the view rectangle did not center on the clicked world position.");
            Require(Vector3.Distance(recentered + delta, new Vector3(85, 7.5f, 8)) < 0.001f,
                "Dragging after recentering did not continue from the new pivot.");
            Require(HeightMapNavigatorWindow.TileIsVisible(new Rect(0, 0, 10, 10), new Vector2(20, 5), new Vector2(10, 10))
                && !HeightMapNavigatorWindow.TileIsVisible(new Rect(0, 0, 10, 10), new Vector2(20.1f, 5), new Vector2(10, 10)),
                "Visibility must pad each side by exactly one tile side length.");
        }
        finally { Object.DestroyImmediate(go); }
    }

    private static void CheckVisibilityAndOverview()
    {
        var root = new GameObject("Navigator Map Check") { hideFlags = HideFlags.HideInHierarchy };
        root.SetActive(false);
        var definition = ScriptableObject.CreateInstance<TiledLevelAsset>();
        var resources = new List<Object> { definition };
        HeightMapNavigatorWindow window = null;
        Texture2D readback = null;
        try
        {
            Texture2D Solid(Color color)
            {
                var texture = new Texture2D(2, 2, TextureFormat.RGBA32, false);
                texture.SetPixels(Enumerable.Repeat(color, 4).ToArray());
                texture.Apply();
                resources.Add(texture);
                return texture;
            }
            Texture2D height = Solid(new Color(0.31f, 0.31f, 0.31f, 1f));
            Color[] colors = { Color.red, Color.green, Color.blue, Color.yellow };
            var entries = new List<TiledLevelAsset.Tile>();
            for (int x = 0; x < colors.Length; x++)
            {
                var level = ScriptableObject.CreateInstance<HeightMapLevelAsset>();
                resources.Add(level);
                Set(level, "heightMap", height);
                Set(level, "mapWidthMeters", 10f);
                Set(level, "mapHeightMeters", 10f);
                Set(level, "normalizeSourceRange", false);
                level.SetBakedSurfaceVisual(Solid(colors[x]));
                entries.Add(new TiledLevelAsset.Tile(new Vector2Int(x, 0), level));
            }
            const int grassTile = 1;
            Texture2D grassPattern = Solid(Color.white);
            var grassLevel = entries[grassTile].level;
            grassLevel.SetBakedGrassland(Solid(Color.clear), grassPattern,
                Enumerable.Repeat(new Vector4(0, 0, 1, 1), 8).ToArray(), 1.6f, Vector2.one);
            Set(grassLevel, "grass1", grassPattern);
            Set(definition, "tileSizeMeters", new Vector2(10, 10));
            Set(definition, "worldUnitsPerMeter", Vector2.one);
            Set(definition, "presentation", entries[0].level);
            Set(definition, "tiles", entries);
            var map = root.AddComponent<MapTestSceneController>();
            Set(map, "levelAsset", definition);
            Set(map, "editorHeightResolution", 16);
            var orphan = new GameObject("Unregistered Previous Tile");
            orphan.transform.SetParent(root.transform, false);
            orphan.SetActive(false);
            typeof(MapTestSceneController).GetMethod("RebuildGeneratedMap", Private).Invoke(map, null);
            Require(map.HasGeneratedMap, "Navigator test map failed to generate.");
            Require(orphan == null && root.transform.childCount == entries.Count,
                "Rebuilding did not delete all previous children before generating the new tiles.");
            SpriteRenderer[] tiles = map.EditorTileRenderers.OrderBy(tile => tile.transform.position.x).ToArray();
            Material sharedMaterial = tiles[0].sharedMaterial;
            var properties = new MaterialPropertyBlock();
            for (int i = 0; i < tiles.Length; i++)
            {
                Require(tiles[i].sharedMaterial == sharedMaterial, "Map tiles must share the controller's material.");
                tiles[i].GetPropertyBlock(properties);
                Require(properties.GetTexture("_SurfaceTex") == entries[i].level.BakedSurfaceVisual,
                    "The property block must retain the tile's own surface texture.");
            }
            tiles[grassTile].GetPropertyBlock(properties);
            Require(properties.GetFloat("_GrassEnabled") == 1f
                && properties.GetTexture("_GrassOverlay1") == grassPattern
                && properties.GetVector("_GrassOverlaySettings").z == 1f,
                "The grass tile must enable grass and select only the assigned overlay.");
            Require(properties.GetTexture("_GrassOverlay2") == null && properties.GetTexture("_GrassOverlay3") == null,
                "Unassigned grass overlays must be left out of the property block.");
            tiles[3].gameObject.SetActive(false);

            window = ScriptableObject.CreateInstance<HeightMapNavigatorWindow>();
            var maps = (List<MapTestSceneController>)Get(window, "maps");
            maps.Add(map);
            var originals = (Dictionary<GameObject, bool>)Get(window, "originalStates");
            foreach (var tile in tiles)
                originals.Add(tile.gameObject, tile.gameObject.activeSelf);
            Set(window, "mapsDirty", false);
            var cameraObject = new GameObject("Navigator Viewport Camera") { hideFlags = HideFlags.HideAndDontSave };
            resources.Add(cameraObject);
            Camera camera = cameraObject.AddComponent<Camera>();
            camera.enabled = false;
            camera.orthographic = true;
            camera.aspect = 1f;
            camera.orthographicSize = 0.5f;
            camera.transform.position = tiles[0].transform.position - Vector3.forward * 10f;
            Rect narrow = new((Vector2)tiles[0].transform.position - Vector2.one * 0.5f, Vector2.one);
            window.UpdateVisibility(camera);
            Require(tiles[0].gameObject.activeSelf && tiles[1].gameObject.activeSelf
                && !tiles[2].gameObject.activeSelf && !tiles[3].gameObject.activeSelf,
                "Visibility updates without a Scene GUI event or overview did not hide distant tiles.");
            camera.transform.position = tiles[3].transform.position - Vector3.forward * 10f;
            window.UpdateVisibility(camera);
            Require(!tiles[0].gameObject.activeSelf && !tiles[1].gameObject.activeSelf
                && tiles[2].gameObject.activeSelf && tiles[3].gameObject.activeSelf,
                "Panning the camera did not update active tiles without a Scene GUI event.");
            camera.transform.position = Vector3.back * 10f;
            camera.orthographicSize = 30f;
            window.UpdateVisibility(camera);
            Require(tiles.All(tile => tile.gameObject.activeSelf), "Zooming out did not reactivate all tiles.");
            camera.transform.position = tiles[0].transform.position - Vector3.forward * 10f;
            camera.orthographicSize = 0.5f;
            window.UpdateVisibility(camera);
            Call(window, "SetDynamicTileActivation", false);
            Require(tiles.All(tile => tile.gameObject.activeSelf), "Disabling dynamic activation did not show all tiles.");
            camera.transform.position = new Vector3(1000, 1000, -10);
            window.UpdateVisibility(camera);
            Require(tiles.All(tile => tile.gameObject.activeSelf), "Camera movement hid tiles while dynamic activation was disabled.");
            Call(window, "SetDynamicTileActivation", true);
            camera.transform.position = tiles[0].transform.position - Vector3.forward * 10f;
            window.UpdateVisibility(camera);
            Require(!tiles[2].gameObject.activeSelf && !tiles[3].gameObject.activeSelf,
                "Re-enabling dynamic activation did not resume culling.");
            Call(window, "CaptureOverview");
            var texture = (RenderTexture)Get(window, "overview");
            Require(texture != null && texture.IsCreated(), "Overview texture was not rendered.");
            RenderTexture previous = RenderTexture.active;
            try
            {
                RenderTexture.active = texture;
                readback = new Texture2D(texture.width, texture.height, TextureFormat.RGBA32, false);
                readback.ReadPixels(new Rect(0, 0, texture.width, texture.height), 0, 0);
                readback.Apply();
            }
            finally { RenderTexture.active = previous; }
            for (int i = 0; i < colors.Length; i++)
            {
                Color pixel = readback.GetPixel(Mathf.RoundToInt((i + 0.5f) * texture.width / colors.Length), texture.height / 2);
                Require(Vector3.Distance(new Vector3(pixel.r, pixel.g, pixel.b),
                    new Vector3(colors[i].r, colors[i].g, colors[i].b)) < 0.35f,
                    $"Tile {i}, including hidden tiles, is missing from the overview: {pixel}.");
            }
            Require(!tiles[2].gameObject.activeSelf && !tiles[3].gameObject.activeSelf,
                "Capturing the overview changed scene tile visibility.");
            Call(window, "ApplyVisibility", new Rect(-100, -100, 200, 200));
            Require(tiles.All(tile => tile.gameObject.activeSelf), "Zooming out did not reactivate all tiles.");
            Call(window, "HandlePlayModeChanged", PlayModeStateChange.ExitingEditMode);
            Require(tiles.Take(3).All(tile => tile.gameObject.activeSelf) && !tiles[3].gameObject.activeSelf,
                "Entering Play Mode did not restore the initial tile states.");
            foreach (var tile in tiles)
                originals.Add(tile.gameObject, tile.gameObject.activeSelf);
            Call(window, "ApplyVisibility", narrow);
            Object.DestroyImmediate(window);
            window = null;
            Require(tiles.Take(3).All(tile => tile.gameObject.activeSelf) && !tiles[3].gameObject.activeSelf,
                "Closing Navigator did not restore the initial tile states.");
            Require(texture == null, "Closing Navigator did not release its overview texture.");

            window = ScriptableObject.CreateInstance<HeightMapNavigatorWindow>();
            root.SetActive(true);
            Call(window, "SynchronizeTiles");
            window.UpdateVisibility(camera);
            typeof(MapTestSceneController).GetMethod("RebuildGeneratedMap", Private).Invoke(map, null);
            Require((bool)Get(window, "mapsDirty"), "A tile rebuild did not invalidate Navigator's tile registry.");
            window.UpdateVisibility(camera);
            SpriteRenderer[] rebuilt = map.EditorTileRenderers.OrderBy(tile => tile.transform.position.x).ToArray();
            Require(rebuilt[0].gameObject.activeSelf && rebuilt[1].gameObject.activeSelf
                && !rebuilt[2].gameObject.activeSelf && !rebuilt[3].gameObject.activeSelf,
                "Rebuilding tiles with an unchanged camera left distant replacement tiles active.");
            rebuilt[2].gameObject.SetActive(true);
            Call(window, "HandleHierarchyChanged");
            window.UpdateVisibility(camera);
            Require(!rebuilt[2].gameObject.activeSelf,
                "Reactivating an existing distant tile bypassed visibility because the camera did not move.");
        }
        finally
        {
            if (window != null) Object.DestroyImmediate(window);
            if (readback != null) Object.DestroyImmediate(readback);
            Object.DestroyImmediate(root);
            foreach (Object resource in resources)
                Object.DestroyImmediate(resource);
        }
    }

    private static void Set(object target, string field, object value) => target.GetType().GetField(field, Private).SetValue(target, value);
    private static object Get(object target, string field) => target.GetType().GetField(field, Private).GetValue(target);
    private static void Call(object target, string method, params object[] args) => target.GetType().GetMethod(method, Private).Invoke(target, args);
    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
