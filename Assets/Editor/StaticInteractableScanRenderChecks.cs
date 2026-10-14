using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using AnimalGame.Rendering;
using AnimalGame.RobotMap;
using AnimalGame.World;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace AnimalGame.Editor
{
    /// <summary>GPU checks of production disconnected two-direction bars in disposable editor fixtures.</summary>
    public static class StaticInteractableScanRenderChecks
    {
        private const int Size = 512;
        private const float Angle = -37f, Width = 3f, Spacing = 24f, IntersectionGap = 8f;
        private const string OutputDirectory = "output/StaticInteractableScanRenderChecks";
        private static readonly Vector2 Center = new(Size * .5f, Size * .5f);
        private static readonly Rect Viewport = new(0f, 0f, Size, Size);
        private static readonly Color Gold = new Color32(212, 184, 84, 255);
        private static readonly MethodInfo Populate = typeof(BioScanStaticAreaGraphic).GetMethod(
            "OnPopulateMesh", BindingFlags.Instance | BindingFlags.NonPublic,
            null, new[] { typeof(VertexHelper) }, null);

        [MenuItem("Animal Game/Validation/Run Static Interactable Scan Render Checks")]
        public static void Run()
        {
            Require(!EditorApplication.isPlayingOrWillChangePlaymode, "Run the scan render checks in Edit Mode.");
            Require(SystemInfo.graphicsDeviceType != GraphicsDeviceType.Null,
                "Static scan pixel checks require a graphics device; omit -nographics.");
            Shader shader = AssetDatabase.LoadAssetAtPath<Shader>("Assets/Shaders/Resources/BioScanStaticArea.shader");
            Require(shader != null && shader.isSupported && !ShaderUtil.ShaderHasError(shader),
                "The production static-area shader is unavailable or has compilation errors.");
            Scene scene = EditorSceneManager.NewPreviewScene();
            Material material = null;
            Mesh largeMesh = null, smallMesh = null, viewportMesh = null;
            Texture2D large = null, small = null, circleClipped = null, viewportClipped = null, reference = null;
            float oldClipEnabled = Shader.GetGlobalFloat("_PlayerUiClipEnabled");
            float oldClipRadius = Shader.GetGlobalFloat("_PlayerUiClipRadiusPixels");
            float oldClipSoftness = Shader.GetGlobalFloat("_PlayerUiClipSoftnessPixels");
            Vector4 oldClipCenter = Shader.GetGlobalVector("_PlayerUiClipCenterPixels");
            float oldZTest = Shader.GetGlobalFloat("unity_GUIZTestMode");
            try
            {
                Directory.CreateDirectory(OutputDirectory);
                material = new Material(shader) { hideFlags = HideFlags.HideAndDontSave };
                Shader.SetGlobalFloat("unity_GUIZTestMode", (int)CompareFunction.Always);
                PlayerUiOrganicVisibility.DisableClip();
                List<StaticScanLineSegment> largeLines = MakePattern(176f);
                List<StaticScanLineSegment> smallLines = MakePattern(106f);
                CheckGridGeometry(largeLines, 176f);
                CheckGridGeometry(smallLines, 106f);
                largeMesh = MakeAreaMesh(scene, largeLines, Center, Gold, Width, Viewport);
                large = Capture(largeMesh, material, Color.clear);
                CheckPatternPixels(large, largeLines, Center, Width);
                CheckFlatEndpoints(large, largeLines, Center);
                CheckEmptyIntersections(large, largeLines);
                File.WriteAllBytes(Path.Combine(OutputDirectory, "broken-grid-pixel-probe.png"), large.EncodeToPNG());
                reference = Capture(largeMesh, material, Color.black);
                File.WriteAllBytes(Path.Combine(OutputDirectory, "broken-grid-preview.png"), reference.EncodeToPNG());
                smallMesh = MakeAreaMesh(scene, smallLines, Center, Gold, Width, Viewport);
                small = Capture(smallMesh, material, Color.clear);
                CheckPatternPixels(small, smallLines, Center, Width);
                CheckFlatEndpoints(small, smallLines, Center);
                CheckEmptyIntersections(small, smallLines);
                CheckStableStyle(large, small, largeLines, smallLines);
                Rect croppedViewport = new(152f, 116f, 208f, 240f);
                viewportMesh = MakeAreaMesh(scene, largeLines, Center, Gold, Width, croppedViewport);
                viewportClipped = Capture(viewportMesh, material, Color.clear);
                CheckViewportClipping(large, viewportClipped, croppedViewport);
                PlayerUiOrganicVisibility.SetClip(Center, 60f, 2f);
                circleClipped = Capture(largeMesh, material, Color.clear);
                CheckCircleIndependence(large, circleClipped);
                ExportProductionPreview(scene, material);
                Require(!ShaderUtil.ShaderHasError(shader), "The static-line shader reported errors after rendering.");
                Debug.Log("Static interactable scan render checks PASS: two perpendicular line families, complete flat endpoints, "
                    + "transparent 8 px intersection gaps, 3 px width / 24 px spacing / 16 px segment length, "
                    + "no crossings or clipped fringe segments, stable style at two footprint sizes, viewport "
                    + "clipping, and visible gold outside the organic UI circle. Device: "
                    + SystemInfo.graphicsDeviceType + ". Preview: "
                    + Path.GetFullPath(Path.Combine(OutputDirectory, "big-garbage-yellow-area.png")));
            }
            finally
            {
                Shader.SetGlobalFloat("_PlayerUiClipEnabled", oldClipEnabled);
                Shader.SetGlobalFloat("_PlayerUiClipRadiusPixels", oldClipRadius);
                Shader.SetGlobalFloat("_PlayerUiClipSoftnessPixels", oldClipSoftness);
                Shader.SetGlobalVector("_PlayerUiClipCenterPixels", oldClipCenter);
                Shader.SetGlobalFloat("unity_GUIZTestMode", oldZTest);
                if (large != null) Object.DestroyImmediate(large);
                if (small != null) Object.DestroyImmediate(small);
                if (circleClipped != null) Object.DestroyImmediate(circleClipped);
                if (viewportClipped != null) Object.DestroyImmediate(viewportClipped);
                if (reference != null) Object.DestroyImmediate(reference);
                if (largeMesh != null) Object.DestroyImmediate(largeMesh);
                if (smallMesh != null) Object.DestroyImmediate(smallMesh);
                if (viewportMesh != null) Object.DestroyImmediate(viewportMesh);
                if (material != null) Object.DestroyImmediate(material);
                EditorSceneManager.ClosePreviewScene(scene);
            }
        }

        public static void RunBatch()
        {
            try { Run(); EditorApplication.Exit(0); }
            catch (Exception error) { Debug.LogException(error); EditorApplication.Exit(1); }
        }

        private static List<StaticScanLineSegment> MakePattern(float radius)
        {
            var hull = new List<Vector2>
            {
                new(-radius, -radius), new(radius, -radius),
                new(radius, radius), new(-radius, radius)
            };
            var lines = new List<StaticScanLineSegment>();
            BioScanStaticLinePattern.Build(hull, Spacing, Angle, lines, IntersectionGap, Width);
            Require(lines.Count > 20, "The production builder did not create a substantial disconnected grid.");
            return lines;
        }

        private static Mesh MakeAreaMesh(Scene scene, List<StaticScanLineSegment> segments, Vector2 anchor,
            Color tint, float width, Rect viewport)
        {
            var root = new GameObject("Static line GPU mesh fixture", typeof(RectTransform),
                typeof(CanvasRenderer), typeof(BioScanStaticAreaGraphic));
            Mesh mesh = null;
            try
            {
                SceneManager.MoveGameObjectToScene(root, scene);
                root.transform.position = Vector3.zero;
                root.transform.rotation = Quaternion.identity;
                root.transform.localScale = Vector3.one;
                BioScanStaticAreaGraphic graphic = root.GetComponent<BioScanStaticAreaGraphic>();
                graphic.raycastTarget = false;
                graphic.maskable = false;
                Require(graphic.TryAddArea(segments, anchor, tint, width, viewport),
                    "Could not create the production finite-line mesh.");
                mesh = new Mesh { hideFlags = HideFlags.HideAndDontSave };
                using (var helper = new VertexHelper())
                {
                    Populate.Invoke(graphic, new object[] { helper });
                    helper.FillMesh(mesh);
                }
                Require(mesh.vertexCount == segments.Count * 4,
                    "The production graphic did not create one complete quad per line segment.");
                Vector2 firstCenter = (segments[0].Start + segments[0].End) * .5f + anchor;
                Vector3[] vertices = mesh.vertices;
                Vector2 generatedCenter = (Vector2)(vertices[0] + vertices[1] + vertices[2] + vertices[3]) * .25f;
                Require(Vector2.Distance(generatedCenter, firstCenter) < .01f,
                    "The GPU fixture did not map its screen anchor to pixel-coordinate mesh vertices.");
                return mesh;
            }
            catch { if (mesh != null) Object.DestroyImmediate(mesh); throw; }
            finally { Object.DestroyImmediate(root); }
        }

        private static Texture2D Capture(Mesh mesh, Material material, Color background,
            Mesh spriteMesh = null, Material spriteMaterial = null)
        {
            RenderTexture previous = RenderTexture.active;
            bool oldSrgbWrite = GL.sRGBWrite;
            var target = new RenderTexture(Size, Size, 0, RenderTextureFormat.ARGB32);
            var snapshot = new Texture2D(Size, Size, TextureFormat.RGBA32, false);
            bool matrixPushed = false;
            try
            {
                target.Create();
                RenderTexture.active = target;
                GL.sRGBWrite = QualitySettings.activeColorSpace == ColorSpace.Linear;
                GL.Clear(true, true, background);
                GL.PushMatrix();
                matrixPushed = true;
                GL.LoadPixelMatrix(0f, Size, 0f, Size);
                if (spriteMesh != null && spriteMaterial != null)
                {
                    Require(spriteMaterial.SetPass(0), "The waste preview sprite material has no supported pass.");
                    Graphics.DrawMeshNow(spriteMesh, Matrix4x4.identity);
                }
                Require(material.SetPass(0), "The static-line material has no supported pass.");
                Graphics.DrawMeshNow(mesh, Matrix4x4.identity);
                GL.PopMatrix();
                matrixPushed = false;
                snapshot.ReadPixels(new Rect(0f, 0f, Size, Size), 0, 0);
                snapshot.Apply();
                return snapshot;
            }
            catch { Object.DestroyImmediate(snapshot); throw; }
            finally
            {
                if (matrixPushed) GL.PopMatrix();
                GL.sRGBWrite = oldSrgbWrite;
                RenderTexture.active = previous;
                target.Release();
                Object.DestroyImmediate(target);
            }
        }

        private static void CheckGridGeometry(List<StaticScanLineSegment> lines, float footprintRadius)
        {
            float radians = Angle * Mathf.Deg2Rad;
            Vector2 direction = new(Mathf.Cos(radians), Mathf.Sin(radians));
            Vector2 normal = new(-direction.y, direction.x);
            int firstFamily = 0, secondFamily = 0;
            foreach (StaticScanLineSegment line in lines)
            {
                Vector2 vector = line.End - line.Start;
                Require(Mathf.Abs(vector.magnitude - (Spacing - IntersectionGap)) < .01f,
                    "The grid contains an incomplete fringe segment or changed the configured intersection gap.");
                bool first = Mathf.Abs(Vector2.Dot(vector.normalized, direction)) > .99999f;
                bool second = Mathf.Abs(Vector2.Dot(vector.normalized, normal)) > .99999f;
                Require(first || second, "A grid segment is outside the two perpendicular directions.");
                if (first) firstFamily++; else secondFamily++;
                Vector2 center = (line.Start + line.End) * .5f;
                float along = Vector2.Dot(center, first ? direction : normal) / Spacing;
                float across = Vector2.Dot(center, first ? normal : direction) / Spacing;
                Require(Mathf.Abs(across - Mathf.Round(across)) < .001f
                    && Mathf.Abs(along - .5f - Mathf.Round(along - .5f)) < .001f,
                    "The grid did not retain its 24 px shared lattice or half-cell short-segment phase.");
                Vector2 edgeNormal = new(-vector.normalized.y, vector.normalized.x);
                foreach (Vector2 endpoint in new[] { line.Start, line.End })
                foreach (float side in new[] { -1f, 1f })
                {
                    Vector2 corner = endpoint + edgeNormal * (Width * .5f * side);
                    Require(Mathf.Abs(corner.x) <= footprintRadius + .01f
                        && Mathf.Abs(corner.y) <= footprintRadius + .01f,
                        "An edge bar was clipped by the footprint instead of keeping a complete cap.");
                }
            }
            Require(firstFamily > 10 && secondFamily > 10,
                "The production grid does not contain substantial coverage from both perpendicular families.");
            // Shared lattice phase plus a gap larger than the stroke width
            // prevents the perpendicular rectangular strokes from touching.
            Require(IntersectionGap > Width + 1f, "The fixture gap is too small to verify separate strokes.");
        }

        private static void CheckEmptyIntersections(Texture2D texture, List<StaticScanLineSegment> lines)
        {
            float radians = Angle * Mathf.Deg2Rad;
            Vector2 direction = new(Mathf.Cos(radians), Mathf.Sin(radians));
            Vector2 normal = new(-direction.y, direction.x);
            int nodes = 0, emptySamples = 0;
            for (int column = -3; column <= 3; column++)
            for (int row = -3; row <= 3; row++)
            {
                Vector2 node = direction * (column * Spacing) + normal * (row * Spacing);
                bool surrounded = true;
                foreach (Vector2 incoming in new[] { direction, -direction, normal, -normal })
                {
                    Vector2 endpoint = node + incoming * (IntersectionGap * .5f);
                    bool found = false;
                    foreach (StaticScanLineSegment line in lines)
                        if ((line.Start - endpoint).sqrMagnitude < .0001f
                            || (line.End - endpoint).sqrMagnitude < .0001f)
                        { found = true; break; }
                    if (!found) { surrounded = false; break; }
                }
                if (!surrounded) continue;
                nodes++;
                Vector2 absolute = node + Center;
                int minX = Mathf.FloorToInt(absolute.x - IntersectionGap * .5f);
                int maxX = Mathf.CeilToInt(absolute.x + IntersectionGap * .5f);
                int minY = Mathf.FloorToInt(absolute.y - IntersectionGap * .5f);
                int maxY = Mathf.CeilToInt(absolute.y + IntersectionGap * .5f);
                for (int y = minY; y <= maxY; y++)
                for (int x = minX; x <= maxX; x++)
                {
                    Vector2 offset = new Vector2(x + .5f, y + .5f) - absolute;
                    if (offset.sqrMagnitude >= Mathf.Pow(IntersectionGap * .5f - .75f, 2f)) continue;
                    Require(texture.GetPixel(x, y).a < .01f,
                        "The final GPU image filled an intentional transparent intersection at " + column + "," + row + ".");
                    emptySamples++;
                }
                foreach (Vector2 incoming in new[] { direction, -direction, normal, -normal })
                    Require(Sample(texture, absolute + incoming * (IntersectionGap * .5f + 2f)).a > .7f,
                        "A gap was made by removing an entire direction instead of keeping four separate approaching bars.");
            }
            Require(nodes > 15 && emptySamples > 350,
                "Insufficient real GPU intersection holes were verified between four visible incoming strokes.");
        }

        private static float RectangleDistance(Vector2 point, StaticScanLineSegment line, Vector2 anchor, float width)
        {
            Vector2 vector = line.End - line.Start;
            Vector2 direction = vector.normalized, normal = new(-direction.y, direction.x);
            Vector2 offset = point - anchor - (line.Start + line.End) * .5f;
            Vector2 q = new(Mathf.Abs(Vector2.Dot(offset, direction)) - vector.magnitude * .5f,
                Mathf.Abs(Vector2.Dot(offset, normal)) - width * .5f);
            return Vector2.Max(q, Vector2.zero).magnitude + Mathf.Min(Mathf.Max(q.x, q.y), 0f);
        }

        private static float[] RasterDistances(List<StaticScanLineSegment> lines, Vector2 anchor, float width)
        {
            var result = new float[Size * Size];
            for (int index = 0; index < result.Length; index++) result[index] = float.PositiveInfinity;
            // Only pixels near a bar can have coverage. Avoid an expensive
            // full-image search through hundreds of independent short bars.
            Vector2 padding = Vector2.one * (width * .5f + 1f);
            foreach (StaticScanLineSegment line in lines)
            {
                Vector2 minimum = Vector2.Min(line.Start, line.End) + anchor - padding;
                Vector2 maximum = Vector2.Max(line.Start, line.End) + anchor + padding;
                int minX = Mathf.Clamp(Mathf.FloorToInt(minimum.x), 0, Size - 1);
                int maxX = Mathf.Clamp(Mathf.CeilToInt(maximum.x), 0, Size - 1);
                int minY = Mathf.Clamp(Mathf.FloorToInt(minimum.y), 0, Size - 1);
                int maxY = Mathf.Clamp(Mathf.CeilToInt(maximum.y), 0, Size - 1);
                for (int y = minY; y <= maxY; y++)
                for (int x = minX; x <= maxX; x++)
                {
                    int index = y * Size + x;
                    result[index] = Mathf.Min(result[index],
                        RectangleDistance(new Vector2(x + .5f, y + .5f), line, anchor, width));
                }
            }
            return result;
        }

        private static void CheckPatternPixels(Texture2D texture, List<StaticScanLineSegment> lines,
            Vector2 anchor, float width)
        {
            Color[] pixels = texture.GetPixels();
            float[] distances = RasterDistances(lines, anchor, width);
            int lineSamples = 0, gapSamples = 0, edgeSamples = 0;
            for (int y = 0; y < Size; y++)
            for (int x = 0; x < Size; x++)
            {
                Color actual = pixels[y * Size + x];
                float distance = distances[y * Size + x];
                float coverage = 1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(-.5f, .5f, distance));
                // UI SrcAlpha blending over a transparent target writes alpha squared.
                // This probes the exact configured width and the full flat caps,
                // including their one-pixel antialias transition.
                Require(Mathf.Abs(actual.a - coverage * coverage) < .035f,
                    "Finite-line pixels disagree with the configured width or complete flat cap at "
                    + x + "," + y + ".");
                if (distance < -.8f)
                {
                    lineSamples++;
                    Require(actual.a > .93f, "A finite line is missing a core pixel at " + x + "," + y + ".");
                    Require(actual.r > actual.b && actual.g > actual.b, "A line failed to render the production gold tint.");
                }
                else if (distance > 1.2f)
                {
                    gapSamples++;
                    Require(actual.a < .01f,
                        "An intersection, edge remnant or width overflow filled an intended gap at " + x + "," + y + ".");
                }
                else if (actual.a > .01f && actual.a < .93f) edgeSamples++;
            }
            Require(lineSamples > 1500 && gapSamples > 150000 && edgeSamples > 500,
                "The pixel probe did not cover line interiors, antialiased edges and transparent gaps.");
        }

        private static void CheckFlatEndpoints(Texture2D texture, List<StaticScanLineSegment> lines, Vector2 anchor)
        {
            int count = 0;
            foreach (StaticScanLineSegment line in lines)
            {
                Vector2 direction = (line.End - line.Start).normalized;
                foreach (Vector2 endpoint in new[] { line.Start, line.End })
                {
                    Vector2 outward = endpoint == line.Start ? -direction : direction;
                    Require(Sample(texture, endpoint + anchor - outward * 3f).a > .7f,
                        "A finite line did not reach its clean flat endpoint.");
                    Require(Sample(texture, endpoint + anchor + outward * 3f).a < .01f,
                        "A finite line extended beyond its complete flat endpoint.");
                    count++;
                }
            }
            Require(count > 6, "Insufficient complete endpoints were checked.");
        }

        private static Color Sample(Texture2D texture, Vector2 point)
        {
            int x = Mathf.FloorToInt(point.x), y = Mathf.FloorToInt(point.y);
            Require(x >= 0 && x < Size && y >= 0 && y < Size, "An endpoint test fell outside the fixture image.");
            return texture.GetPixel(x, y);
        }

        private static void CheckStableStyle(Texture2D large, Texture2D small,
            List<StaticScanLineSegment> largeLines, List<StaticScanLineSegment> smallLines)
        {
            Color[] first = large.GetPixels(), second = small.GetPixels();
            float[] firstDistances = RasterDistances(largeLines, Center, Width);
            float[] secondDistances = RasterDistances(smallLines, Center, Width);
            int count = 0;
            for (int y = 190; y < 322; y++)
            for (int x = 190; x < 322; x++)
            {
                // Compare shared cores and gaps, away from complete endpoints.
                float a = firstDistances[y * Size + x];
                float b = secondDistances[y * Size + x];
                if (!(float.IsPositiveInfinity(a) && float.IsPositiveInfinity(b))
                    && Mathf.Abs(a - b) > .01f) continue;
                Require(Mathf.Abs(first[y * Size + x].a - second[y * Size + x].a) < .02f,
                    "Object area changed the configured line width or spacing in screen pixels.");
                count++;
            }
            Require(count > 8000, "Insufficient common pixels were available to compare finite-line styling.");
        }

        private static void CheckViewportClipping(Texture2D before, Texture2D after, Rect viewport)
        {
            Color[] original = before.GetPixels(), cropped = after.GetPixels();
            int removedGold = 0, retainedGold = 0;
            for (int y = 0; y < Size; y++)
            for (int x = 0; x < Size; x++)
            {
                int index = y * Size + x;
                if (viewport.Contains(new Vector2(x + .5f, y + .5f)))
                {
                    Require(Mathf.Abs(original[index].a - cropped[index].a) < .01f,
                        "The actual viewport changed a line pixel inside its bounds.");
                    if (cropped[index].a > .8f) retainedGold++;
                }
                else
                {
                    Require(cropped[index].a < .01f, "Static lines leaked outside the actual game viewport.");
                    if (original[index].a > .8f) removedGold++;
                }
            }
            Require(removedGold > 500 && retainedGold > 1000,
                "Viewport clipping did not exercise substantial inside and outside line coverage.");
        }

        private static void CheckCircleIndependence(Texture2D before, Texture2D after)
        {
            Color[] original = before.GetPixels(), clipped = after.GetPixels();
            int outsideGold = 0;
            for (int y = 0; y < Size; y++)
            for (int x = 0; x < Size; x++)
            {
                int index = y * Size + x;
                Require(Mathf.Abs(original[index].a - clipped[index].a) < .01f,
                    "The organic UI circle changed a static scan pixel.");
                if ((new Vector2(x + .5f, y + .5f) - Center).sqrMagnitude > 80f * 80f && clipped[index].a > .8f)
                    outsideGold++;
            }
            Require(outsideGold > 1500, "No substantial yellow lines remained outside the circular player view.");
        }

        private static void ExportProductionPreview(Scene scene, Material lineMaterial)
        {
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Environment/Garbage/Big_Garbage.prefab");
            Require(prefab != null, "The production large-waste prefab is missing.");
            GameObject instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab, scene);
            Mesh lineMesh = null, bodyMesh = null;
            Material bodyMaterial = null;
            Texture2D preview = null, pureLines = null;
            try
            {
                ScannableInteractable target = instance.GetComponent<ScannableInteractable>();
                Require(target != null, "Large waste has no production scan attribute.");
                var outline = new List<Vector3>();
                Require(target.TryGetWorldOutline(outline) && outline.Count >= 3, "Large waste has no production envelope.");
                Vector2 minimum = new(float.PositiveInfinity, float.PositiveInfinity);
                Vector2 maximum = new(float.NegativeInfinity, float.NegativeInfinity);
                foreach (Vector3 point in outline)
                {
                    minimum = Vector2.Min(minimum, point);
                    maximum = Vector2.Max(maximum, point);
                }
                Vector2 worldCenter = (minimum + maximum) * .5f;
                float scale = 400f / Mathf.Max(maximum.x - minimum.x, maximum.y - minimum.y);
                var canonicalPoints = new List<Vector2>();
                foreach (Vector3 point in outline) canonicalPoints.Add(((Vector2)point - worldCenter) * scale);
                var lines = new List<StaticScanLineSegment>();
                BioScanStaticLinePattern.Build(canonicalPoints, target.LineSpacingPixels, target.LineAngleDegrees, lines,
                    target.IntersectionGapPixels, target.LineWidthPixels);
                Require(lines.Count > 0, "The production large-waste preview has no disconnected grid segments.");
                lineMesh = MakeAreaMesh(scene, lines, Center, target.AreaColor, target.LineWidthPixels, Viewport);
                pureLines = Capture(lineMesh, lineMaterial, Color.black);
                File.WriteAllBytes(Path.Combine(OutputDirectory, "pure-lines-preview.png"), pureLines.EncodeToPNG());
                WorldInteraction interaction = instance.GetComponent<WorldInteraction>();
                SpriteRenderer renderer = interaction != null ? interaction.SpriteSource : null;
                Require(renderer != null && renderer.sprite != null, "Production large waste has no body sprite.");
                Sprite sprite = renderer.sprite;
                Vector2[] vertices = sprite.vertices;
                var positions = new Vector3[vertices.Length];
                var colors = new Color32[vertices.Length];
                for (int i = 0; i < vertices.Length; i++)
                {
                    Vector2 local = vertices[i];
                    if (renderer.flipX) local.x = -local.x;
                    if (renderer.flipY) local.y = -local.y;
                    Vector2 world = renderer.transform.TransformPoint(local);
                    positions[i] = (world - worldCenter) * scale + Center;
                    colors[i] = renderer.color;
                }
                ushort[] spriteTriangles = sprite.triangles;
                int[] triangles = new int[spriteTriangles.Length];
                for (int i = 0; i < triangles.Length; i++) triangles[i] = spriteTriangles[i];
                bodyMesh = new Mesh { hideFlags = HideFlags.HideAndDontSave };
                bodyMesh.vertices = positions; bodyMesh.uv = sprite.uv;
                bodyMesh.colors32 = colors; bodyMesh.triangles = triangles;
                Shader bodyShader = Shader.Find("Sprites/Default");
                Require(bodyShader != null && bodyShader.isSupported, "Could not render the production waste sprite preview.");
                bodyMaterial = new Material(bodyShader) { hideFlags = HideFlags.HideAndDontSave };
                bodyMaterial.mainTexture = sprite.texture;
                preview = Capture(lineMesh, lineMaterial, Color.black, bodyMesh, bodyMaterial);
                File.WriteAllBytes(Path.Combine(OutputDirectory, "big-garbage-yellow-area.png"), preview.EncodeToPNG());
            }
            finally
            {
                if (preview != null) Object.DestroyImmediate(preview);
                if (pureLines != null) Object.DestroyImmediate(pureLines);
                if (lineMesh != null) Object.DestroyImmediate(lineMesh);
                if (bodyMesh != null) Object.DestroyImmediate(bodyMesh);
                if (bodyMaterial != null) Object.DestroyImmediate(bodyMaterial);
                Object.DestroyImmediate(instance);
            }
        }

        private static void Require(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException(message);
        }
    }
}
