using System;
using System.Collections;
using System.Reflection;
using AnimalGame.Animals;
using AnimalGame.Discovery;
using AnimalGame.RobotMap;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace AnimalGame.Editor
{
    public static class BioScanRegressionChecks
    {
        [MenuItem("Animal Game/Validation/Run Biological Scan Checks")]
        public static void Run()
        {
            using (var f = new Fixture())
            {
                DiscoverableEntity first = f.Animal(new Vector2(4f, 0f));
                f.Markers.ShowScannedAnimal(first);
                f.Tick(0f);
                object state = f.State(first);
                Require(f.Root(state).activeSelf && !f.Seen(state),
                    "An on-screen animal outside the player circle must show an unconfirmed marker.");
                Image icon = (Image)Get(state, "Icon");
                Require(icon.material.GetFloat("_Filled") == 0f && !icon.raycastTarget && !icon.maskable,
                    "Unconfirmed marker must be a hollow star without intercepting input or view clipping.");
                Require(((Color32)icon.color).Equals(new Color32(212, 184, 84, 255)),
                    "Marker must use interaction gold #D4B854.");

                f.Tick(3f);
                DiscoverableEntity second = f.Animal(new Vector2(-4f, 0f));
                f.Markers.ShowScannedAnimal(second);
                f.Tick(0f);
                f.Tick(4f);
                Require(!f.Root(state).activeSelf && f.Root(f.State(second)).activeSelf,
                    "Each animal needs its own seven-second lifetime starting at wave contact.");

                f.Markers.ShowScannedAnimal(first);
                f.Tick(0f);
                f.Tick(6f);
                f.Markers.ShowScannedAnimal(first);
                f.Tick(0f);
                f.Tick(1f);
                Require(f.Count == 2 && Mathf.Abs(f.Remaining(state) - 6f) < 0.001f,
                    "Repeat scans must refresh seven seconds without duplicating markers.");

                first.transform.position = new Vector3(0f, 0f, 0f);
                f.Tick(0f);
                Require(f.Seen(state) && icon.material.GetFloat("_Filled") == 1f
                    && icon.transform.childCount == 0,
                    "A visible animal entering the player view must switch to a solid star without a dot overlay.");
                Vector2 oldPosition = icon.rectTransform.anchoredPosition;
                first.transform.position = new Vector3(4f, 1f, 0f);
                f.Tick(0f);
                Require(f.Seen(state) && icon.material.GetFloat("_Filled") == 1f
                    && Vector2.Distance(oldPosition, icon.rectTransform.anchoredPosition) > 10f,
                    "The confirmed marker must follow the animal and stay confirmed outside the view.");
                f.Tick(6f);
                f.Markers.ShowScannedAnimal(first);
                f.Tick(0f);
                Require(f.Seen(state) && icon.material.GetFloat("_Filled") == 1f,
                    "Confirmation must survive expiry and a later scan of the same individual.");

                SpriteRenderer secondBody = second.GetComponent<SpriteRenderer>();
                second.transform.position = Vector3.zero;
                secondBody.color = new Color(1f, 1f, 1f, 0f);
                f.Tick(0f);
                Require(!f.Seen(f.State(second)), "A fully submerged/transparent body must not count as seen.");
                secondBody.color = Color.white;
                secondBody.enabled = false;
                f.Tick(0f);
                Require(!f.Seen(f.State(second)), "A disabled body must not count as seen.");
                secondBody.enabled = true;
                using (AnimalSimulation.AcquirePause())
                {
                    float remaining = f.Remaining(state);
                    Call(f.Markers, "LateUpdate");
                    Require(!f.Seen(f.State(second)) && f.Remaining(state) == remaining,
                        "Animal-simulation pause must freeze both observation and marker lifetime.");
                }
                f.Tick(0f);
                Require(f.Seen(f.State(second)),
                    "First sight after marker expiry must be remembered for the next scan.");

                first.transform.position = new Vector3(100f, 0f, 0f);
                f.Tick(0f);
                Require(!f.Root(state).activeSelf, "Off-screen markers must be hidden without edge clamping.");
                first.transform.position = new Vector3(4f, 0f, 0f);
                f.Tick(0f);
                Require(f.Root(state).activeSelf, "An unexpired marker must reappear when back on screen.");
                first.gameObject.SetActive(false);
                f.Tick(0f);
                Require(!f.Root(state).activeSelf && f.Remaining(state) == 0f,
                    "Disabling an animal must retire its active marker.");

                // Cleanup the view explicitly because edit-mode checks cannot
                // use the component's deferred play-mode Destroy operation.
                Object.DestroyImmediate(f.Root(state));
                Object.DestroyImmediate(first.gameObject);
                f.Tick(0f);
                Require(f.Count == 1, "Destroyed entity records must be removable without losing other markers.");
            }

            RunTrailChecks();
            RunHitBurstChecks();

            Shader shader = AssetDatabase.LoadAssetAtPath<Shader>("Assets/Shaders/BioScanSignalClip.shader");
            Require(shader != null && !ShaderUtil.ShaderHasError(shader), "Biological signal shader must import without errors.");
            var material = new Material(shader);
            try
            {
                Require(!material.HasProperty("_ClipRadiusPixels"),
                    "Biological scan points must no longer use the circular player-view clip.");
            }
            finally { Object.DestroyImmediate(material); }
            Debug.Log("Biological scan checks PASS: off-view markers, independent expiry, rescan refresh, first sight, retained confirmation, body visibility, pause, tracking, off-screen/disabled cleanup, artwork, shader, breathing opacity, movement trails, independent trail expiry, camera projection.");
        }

        private static void RunTrailChecks()
        {
            using (var f = new Fixture())
            {
                // Keep lifecycle scenarios independent of visual tuning defaults.
                Set(f.Markers, "trailPointLifetime", 1.5f);
                Set(f.Markers, "trailPointSpacing", 0.3f);
                Set(f.Markers, "trailSpawnInterval", 0.3f);
                DiscoverableEntity animal = f.Animal(new Vector2(4f, 0f));
                f.Markers.ShowScannedAnimal(animal);
                f.Tick(0f);
                object state = f.State(animal);
                Image icon = (Image)Get(state, "Icon");
                var trail = (BioScanAnimalTrailGraphic)Get(f.Markers, "trailGraphic");
                var footprints = (IList)Get(trail, "footprints");
                f.Tick(0.6f);
                Require(Mathf.Abs(icon.color.a - 0.2f) < 0.001f && footprints.Count == 0,
                    "Half a breath must reach 20% opacity and a stationary animal must not leave footprints.");

                animal.transform.position += Vector3.right * 0.4f;
                f.Tick(0.2f);
                Require(footprints.Count == 0,
                    "Even fast movement must wait for the slower 0.3-second footprint cadence.");
                animal.transform.position += Vector3.right * 0.2f;
                f.Tick(0.11f);
                Require(footprints.Count == 1 && !f.Seen(state) && !trail.maskable && !trail.raycastTarget,
                    "Movement outside the player view must leave a non-interactive footprint at the next sample.");
                Vector3 originalPosition = (Vector3)Get(footprints[0], "Position");
                Require(originalPosition.x > 4.4f && originalPosition.x < 4.6f,
                    "Footprints must lie on the world-space movement path at the sampling time.");
                animal.transform.position += Vector3.right * 0.6f;
                f.Tick(0.3f);
                Require(footprints.Count == 2,
                    "Crossing several distance thresholds must still produce only one dot per sampling interval.");
                float remaining = (float)Get(footprints[0], "Remaining");
                Color beforePause = icon.color;
                using (AnimalSimulation.AcquirePause())
                {
                    Call(f.Markers, "LateUpdate");
                    Require((float)Get(footprints[0], "Remaining") == remaining && icon.color == beforePause,
                        "Pausing must freeze both trail expiry and marker breathing.");
                }

                Camera camera = (Camera)Get(f.Markers, "worldCamera");
                using (var mesh = new VertexHelper())
                {
                    Call(trail, "OnPopulateMesh", mesh);
                    Require(mesh.currentVertCount > 0, "Off-view footprints must produce visible UI geometry.");
                    UIVertex before = default;
                    mesh.PopulateUIVertex(ref before, 0);
                    camera.transform.position += Vector3.right * 0.25f;
                    camera.orthographicSize = 4f;
                    f.Tick(0f);
                    Call(trail, "OnPopulateMesh", mesh);
                    UIVertex after = default;
                    mesh.PopulateUIVertex(ref after, 0);
                    Require(Vector3.Distance(before.position, after.position) > 1f
                        && (Vector3)Get(footprints[0], "Position") == originalPosition,
                        "Camera movement and zoom must reproject footprints without moving their world anchors.");
                }

                f.Markers.ShowScannedAnimal(animal);
                f.Tick(0f);
                Require(footprints.Count == 2 && (float)Get(footprints[0], "Remaining") == remaining,
                    "A repeat scan must not duplicate existing footprints or refresh their age.");
                f.Tick(1.2f);
                Require(footprints.Count == 1, "Older footprints must expire independently of newer ones.");
                f.Tick(0.31f);
                Require(footprints.Count == 0 && f.Root(state).activeSelf,
                    "Each footprint must expire after 1.5 seconds even while the animal marker remains active.");

                f.Tick(5.29f); // 0.2 seconds of the refreshed scan remain.
                animal.transform.position += Vector3.right * 0.96f;
                f.Tick(0.3f);
                Require(!f.Root(state).activeSelf && footprints.Count == 1,
                    "The final active part of a frame must leave footprints even when the scan expires in that frame.");
                foreach (object footprint in footprints)
                    Require(((Vector3)Get(footprint, "Position")).x < 5.85f,
                        "Movement after the seven-second boundary must not produce footprints.");
                int finalCount = footprints.Count;
                animal.transform.position += Vector3.right * 0.4f;
                f.Tick(0.1f);
                Require(footprints.Count == finalCount,
                    "After marker expiry, existing footprints must finish their own lifetime without new emission.");
                f.Tick(1.5f);
                Require(footprints.Count == 0, "All final footprints must drain after the scan has ended.");

                f.Markers.ShowScannedAnimal(animal);
                f.Tick(0f);
                animal.transform.position += Vector3.left * 5f;
                f.Tick(0.1f);
                Require(footprints.Count == 0, "Teleportation must not leave an artificial trail.");
                animal.transform.position += Vector3.right * 0.41f;
                f.Tick(0.31f);
                Require(footprints.Count == 1, "Ordinary movement after teleportation must resume trail emission.");
                Object.DestroyImmediate(f.Root(state));
                Object.DestroyImmediate(animal.gameObject);
                f.Tick(0.1f);
                Require(f.Count == 0 && footprints.Count == 1,
                    "Destroying the last animal must leave its existing footprints to expire naturally.");
                f.Tick(1.5f);
                Require(footprints.Count == 0, "Footprints must still expire when no tracked animal remains.");
            }

            using (var f = new Fixture())
            {
                Set(f.Markers, "trailSpawnInterval", 0.24f);
                Set(f.Markers, "trailPointSpacing", 0.25f);
                Set(f.Markers, "trailPointLifetime", 1.8f);
                DiscoverableEntity animal = f.Animal(new Vector2(4f, 0f));
                f.Markers.ShowScannedAnimal(animal);
                f.Tick(0f);
                for (int step = 0; step < 24; step++)
                {
                    animal.transform.position += Vector3.right * 0.08f;
                    f.Tick(0.06f);
                }
                var trail = (BioScanAnimalTrailGraphic)Get(f.Markers, "trailGraphic");
                Require(((IList)Get(trail, "footprints")).Count == 6,
                    "Samples aligned to frame boundaries must not be lost to floating-point rounding.");
            }
        }

        private static void RunHitBurstChecks()
        {
            using (var f = new Fixture())
            {
                Set(f.Markers, "scanHitBurstDuration", 0.45f);
                DiscoverableEntity animal = f.Animal(new Vector2(4f, 0f));
                f.Markers.ShowScannedAnimal(animal);
                f.Tick(0f);
                var graphic = (BioScanAnimalBurstGraphic)Get(f.Markers, "burstGraphic");
                var bursts = (IList)Get(graphic, "bursts");
                Require(bursts.Count == 1 && !f.Seen(f.State(animal))
                    && !graphic.maskable && !graphic.raycastTarget,
                    "Scan contact must play a burst outside player sight without confirming the animal or blocking input.");
                using (var mesh = new VertexHelper())
                {
                    Call(graphic, "OnPopulateMesh", mesh);
                    Require(mesh.currentVertCount > 0, "An off-view scan burst must render on screen.");
                    UIVertex initial = default;
                    mesh.PopulateUIVertex(ref initial, 1);
                    f.Tick(0.12f);
                    Call(graphic, "OnPopulateMesh", mesh);
                    UIVertex expanded = default;
                    mesh.PopulateUIVertex(ref expanded, 1);
                    Require(Vector3.Distance(initial.position, expanded.position) > 3f
                        && expanded.color.a < initial.color.a,
                        "The scan burst must expand outwards and fade as it ages.");
                }
                using (AnimalSimulation.AcquirePause())
                {
                    float age = (float)Get(bursts[0], "Age");
                    Call(f.Markers, "LateUpdate");
                    Require((float)Get(bursts[0], "Age") == age, "Pause must also freeze the hit burst.");
                }
                f.Markers.ShowScannedAnimal(animal);
                f.Markers.ShowScannedAnimal(animal);
                f.Tick(0f);
                Require(bursts.Count == 1 && (float)Get(bursts[0], "Age") == 0f,
                    "A repeat scan must restart one burst instead of stacking flashes on the same animal.");
                f.Tick(0.46f);
                Require(bursts.Count == 0 && f.Root(f.State(animal)).activeSelf,
                    "The brief burst must finish independently while the seven-second marker stays visible.");
                animal.transform.position = Vector3.zero;
                f.Tick(0f);
                Require(f.Seen(f.State(animal)) && bursts.Count == 0,
                    "First sight alone must not retrigger a scan-hit effect.");
                f.Markers.ShowScannedAnimal(animal);
                f.Tick(0f);
                f.Markers.enabled = false;
                // Preview-scene components do not receive play-mode callbacks.
                Call(f.Markers, "OnDisable");
                Require(bursts.Count == 0, "Disabling the marker system must clear active hit bursts.");
            }
        }

        public static void RunBatch()
        {
            try { Run(); EditorApplication.Exit(0); }
            catch (Exception error) { Debug.LogException(error); EditorApplication.Exit(1); }
        }

        private static void Require(bool condition, string message)
        { if (!condition) throw new InvalidOperationException(message); }
        private static object Get(object target, string name) => target.GetType()
            .GetField(name, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public).GetValue(target);
        private static void Set(object target, string name, object value) => target.GetType()
            .GetField(name, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public).SetValue(target, value);
        private static void Call(object target, string name, params object[] args) => target.GetType()
            .GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.DeclaredOnly).Invoke(target, args);

        private sealed class Fixture : IDisposable
        {
            private readonly Scene scene;
            private readonly Sprite bodySprite;
            public readonly BioScanAnimalMarkerUI Markers;
            private IDictionary States => (IDictionary)Get(Markers, "markers");
            public int Count => States.Count;

            public Fixture()
            {
                scene = EditorSceneManager.NewPreviewScene();
                Camera camera = Create("Scan validation camera").AddComponent<Camera>();
                camera.transform.position = new Vector3(0f, 0f, -10f);
                camera.orthographic = true;
                camera.orthographicSize = 5f;
                camera.pixelRect = new Rect(0f, 0f, 800f, 600f);
                var viewObject = new GameObject("Scan validation view", typeof(RectTransform), typeof(Canvas));
                SceneManager.MoveGameObjectToScene(viewObject, scene);
                Canvas canvas = viewObject.GetComponent<Canvas>();
                canvas.renderMode = RenderMode.ScreenSpaceOverlay;
                canvas.sortingOrder = 30;
                ScanChargeUI view = viewObject.AddComponent<ScanChargeUI>();
                Set(view, "uiRingRadiusPixels", 100f);
                Canvas.ForceUpdateCanvases();
                Vector2 viewCenter = view.GetUiCenterScreenPoint();
                camera.pixelRect = new Rect(viewCenter.x - 400f, viewCenter.y - 300f, 800f, 600f);
                Markers = Create("Scan validation markers").AddComponent<BioScanAnimalMarkerUI>();
                Markers.Initialize(view, camera);
                bodySprite = Sprite.Create(Texture2D.whiteTexture, new Rect(0f, 0f, 2f, 2f), Vector2.one * 0.5f, 4f);
            }

            private GameObject Create(string name)
            {
                var go = new GameObject(name);
                SceneManager.MoveGameObjectToScene(go, scene);
                return go;
            }

            public DiscoverableEntity Animal(Vector2 position)
            {
                GameObject go = Create("Scan validation animal");
                go.transform.position = position;
                SpriteRenderer body = go.AddComponent<SpriteRenderer>();
                body.sprite = bodySprite;
                AnimalPhotoSubject subject = go.AddComponent<AnimalPhotoSubject>();
                Set(subject, "photoBoundsRenderers", new[] { body });
                DiscoverableEntity entity = go.AddComponent<DiscoverableEntity>();
                entity.SetDiscovered(true);
                return entity;
            }

            public object State(DiscoverableEntity entity) => States[entity];
            public bool Seen(object state) => (bool)Get(state, "HasBeenSeen");
            public float Remaining(object state) => (float)Get(state, "Remaining");
            public GameObject Root(object state) => ((RectTransform)Get(state, "Root")).gameObject;
            public void Tick(float seconds)
            {
                Canvas.ForceUpdateCanvases();
                Call(Markers, "TickMarkers", seconds, true);
            }
            public void Dispose()
            {
                Markers.enabled = false;
                Canvas canvas = (Canvas)Get(Markers, "markerCanvas");
                if (canvas != null) Object.DestroyImmediate(canvas.gameObject);
                Object.DestroyImmediate((Material)Get(Markers, "hollowMarkerMaterial"));
                Object.DestroyImmediate((Material)Get(Markers, "solidMarkerMaterial"));
                EditorSceneManager.ClosePreviewScene(scene);
                Object.DestroyImmediate(bodySprite);
            }
        }
    }
}
