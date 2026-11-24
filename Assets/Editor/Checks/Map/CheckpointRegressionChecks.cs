using System;
using System.IO;
using System.Reflection;
using AnimalGame.MapTest;
using AnimalGame.RobotMap;
using AnimalGame.Save;
using AnimalGame.World;
using AnimalGame.World.Checkpoints;
using AnimalGame.World.Map;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;
using Object = UnityEngine.Object;

namespace AnimalGame.Editor
{
    public static class CheckpointRegressionChecks
    {
        private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
        private static void Require(bool value, string message)
        { if (!value) throw new InvalidOperationException(message); }
        private static void Call(object target, string method) => target.GetType().GetMethod(method, Private).Invoke(target, null);

        public static void RunBatch()
        {
            CheckPins();
            CheckDiscovery();
            WorldMapRegressionChecks.Run();
            Debug.Log("CHECKPOINT_CHECKS_PASS: independent station and overlap discovery, saved station coverage, cached Voronoi fog, hidden seeds, player pins and GPU map rendering.");
        }

        private static void CheckPins()
        {
            var state = new MapViewState();
            Vector2 screen = Vector2.one * 1000;
            state.Open(Vector2.one * 500, Vector2.one * 100, Vector2.one * 1000);
            state.Snap(screen, 14);
            Require(state.DisplayCursor == state.Cursor, "An empty map must leave the cursor free.");
            state.Submit();
            Require(state.Pins.Count == 1 && state.Pins[0] == state.Cursor, "A did not place a pin at the cursor.");
            state.Snap(screen, 14);
            Require(state.SelectedPin == 0, "A player pin did not snap.");
            state.Submit();
            Require(state.Pins.Count == 0, "Second A did not remove the pin.");
        }

        private static void CheckDiscovery()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Environment/Checkpoint/Checkpoint.prefab");
            Require(prefab != null && prefab.GetComponent<Checkpoint>() != null, "Checkpoint prefab is missing.");
            Require(prefab.GetComponent<Collider2D>() == null, "Checkpoint must not have a physics collider.");
            Require(prefab.GetComponent<WorldInteraction>().Kind == WorldInteractionKind.Checkpoint, "Checkpoint kind is not exclusive.");
            Require((InteractionPushPlan.Solids & WorldInteractionKind.Checkpoint) == 0, "Checkpoint blocks movement.");
            Shader fogShader = Shader.Find("Hidden/AnimalGame/Checkpoint Fog");
            Require(fogShader != null && !ShaderUtil.ShaderHasError(fogShader), "Fog shader failed compilation.");
            var previousPlayer = HeightMapPlayerSceneBootstrap.inst;
            var previousManager = CheckpointManager.inst;
            var previousSave = SaveSystem.Current;
            var currentSave = typeof(SaveSystem).GetProperty(nameof(SaveSystem.Current));
            string testSave = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".bin");
            float previousTime = Time.timeScale;
            var scene = EditorSceneManager.NewPreviewScene();
            GameObject New(string name)
            {
                var go = new GameObject(name);
                SceneManager.MoveGameObjectToScene(go, scene);
                return go;
            }
            var source = new Texture2D(128, 128, TextureFormat.RGBA32, false, true);
            CheckpointManager testManager = null;
            RenderTexture beforeSync = null;
            var pixels = new Color[128 * 128];
            for (int y = 0; y < 128; y++) for (int x = 0; x < 128; x++)
                pixels[y * 128 + x] = new Color(x / 127f, 0, 0, 1);
            source.SetPixels(pixels); source.Apply();
            try
            {
                currentSave.SetValue(null, new GameSave());
                var map = New("Map").AddComponent<MapTestSceneController>();
                using (var fixture = new TiledMapTestFixture(map, source, 100, 1, 140))
                {
                    var root = New("Checkpoint manager");
                    var a = Object.Instantiate(prefab, root.transform).GetComponent<Checkpoint>();
                    var bound = new SerializedObject(a);
                    const string stationId = "regression-station";
                    bound.FindProperty("stationId").stringValue = stationId;
                    bound.ApplyModifiedPropertiesWithoutUndo();
                    var group = new GameObject("Nested checkpoints"); group.transform.SetParent(root.transform, false);
                    var b = Object.Instantiate(prefab, group.transform).GetComponent<Checkpoint>();
                    a.transform.position = map.MapPositionToWorld(new Vector2(25, 50));
                    b.transform.position = map.MapPositionToWorld(new Vector2(75, 50));
                    b.gameObject.SetActive(false);
                    var manager = root.AddComponent<CheckpointManager>();
                    testManager = manager;
                    typeof(CheckpointManager).GetField("fogShader", Private).SetValue(manager, fogShader);
                    typeof(CheckpointManager).GetField("fogResolution", Private).SetValue(manager, 256);
                    Call(manager, "Awake");
                    var bootstrap = New("Player bootstrap").AddComponent<HeightMapPlayerSceneBootstrap>();
                    var robot = New("Robot shape").AddComponent<RobotMover>();
                    robot.ColliderRadius = .75f;
                    bootstrap.robot = robot; bootstrap.robotObject = robot.gameObject; bootstrap.map = map;
                    HeightMapPlayerSceneBootstrap.inst = bootstrap;
                    Time.timeScale = 1;
                    RenderTexture fog = manager.GetFog(map);
                    Require(manager.Checkpoints.Count == 2 && manager.TryGetStationRegion(map, stationId,
                        out var center, out var bounds) && Vector2.Distance(center, new Vector2(25, 50)) < .01f
                        && Mathf.Abs(bounds.xMax - 50f) < .01f && Mathf.Abs(bounds.width - 50f) < .01f,
                        "The station region did not stop halfway to the inactive neighboring seed.");
                    Require(RenderTexture.active != fog, "Fog bake leaked the active render target.");
                    Require(Sample(fog, .1f) > .99f && Sample(fog, .9f) > .99f, "Initial map is not fully fogged.");
                    int initialRevision = manager.Revision;
                    Require(manager.GetFog(map) == fog && manager.Revision == initialRevision, "Fog regenerated without a checkpoint change.");
                    var camera = New("Map camera").AddComponent<Camera>();
                    camera.orthographic = true; camera.orthographicSize = 10;
                    camera.transform.position = map.MapPositionToWorld(new Vector2(50, 50)) + Vector3.back * 10;
                    bootstrap.mapCamera = camera;
                    var uiPrefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Resources/UI/Map/WorldMapUI.prefab");
                    var uiGo = Object.Instantiate(uiPrefab); SceneManager.MoveGameObjectToScene(uiGo, scene);
                    var ui = uiGo.GetComponent<WorldMapUI>();
                    Call(ui, "OnEnable");
                    ui.Open();
                    Require(ui.IsOpen, "Map did not open.");
                    var tree = uiGo.GetComponent<UIDocument>().rootVisualElement;
                    Require(tree.Q("map-checkpoints") == null && tree.Q("pin-0") != null,
                        "Survey seeds must stay hidden while player pins remain available.");
                    var stylesheet = AssetDatabase.LoadAssetAtPath<StyleSheet>("Assets/Prefabs/Resources/UI/Map/Map.uss");
                    var elements = tree.Query<VisualElement>().ToList();
                    Require(stylesheet != null && elements.Exists(element => element.styleSheets.Contains(stylesheet)),
                        "The world map must load its runtime stylesheet.");
                    ui.Close();
                    robot.transform.position = a.transform.position;
                    var shape = robot.GetCollisionShape(map);
                    Require(WorldInteractionQuery.Query(shape, WorldInteractionKind.Checkpoint, map, scene), "Robot shape did not overlap checkpoint.");
                    Require(!WorldInteractionQuery.Query(shape, InteractionPushPlan.Solids, map, scene), "Checkpoint was included in solid collision queries.");
                    Call(manager, "LateUpdate");
                    Require(!a.Visited && !b.Visited && manager.Revision == initialRevision,
                        "Walking over a station-bound seed revealed map data.");
                    beforeSync = manager.CaptureFog(map);
                    var sync = typeof(SaveSystem).GetMethod(nameof(SaveSystem.SyncStation),
                        BindingFlags.Static | BindingFlags.NonPublic, null, new[] { typeof(string), typeof(string) }, null);
                    Require((bool)sync.Invoke(null, new object[] { stationId, testSave }), "Station sync was not committed.");
                    Require(SaveSystem.LoadOrCreate(testSave).syncedStationIds.Contains(stationId), "Station survey was not saved.");
                    fog = manager.GetFog(map);
                    Require(a.Visited && !b.Visited && Sample(fog, .1f) < .01f && Sample(fog, .9f) > .99f,
                        "Sync did not reveal exactly its bound Voronoi cell.");
                    Require(Sample(beforeSync, .1f) > .99f, "The reveal lost its independent pre-sync fog snapshot.");
                    int syncedRevision = manager.Revision;
                    Call(manager, "LateUpdate");
                    Require(manager.GetFog(map) == fog && manager.Revision == syncedRevision, "Repeated access rerendered fog.");
                    b.gameObject.SetActive(true);
                    robot.transform.position = b.transform.position;
                    Call(manager, "LateUpdate");
                    Require(b.Visited && Sample(fog, .1f) < .01f && Sample(fog, .9f) < .01f, "All visited cells did not fully reveal.");
                    Require(!ShaderUtil.ShaderHasError(fogShader), "Fog shader failed during rendering.");
                }
            }
            finally
            {
                if (beforeSync != null) RenderTexture.ReleaseTemporary(beforeSync);
                // Awake was invoked manually in this edit-mode fixture; release its GPU ownership explicitly.
                if (testManager != null) Call(testManager, "OnDestroy");
                EditorSceneManager.ClosePreviewScene(scene);
                Object.DestroyImmediate(source);
                Time.timeScale = previousTime;
                HeightMapPlayerSceneBootstrap.inst = previousPlayer;
                CheckpointManager.inst = previousManager;
                currentSave.SetValue(null, previousSave);
                if (File.Exists(testSave)) File.Delete(testSave);
            }
        }

        private static float Sample(RenderTexture texture, float u)
        {
            var previous = RenderTexture.active;
            var readback = new Texture2D(1, 1, TextureFormat.RGBA32, false, true);
            try
            {
                RenderTexture.active = texture;
                readback.ReadPixels(new Rect((int)(texture.width * u), texture.height / 2, 1, 1), 0, 0);
                readback.Apply();
                return readback.GetPixel(0, 0).r;
            }
            finally { RenderTexture.active = previous; Object.DestroyImmediate(readback); }
        }
    }
}
