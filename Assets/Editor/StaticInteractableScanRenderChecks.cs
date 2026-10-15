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
    /// <summary>GPU checks of complete continuous parallel bars in disposable editor fixtures.</summary>
    public static class StaticInteractableScanRenderChecks
    {
        private const int Size = 512;
        private const float Angle = 45f, Width = .9f, Spacing = 7.7f;
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
                CheckParallelGeometry(largeLines, 176f);
                CheckParallelGeometry(smallLines, 106f);
                largeMesh = MakeAreaMesh(scene, largeLines, Center, Gold, Width, Viewport);
                large = Capture(largeMesh, material, Color.clear);
                CheckPatternPixels(large, largeLines, Center, Width);
                CheckFlatEndpoints(large, largeLines, Center);
                CheckContinuousInteriors(large, largeLines);
                File.WriteAllBytes(Path.Combine(OutputDirectory, "parallel-lines-pixel-probe.png"), large.EncodeToPNG());
                reference = Capture(largeMesh, material, Color.black);
                File.WriteAllBytes(Path.Combine(OutputDirectory, "parallel-lines-preview.png"), reference.EncodeToPNG());
                smallMesh = MakeAreaMesh(scene, smallLines, Center, Gold, Width, Viewport);
                small = Capture(smallMesh, material, Color.clear);
                CheckPatternPixels(small, smallLines, Center, Width);
                CheckFlatEndpoints(small, smallLines, Center);
                CheckContinuousInteriors(small, smallLines);
                CheckStableStyle(large, small, largeLines, smallLines);
                Rect croppedViewport = new(152f, 116f, 208f, 240f);
                viewportMesh = MakeAreaMesh(scene, largeLines, Center, Gold, Width, croppedViewport);
                viewportClipped = Capture(viewportMesh, material, Color.clear);
                CheckViewportClipping(large, viewportClipped, croppedViewport);
                PlayerUiOrganicVisibility.SetClip(Center, 60f, 2f);
                circleClipped = Capture(largeMesh, material, Color.clear);
                CheckCircleIndependence(large, circleClipped);
                CheckDiamondGlyphShape(scene, material);
                ExportPointBoundaryComparison(scene, material);
                ExportProductionPreview(scene, material);
                ExportSmallGarbageComparison(scene, material);
                CheckOpacityAnimation(scene, material);
                Require(!ShaderUtil.ShaderHasError(shader), "The static-line shader reported errors after rendering.");
                Debug.Log("Static interactable scan render checks PASS: one continuous parallel line family, complete flat endpoints, "
                    + "0.9 px width / 7.7 px normal-view spacing, smooth inset envelope, no deliberate interior gaps, "
                    + "no crossings or clipped fringe segments, stable style at two footprint sizes, viewport "
                    + "clipping, visible gold outside the organic UI circle, and actual-size small-waste "
                    + "interior fill with complete short bars; smooth entrance and breathing alpha "
                    + "on unchanged production-shader geometry; filled four-tipped diamond outline points, "
                    + "clear point intervals, circle-independent outline pixels and production combined previews. Device: "
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
            BioScanStaticLinePattern.Build(hull, Spacing, Angle, lines, Width);
            Require(lines.Count > 15, "The production builder did not create substantial parallel-line coverage.");
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

        private static void CheckParallelGeometry(List<StaticScanLineSegment> lines, float footprintRadius)
        {
            float radians = Angle * Mathf.Deg2Rad;
            Vector2 direction = new(Mathf.Cos(radians), Mathf.Sin(radians));
            Vector2 normal = new(-direction.y, direction.x);
            var rows = new HashSet<int>();
            int longLines = 0;
            foreach (StaticScanLineSegment line in lines)
            {
                Vector2 vector = line.End - line.Start;
                Require(Mathf.Abs(Vector2.Dot(vector.normalized, direction)) > .99999f,
                    "The pattern contains a second direction instead of parallel slanted bars.");
                Require(vector.magnitude >= Mathf.Max(3f, Width * 3f) - .01f,
                    "The pattern contains an incomplete short fringe bar.");
                if (vector.magnitude > Spacing * 5f) longLines++;
                Vector2 center = (line.Start + line.End) * .5f;
                float row = Vector2.Dot(center, normal) / Spacing;
                Require(Mathf.Abs(row - Mathf.Round(row)) < .001f,
                    "The parallel bars did not retain their configured 7.7 px spacing.");
                Require(rows.Add(Mathf.RoundToInt(row)),
                    "A single row was broken into separate bars instead of one continuous line.");
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
            Require(longLines > 10,
                "The production pattern does not contain enough continuous long parallel lines.");
        }

        private static void CheckContinuousInteriors(Texture2D texture, List<StaticScanLineSegment> lines)
        {
            int samples = 0;
            foreach (StaticScanLineSegment line in lines)
            {
                Vector2 vector = line.End - line.Start;
                float length = vector.magnitude;
                Vector2 direction = vector / length;
                for (float offset = 3f; offset < length - 3f; offset += 2f)
                {
                    // A subpixel stroke can fall between pixel centers. Probe
                    // its complete local raster footprint, rather than require
                    // one arbitrarily rounded pixel to contain an opaque core.
                    Require(StrongestSample(texture, Center + line.Start + direction * offset).a > .15f,
                        "A continuous parallel bar contains a visible interior gap.");
                    samples++;
                }
            }
            Require(samples > 1000, "Insufficient continuous line interiors were checked on the GPU.");
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
            // full-image search through independent complete bars.
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
            Vector2 anchor, float width, int minimumInteriorSamples = 500, int minimumEdgeSamples = 500)
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
                if (distance < -.3f)
                {
                    lineSamples++;
                    Require(actual.a > .75f, "A finite subpixel line is missing high-coverage pixels at " + x + "," + y + ".");
                    Require(actual.r > actual.b && actual.g > actual.b, "A line failed to render the production gold tint.");
                }
                else if (distance > 1.2f)
                {
                    gapSamples++;
                    Require(actual.a < .01f,
                        "An edge remnant or width overflow filled an intended gap at " + x + "," + y + ".");
                }
                else if (actual.a > .01f && actual.a < .93f) edgeSamples++;
            }
            Require(lineSamples > minimumInteriorSamples && gapSamples > 150000 && edgeSamples > minimumEdgeSamples,
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
                    Require(StrongestSample(texture, endpoint + anchor - outward * 3f).a > .15f,
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

        private static Color StrongestSample(Texture2D texture, Vector2 point)
        {
            int centerX = Mathf.FloorToInt(point.x), centerY = Mathf.FloorToInt(point.y);
            Color strongest = Color.clear;
            for (int y = centerY - 1; y <= centerY + 1; y++)
            for (int x = centerX - 1; x <= centerX + 1; x++)
            {
                if (x < 0 || x >= Size || y < 0 || y >= Size) continue;
                Color candidate = texture.GetPixel(x, y);
                if (candidate.a > strongest.a) strongest = candidate;
            }
            return strongest;
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
            Mesh lineMesh = null, beforeMesh = null, bodyMesh = null, outlineMesh = null, combinedMesh = null,
                boundedLineMesh = null;
            Material bodyMaterial = null;
            Texture2D preview = null, before = null, pureLines = null, outlinePixels = null,
                clippedOutline = null, combinedPreview = null, secondCombinedPreview = null,
                unboundedPreview = null;
            try
            {
                ScannableInteractable target = instance.GetComponent<ScannableInteractable>();
                Require(target != null, "Large waste has no production scan attribute.");
                target.Reveal();
                typeof(ScannableInteractable).GetMethod("TickReveal", BindingFlags.Instance | BindingFlags.NonPublic)
                    .Invoke(target, new object[] { target.FadeInDuration });
                uint firstSeed = target.OutlinePatternSeed;
                Require(firstSeed != 0, "The production preview's real scan did not create an outline seed.");
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
                    target.LineWidthPixels);
                Require(lines.Count > 0, "The production large-waste preview has no continuous parallel lines.");
                Color currentTint = (Color)typeof(BioScanStaticAreaUI).GetMethod("GetAreaTint",
                    BindingFlags.Static | BindingFlags.NonPublic).Invoke(null, new object[] { target });
                Color outlineTint = (Color)typeof(BioScanStaticAreaUI).GetMethod("GetOutlineTint",
                    BindingFlags.Static | BindingFlags.NonPublic).Invoke(null, new object[] { target });
                lineMesh = MakeAreaMesh(scene, lines, Center, currentTint, target.LineWidthPixels, Viewport);
                var oldLines = new List<StaticScanLineSegment>();
                Require(BioScanStaticLinePattern.Build(canonicalPoints, Spacing, Angle, oldLines,
                    1.4f, default, Mathf.Max(6f, Spacing * 1.25f), 0f),
                    "The old sharp-envelope comparison did not produce bars.");
                Color oldTint = target.AreaColor;
                oldTint.a *= .675f;
                beforeMesh = MakeAreaMesh(scene, oldLines, Center, oldTint, 1.4f, Viewport);
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
                File.WriteAllBytes(Path.Combine(OutputDirectory, "fine-hatch-after.png"), preview.EncodeToPNG());
                before = Capture(beforeMesh, lineMaterial, Color.black, bodyMesh, bodyMaterial);
                File.WriteAllBytes(Path.Combine(OutputDirectory, "fine-hatch-before.png"), before.EncodeToPNG());
                Require(target.ShowOutlineDots && Mathf.Abs(target.OutlineDotSizePixels - 3.6f) < .001f
                    && Mathf.Abs(target.OutlineDotSpacingPixels - 13f) < .001f
                    && Mathf.Abs(target.SmallOutlineDotSpacingPixels - 9f) < .001f,
                    "Production waste must enable the intended small solid diamond outline points.");
                var outlinePoints = new List<Vector2>();
                Require(BioScanStaticOutlinePattern.BuildDots(canonicalPoints, target.OutlineDotSpacingPixels,
                    outlinePoints, target.OutlineDotSizePixels, firstSeed),
                    "The production large-waste outline did not produce diamond point centers.");
                var repeatedPoints = new List<Vector2>();
                Require(BioScanStaticOutlinePattern.BuildDots(canonicalPoints, target.OutlineDotSpacingPixels,
                    repeatedPoints, target.OutlineDotSizePixels, firstSeed)
                    && repeatedPoints.Count == outlinePoints.Count,
                    "Rebuilding the same seeded point layout changed its count.");
                for (int index = 0; index < outlinePoints.Count; index++)
                    Require(outlinePoints[index].Equals(repeatedPoints[index]),
                        "Rebuilding the same seeded outline moved a point center.");
                float diamondSide = target.OutlineDotSizePixels / Mathf.Sqrt(2f);
                List<StaticScanLineSegment> diamondBars = MakeDiamondBars(outlinePoints, target.OutlineDotSizePixels);
                outlineMesh = MakeAreaMesh(scene, diamondBars, Center, target.AreaColor, diamondSide, Viewport);
                PlayerUiOrganicVisibility.DisableClip();
                outlinePixels = Capture(outlineMesh, lineMaterial, Color.clear);
                CheckPatternPixels(outlinePixels, diamondBars, Center, diamondSide, 10, 50);
                CheckOutlineDotGaps(outlinePixels, outlinePoints, diamondBars, diamondSide);
                PlayerUiOrganicVisibility.SetClip(Center, 60f, 2f);
                clippedOutline = Capture(outlineMesh, lineMaterial, Color.clear);
                CheckOutlineCircleIndependence(outlinePixels, clippedOutline);
                File.WriteAllBytes(Path.Combine(OutputDirectory, "diamond-outline-pixel-probe.png"),
                    outlinePixels.EncodeToPNG());
                Object.DestroyImmediate(outlineMesh);
                outlineMesh = MakeAreaMesh(scene, diamondBars, Center, outlineTint, diamondSide, Viewport);
                combinedMesh = CombineMeshes(lineMesh, outlineMesh);
                unboundedPreview = Capture(combinedMesh, lineMaterial, Color.black, bodyMesh, bodyMaterial);
                File.WriteAllBytes(Path.Combine(OutputDirectory, "big-garbage-boundary-before.png"),
                    unboundedPreview.EncodeToPNG());
                Object.DestroyImmediate(combinedMesh);
                List<StaticScanLineSegment> boundedLines = ClipPreviewHatch(lines, outlinePoints, target.LineWidthPixels);
                Require(boundedLines.Count > 0, "The production Big_Garbage point boundary lost all interior hatch.");
                boundedLineMesh = MakeAreaMesh(scene, boundedLines, Center, currentTint, target.LineWidthPixels, Viewport);
                combinedMesh = CombineMeshes(boundedLineMesh, outlineMesh);
                combinedPreview = Capture(combinedMesh, lineMaterial, Color.black, bodyMesh, bodyMaterial);
                byte[] firstPreview = combinedPreview.EncodeToPNG();
                File.WriteAllBytes(Path.Combine(OutputDirectory, "big-garbage-boundary-after.png"), firstPreview);
                File.WriteAllBytes(Path.Combine(OutputDirectory, "big-garbage-hatch-diamonds.png"), firstPreview);
                File.WriteAllBytes(Path.Combine(OutputDirectory, "big-garbage-hatch-outline.png"), firstPreview);
                target.Reveal();
                uint secondSeed = target.OutlinePatternSeed;
                Require(secondSeed != 0 && secondSeed != firstSeed,
                    "A real rescan did not refresh the outline's nonzero arrangement seed.");
                var secondPoints = new List<Vector2>();
                Require(BioScanStaticOutlinePattern.BuildDots(canonicalPoints, target.OutlineDotSpacingPixels,
                    secondPoints, target.OutlineDotSizePixels, secondSeed),
                    "The second real scan failed to build its diamond point centers.");
                bool different = secondPoints.Count != outlinePoints.Count;
                for (int index = 0; !different && index < outlinePoints.Count; index++)
                    different = !outlinePoints[index].Equals(secondPoints[index]);
                Require(different, "Two real scans reused the same diamond point arrangement.");
                Object.DestroyImmediate(outlineMesh);
                Object.DestroyImmediate(combinedMesh);
                Object.DestroyImmediate(boundedLineMesh);
                List<StaticScanLineSegment> secondDiamonds = MakeDiamondBars(secondPoints, target.OutlineDotSizePixels);
                outlineMesh = MakeAreaMesh(scene, secondDiamonds, Center, outlineTint, diamondSide, Viewport);
                List<StaticScanLineSegment> secondBoundedLines = ClipPreviewHatch(lines, secondPoints, target.LineWidthPixels);
                boundedLineMesh = secondBoundedLines.Count > 0 ? MakeAreaMesh(scene, secondBoundedLines, Center,
                    currentTint, target.LineWidthPixels, Viewport) : null;
                combinedMesh = CombineMeshes(boundedLineMesh, outlineMesh);
                secondCombinedPreview = Capture(combinedMesh, lineMaterial, Color.black, bodyMesh, bodyMaterial);
                byte[] secondPreview = secondCombinedPreview.EncodeToPNG();
                File.WriteAllBytes(Path.Combine(OutputDirectory, "big-garbage-hatch-diamonds-second-scan.png"), secondPreview);
                File.WriteAllBytes(Path.Combine(OutputDirectory, "big-garbage-hatch-outline-second-scan.png"), secondPreview);
                Debug.Log("Fine-hatch production comparison exported on the same 400 px Big_Garbage sprite: "
                    + "old sharp envelope 1.4 px / alpha 0.675; new inset-smoothed envelope 0.9 px / alpha "
                    + currentTint.a.ToString("F3") + ". Normal-view spacing remains 7.7 px.");
                Debug.Log("Seeded diamond outline GPU PASS: same-seed exact centers, distinct real scan seeds "
                    + firstSeed + " and " + secondSeed + ", separate hatch/outline layer tints, two combined previews.");
            }
            finally
            {
                if (preview != null) Object.DestroyImmediate(preview);
                if (before != null) Object.DestroyImmediate(before);
                if (pureLines != null) Object.DestroyImmediate(pureLines);
                if (outlinePixels != null) Object.DestroyImmediate(outlinePixels);
                if (clippedOutline != null) Object.DestroyImmediate(clippedOutline);
                if (combinedPreview != null) Object.DestroyImmediate(combinedPreview);
                if (secondCombinedPreview != null) Object.DestroyImmediate(secondCombinedPreview);
                if (unboundedPreview != null) Object.DestroyImmediate(unboundedPreview);
                if (lineMesh != null) Object.DestroyImmediate(lineMesh);
                if (beforeMesh != null) Object.DestroyImmediate(beforeMesh);
                if (outlineMesh != null) Object.DestroyImmediate(outlineMesh);
                if (combinedMesh != null) Object.DestroyImmediate(combinedMesh);
                if (boundedLineMesh != null) Object.DestroyImmediate(boundedLineMesh);
                if (bodyMesh != null) Object.DestroyImmediate(bodyMesh);
                if (bodyMaterial != null) Object.DestroyImmediate(bodyMaterial);
                Object.DestroyImmediate(instance);
            }
        }

        private static Mesh CombineMeshes(Mesh first, Mesh second)
        {
            var combined = new Mesh { hideFlags = HideFlags.HideAndDontSave };
            var pieces = new List<CombineInstance>();
            if (first != null) pieces.Add(new CombineInstance { mesh = first, transform = Matrix4x4.identity });
            if (second != null) pieces.Add(new CombineInstance { mesh = second, transform = Matrix4x4.identity });
            Require(pieces.Count > 0, "A production preview needs at least one visible layer.");
            combined.CombineMeshes(pieces.ToArray(), true, false);
            return combined;
        }

        private static List<StaticScanLineSegment> ClipPreviewHatch(List<StaticScanLineSegment> source,
            List<Vector2> pointCenters, float width)
        {
            var sourceCopy = new List<StaticScanLineSegment>(source);
            var pointsCopy = new List<Vector2>(pointCenters);
            var hull = new List<Vector2>();
            BioScanStaticLinePattern.BuildPointBoundaryHull(pointCenters, hull);
            var clipped = new List<StaticScanLineSegment>();
            BioScanStaticLinePattern.ClipToBoundary(source, hull, clipped, width, 1f, .25f);
            Require(source.Count == sourceCopy.Count && pointCenters.Count == pointsCopy.Count,
                "Clipping a point boundary mutated its cached source count.");
            for (int index = 0; index < source.Count; index++)
                Require(source[index].Start.Equals(sourceCopy[index].Start)
                    && source[index].End.Equals(sourceCopy[index].End),
                    "Clipping a point boundary moved a cached world-source endpoint.");
            for (int index = 0; index < pointCenters.Count; index++)
                Require(pointCenters[index].Equals(pointsCopy[index]),
                    "Building a point boundary reordered or moved its fixed outline centers.");
            return clipped;
        }

        private static float PointHullDistance(Vector2 point, List<Vector2> hull)
        {
            if (hull.Count < 3) return float.NegativeInfinity;
            double area = 0d, minimum = double.PositiveInfinity;
            for (int index = 0; index < hull.Count; index++)
            {
                Vector2 first = hull[index], second = hull[(index + 1) % hull.Count];
                area += (double)first.x * second.y - (double)first.y * second.x;
            }
            double winding = area >= 0d ? 1d : -1d;
            for (int index = 0; index < hull.Count; index++)
            {
                Vector2 start = hull[index], edge = hull[(index + 1) % hull.Count] - start;
                Vector2 offset = point - start;
                minimum = Math.Min(minimum, winding * ((double)edge.x * offset.y - (double)edge.y * offset.x)
                    / edge.magnitude);
            }
            return (float)minimum;
        }

        private static void ExportPointBoundaryComparison(Scene scene, Material material)
        {
            var sourceFootprint = new List<Vector2> { new(-112f, -94f), new(112f, -94f),
                new(112f, 94f), new(-112f, 94f) };
            var centers = new List<Vector2> { new(-72f, -30f), new(-45f, -65f), new(42f, -58f),
                new(75f, -15f), new(50f, 53f), new(-38f, 61f) };
            var source = new List<StaticScanLineSegment>();
            BioScanStaticLinePattern.Build(sourceFootprint, Spacing, Angle, source, Width);
            List<StaticScanLineSegment> clipped = ClipPreviewHatch(source, centers, Width);
            Require(clipped.Count > 5, "The point-boundary diagnostic lost its measurable hatch interior.");
            var hull = new List<Vector2>();
            BioScanStaticLinePattern.BuildPointBoundaryHull(centers, hull);
            Require(hull.Count >= 3, "The diagnostic point centers did not define their real area boundary.");
            foreach (StaticScanLineSegment line in clipped)
            {
                Vector2 direction = (line.End - line.Start).normalized, normal = new(-direction.y, direction.x);
                foreach (float end in new[] { -1f, 1f })
                foreach (float side in new[] { -1f, 1f })
                {
                    Vector2 cap = end < 0f ? line.Start : line.End;
                    Vector2 corner = cap + direction * end + normal * ((Width * .5f + 1f) * side);
                    Require(PointHullDistance(corner, hull) >= .98f,
                        "A shortened hatch lost its full AA rectangle or Euclidean 1 px boundary clearance.");
                }
            }
            Require(ClipPreviewHatch(source, new List<Vector2> { Vector2.left, Vector2.right }, Width).Count == 0,
                "A two-point boundary must retain its dots while omitting the unsupported hatch interior.");
            Mesh originalMesh = null, clippedMesh = null, dotsMesh = null, beforeMesh = null, afterMesh = null;
            Texture2D originalPixels = null, clippedPixels = null, before = null, after = null;
            try
            {
                originalMesh = MakeAreaMesh(scene, source, Center, Gold, Width, Viewport);
                clippedMesh = MakeAreaMesh(scene, clipped, Center, Gold, Width, Viewport);
                List<StaticScanLineSegment> glyphs = MakeDiamondBars(centers, 3.6f);
                dotsMesh = MakeAreaMesh(scene, glyphs, Center, Gold, 3.6f / Mathf.Sqrt(2f), Viewport);
                originalPixels = Capture(originalMesh, material, Color.clear);
                clippedPixels = Capture(clippedMesh, material, Color.clear);
                CheckPatternPixels(clippedPixels, clipped, Center, Width, 10, 50);
                Color[] oldPixels = originalPixels.GetPixels(), newPixels = clippedPixels.GetPixels();
                int protrudingBefore = 0, retainedPixels = 0;
                for (int index = 0; index < newPixels.Length; index++)
                {
                    if (oldPixels[index].a <= .2f && newPixels[index].a <= .01f) continue;
                    Vector2 point = new Vector2(index % Size + .5f, index / Size + .5f) - Center;
                    float distance = PointHullDistance(point, hull);
                    if (oldPixels[index].a > .2f && distance < 0f) protrudingBefore++;
                    if (newPixels[index].a <= .01f) continue;
                    Require(distance >= .98f, "A rendered hatch tip still protrudes through its diamond-center boundary.");
                    retainedPixels++;
                }
                Require(protrudingBefore > 100 && retainedPixels > 500,
                    "The before/after diagnostic did not expose enough old protruding tips and retained inner hatch.");
                beforeMesh = CombineMeshes(originalMesh, dotsMesh);
                afterMesh = CombineMeshes(clippedMesh, dotsMesh);
                before = Capture(beforeMesh, material, Color.black);
                after = Capture(afterMesh, material, Color.black);
                File.WriteAllBytes(Path.Combine(OutputDirectory, "point-boundary-before.png"), before.EncodeToPNG());
                File.WriteAllBytes(Path.Combine(OutputDirectory, "point-boundary-after.png"), after.EncodeToPNG());
                Debug.Log("Point-boundary GPU PASS: " + protrudingBefore + " old protruding hatch pixels removed; "
                    + "complete square caps/AA and 1 px clearance inside the actual diamond-center hull, "
                    + "unchanged cached sources and a dots-only fallback for degenerate boundaries.");
            }
            finally
            {
                if (originalMesh != null) Object.DestroyImmediate(originalMesh);
                if (clippedMesh != null) Object.DestroyImmediate(clippedMesh);
                if (dotsMesh != null) Object.DestroyImmediate(dotsMesh);
                if (beforeMesh != null) Object.DestroyImmediate(beforeMesh);
                if (afterMesh != null) Object.DestroyImmediate(afterMesh);
                if (originalPixels != null) Object.DestroyImmediate(originalPixels);
                if (clippedPixels != null) Object.DestroyImmediate(clippedPixels);
                if (before != null) Object.DestroyImmediate(before);
                if (after != null) Object.DestroyImmediate(after);
            }
        }

        private static List<StaticScanLineSegment> MakeDiamondBars(List<Vector2> points, float diagonalSize)
        {
            var glyphs = new List<StaticScanLineSegment>();
            BioScanStaticOutlinePattern.BuildDiamondSegments(points, Vector2.one.normalized, diagonalSize, glyphs);
            Require(glyphs.Count == points.Count && glyphs.Count > 0,
                "Each outline point must produce one complete filled diamond glyph.");
            float side = diagonalSize / Mathf.Sqrt(2f);
            for (int index = 0; index < glyphs.Count; index++)
            {
                Require(Mathf.Abs(Vector2.Distance(glyphs[index].Start, glyphs[index].End) - side) < .001f,
                    "A diamond's square side changed the configured tip-to-tip pixel size.");
                Require(((glyphs[index].Start + glyphs[index].End) * .5f - points[index]).sqrMagnitude < .000001f,
                    "Converting an outline point to a diamond moved its center.");
            }
            return glyphs;
        }

        private static void CheckOutlineDotGaps(Texture2D pixels, List<Vector2> centers,
            List<StaticScanLineSegment> glyphs, float width)
        {
            Require(centers.Count > 10, "The outline point probe needs enough diamonds and gaps.");
            float[] distances = RasterDistances(glyphs, Center, width);
            int gapSamples = 0;
            for (int index = 0; index < centers.Count; index++)
            {
                Require(StrongestSample(pixels, Center + centers[index]).a > .75f,
                    "A production outline diamond failed to render its filled body.");
                Vector2 midpoint = Center + (centers[index] + centers[(index + 1) % centers.Count]) * .5f;
                int x = Mathf.FloorToInt(midpoint.x), y = Mathf.FloorToInt(midpoint.y);
                if (x < 0 || x >= Size || y < 0 || y >= Size) continue;
                if (distances[y * Size + x] <= .75f) continue;
                Require(pixels.GetPixel(x, y).a < .01f, "A diamond point interval was filled on the GPU.");
                gapSamples++;
            }
            Require(gapSamples > 10, "The production outline did not expose enough clear intervals between diamonds.");
        }

        private static void CheckDiamondGlyphShape(Scene scene, Material material)
        {
            foreach (float size in new[] { 3.6f, 18f })
            {
                Mesh mesh = null;
                Texture2D pixels = null;
                try
                {
                    // The actual-size SDF probe guards the shipped glyph. The
                    // enlarged diagnostic separates four sharp tips from a disk
                    // without depending on subpixel rounding of a 3.6 px asset.
                    List<StaticScanLineSegment> glyphs = MakeDiamondBars(new List<Vector2> { Vector2.zero }, size);
                    Vector2 anchor = size > 4f ? Center + Vector2.one * .5f : Center;
                    float side = size / Mathf.Sqrt(2f);
                    mesh = MakeAreaMesh(scene, glyphs, anchor, Gold, side, Viewport);
                    pixels = Capture(mesh, material, Color.clear);
                    CheckPatternPixels(pixels, glyphs, anchor, side, 0, 0);
                    Require(StrongestSample(pixels, anchor).a > .95f,
                        "The diamond must have a solid filled center rather than a hollow ring.");
                    if (size > 4f)
                    {
                        foreach (Vector2 cardinal in new[] { Vector2.right, Vector2.left, Vector2.up, Vector2.down })
                            Require(Sample(pixels, anchor + cardinal * 8f).a > .9f,
                                "A diamond lost one of its four cardinal tips.");
                        foreach (Vector2 corner in new[] { new Vector2(6f, 6f), new Vector2(-6f, 6f),
                            new Vector2(-6f, -6f), new Vector2(6f, -6f) })
                        {
                            Require(corner.magnitude < size * .5f - .5f,
                                "The shape counterexample must lie inside a same-radius filled circle.");
                            Require(Sample(pixels, anchor + corner).a < .01f,
                                "The point rendered as a circular dot instead of a four-tipped diamond.");
                        }
                    }
                    else
                    {
                        // These actual-size shoulder pixels are strongly filled
                        // by a radius-1.8 circle, but only AA fringe for a diamond.
                        foreach (Vector2 shoulder in new[] { new Vector2(1.5f, .5f), new Vector2(-1.5f, -.5f),
                            new Vector2(.5f, 1.5f), new Vector2(-.5f, -1.5f) })
                            Require(Sample(pixels, anchor + shoulder).a < .15f,
                                "The actual 3.6 px outline glyph became round instead of diamond-shaped.");
                    }
                }
                finally
                {
                    if (mesh != null) Object.DestroyImmediate(mesh);
                    if (pixels != null) Object.DestroyImmediate(pixels);
                }
            }
            Debug.Log("Outline diamond glyph GPU PASS: actual 3.6 px square SDF, filled center, "
                + "four cardinal tips and transparent diagonal shoulders that distinguish it from a circle.");
        }

        private static void CheckOutlineCircleIndependence(Texture2D before, Texture2D after)
        {
            Color[] original = before.GetPixels(), clipped = after.GetPixels();
            int outside = 0;
            for (int y = 0; y < Size; y++)
            for (int x = 0; x < Size; x++)
            {
                int index = y * Size + x;
                Require(Mathf.Abs(original[index].a - clipped[index].a) < .01f,
                    "The organic UI circle clipped a production outline diamond.");
                if ((new Vector2(x + .5f, y + .5f) - Center).sqrMagnitude > 80f * 80f
                    && clipped[index].a > .05f) outside++;
            }
            Require(outside > 100, "Too few actual outline pixels remained outside the circular player view.");
            Debug.Log("Diamond outline GPU checks PASS: production shader square SDF/AA, visible filled points, "
                + "transparent intervals and " + outside + " unchanged outline pixels outside the organic UI circle.");
        }

        private static void ExportSmallGarbageComparison(Scene scene, Material lineMaterial)
        {
            const float referencePixelsPerWorldUnit = 60f;
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(
                "Assets/Prefabs/Environment/Garbage/Small_Garbage.prefab");
            Require(prefab != null, "The production small-waste prefab is missing.");
            GameObject instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab, scene);
            Mesh beforeMesh = null, afterMesh = null, bodyMesh = null, diamondMesh = null, combinedMesh = null,
                boundedSmallMesh = null;
            Material bodyMaterial = null;
            Texture2D before = null, after = null, linePixels = null, diamondPixels = null, combinedPreview = null,
                unboundedSmallPreview = null;
            try
            {
                ScannableInteractable target = instance.GetComponent<ScannableInteractable>();
                Require(target != null && target.UseSmallAreaFill,
                    "Small waste has not enabled its separate interior-fill presentation.");
                Require(Mathf.Abs(target.LineWidthPixels - Width) < .001f
                    && Mathf.Abs(target.LineSpacingPixels - Spacing) < .001f
                    && Mathf.Abs(target.SmallAreaLineSpacingPixels - 4f) < .001f,
                    "Small waste changed its line width or the shared medium/large style.");
                var footprint = new List<Vector2>();
                Require(target.TryGetPresentationFootprint(footprint, out Vector3 worldCenter),
                    "Small waste has no production presentation envelope.");
                var referenceFootprint = new List<Vector2>();
                Vector2 minimum = new(float.PositiveInfinity, float.PositiveInfinity);
                Vector2 maximum = new(float.NegativeInfinity, float.NegativeInfinity);
                foreach (Vector2 point in footprint)
                {
                    Vector2 referencePoint = point * referencePixelsPerWorldUnit;
                    referenceFootprint.Add(referencePoint);
                    minimum = Vector2.Min(minimum, referencePoint);
                    maximum = Vector2.Max(maximum, referencePoint);
                }
                float referenceSize = Mathf.Max(maximum.x - minimum.x, maximum.y - minimum.y);
                Require(referenceSize > 20f && referenceSize < 40f,
                    "Small-waste preview must use its real roughly 29 px normal-view size, rather than enlarge the layout.");
                var legacyLines = new List<StaticScanLineSegment>();
                var improvedLines = new List<StaticScanLineSegment>();
                const float legacyWidth = 1.4f;
                Require(BioScanStaticLinePattern.Build(referenceFootprint, target.LineSpacingPixels,
                    target.LineAngleDegrees, legacyLines, legacyWidth,
                    -(Vector2)worldCenter * referencePixelsPerWorldUnit,
                    Mathf.Max(6f, target.LineSpacingPixels * 1.25f), 0f),
                    "Could not build the small-waste legacy comparison.");
                Require(BioScanStaticLinePattern.BuildSmallArea(referenceFootprint,
                    target.SmallAreaLineSpacingPixels, target.LineAngleDegrees, improvedLines, target.LineWidthPixels),
                    "Could not build the small-waste interior-fill comparison.");
                Require(improvedLines.Count >= 3 && improvedLines.Count <= 5
                    && improvedLines.Count > legacyLines.Count,
                    "Actual-size small waste should gain 3–5 readable complete bars instead of its sparse legacy fill. "
                    + "Before: " + legacyLines.Count + "; after: " + improvedLines.Count + ".");
                float radians = target.LineAngleDegrees * Mathf.Deg2Rad;
                Vector2 direction = new(Mathf.Cos(radians), Mathf.Sin(radians));
                foreach (StaticScanLineSegment line in improvedLines)
                {
                    Vector2 vector = line.End - line.Start;
                    Require(vector.magnitude >= 4f - .01f,
                        "Small-waste interior fill retained an unreadable fringe fragment.");
                    Require(Mathf.Abs(Vector2.Dot(vector.normalized, direction)) > .99999f,
                        "Small-waste interior fill changed the existing parallel direction.");
                }
                afterMesh = MakeAreaMesh(scene, improvedLines, Center, target.AreaColor, target.LineWidthPixels, Viewport);
                linePixels = Capture(afterMesh, lineMaterial, Color.clear);
                CheckPatternPixels(linePixels, improvedLines, Center, target.LineWidthPixels, 2, 20);
                CheckSmallCompleteCaps(linePixels, improvedLines);
                // Pixel coverage above uses alpha one to isolate width and AA.
                // The human-facing crops use the squared-cosine hatch's cycle
                // mean (3/8), so a still image represents its quiet average.
                Object.DestroyImmediate(afterMesh);
                Color previewTint = target.AreaColor;
                previewTint.a *= Mathf.Lerp(target.BreathingMinAlpha, target.BreathingMaxAlpha, .375f);
                Color legacyTint = target.AreaColor;
                legacyTint.a *= .675f;
                beforeMesh = MakeAreaMesh(scene, legacyLines, Center, legacyTint, legacyWidth, Viewport);
                afterMesh = MakeAreaMesh(scene, improvedLines, Center, previewTint, target.LineWidthPixels, Viewport);
                WorldInteraction interaction = instance.GetComponent<WorldInteraction>();
                SpriteRenderer renderer = interaction != null ? interaction.SpriteSource : null;
                Require(renderer != null && renderer.sprite != null,
                    "Production small waste has no body sprite for its actual-size comparison.");
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
                    positions[i] = (world - (Vector2)worldCenter) * referencePixelsPerWorldUnit + Center;
                    colors[i] = renderer.color;
                }
                ushort[] spriteTriangles = sprite.triangles;
                var triangles = new int[spriteTriangles.Length];
                for (int i = 0; i < triangles.Length; i++) triangles[i] = spriteTriangles[i];
                bodyMesh = new Mesh { hideFlags = HideFlags.HideAndDontSave };
                bodyMesh.vertices = positions; bodyMesh.uv = sprite.uv;
                bodyMesh.colors32 = colors; bodyMesh.triangles = triangles;
                Shader bodyShader = Shader.Find("Sprites/Default");
                Require(bodyShader != null && bodyShader.isSupported,
                    "Could not render the actual-size small-waste comparison sprite.");
                bodyMaterial = new Material(bodyShader) { hideFlags = HideFlags.HideAndDontSave };
                bodyMaterial.mainTexture = sprite.texture;
                before = Capture(beforeMesh, lineMaterial, Color.black, bodyMesh, bodyMaterial);
                after = Capture(afterMesh, lineMaterial, Color.black, bodyMesh, bodyMaterial);
                ExportSmallCrop(before, "small-garbage-before");
                ExportSmallCrop(after, "small-garbage-after");
                Require(target.ShowOutlineDots && Mathf.Abs(target.SmallOutlineDotSpacingPixels - 9f) < .001f,
                    "Actual small waste must enable its separate diamond point outline spacing.");
                target.Reveal();
                var smallOutlinePoints = new List<Vector2>();
                Require(BioScanStaticOutlinePattern.BuildDots(referenceFootprint, target.SmallOutlineDotSpacingPixels,
                    smallOutlinePoints, target.OutlineDotSizePixels, target.OutlinePatternSeed)
                    && smallOutlinePoints.Count >= 3,
                    "Actual-size small waste did not produce readable outline diamond point centers.");
                float diamondSide = target.OutlineDotSizePixels / Mathf.Sqrt(2f);
                List<StaticScanLineSegment> smallDiamonds = MakeDiamondBars(smallOutlinePoints, target.OutlineDotSizePixels);
                diamondMesh = MakeAreaMesh(scene, smallDiamonds, Center, target.AreaColor, diamondSide, Viewport);
                diamondPixels = Capture(diamondMesh, lineMaterial, Color.clear);
                CheckPatternPixels(diamondPixels, smallDiamonds, Center, diamondSide, 2, 20);
                Object.DestroyImmediate(diamondMesh);
                Color outlineTint = target.AreaColor;
                outlineTint.a *= (target.OutlineBreathingMinAlpha + target.OutlineBreathingMaxAlpha) * .5f;
                diamondMesh = MakeAreaMesh(scene, smallDiamonds, Center, outlineTint, diamondSide, Viewport);
                combinedMesh = CombineMeshes(afterMesh, diamondMesh);
                unboundedSmallPreview = Capture(combinedMesh, lineMaterial, Color.black, bodyMesh, bodyMaterial);
                ExportSmallCrop(unboundedSmallPreview, "small-garbage-boundary-before");
                Object.DestroyImmediate(combinedMesh);
                List<StaticScanLineSegment> boundedSmall = ClipPreviewHatch(improvedLines, smallOutlinePoints,
                    target.LineWidthPixels);
                boundedSmallMesh = boundedSmall.Count > 0 ? MakeAreaMesh(scene, boundedSmall, Center,
                    previewTint, target.LineWidthPixels, Viewport) : null;
                combinedMesh = CombineMeshes(boundedSmallMesh, diamondMesh);
                combinedPreview = Capture(combinedMesh, lineMaterial, Color.black, bodyMesh, bodyMaterial);
                ExportSmallCrop(combinedPreview, "small-garbage-diamonds");
                ExportSmallCrop(combinedPreview, "small-garbage-boundary-after");
                Debug.Log("Actual-size small waste GPU fill PASS: " + referenceSize.ToString("F2")
                    + " px envelope at 60 px/world unit, legacy " + legacyLines.Count + " bars → "
                    + improvedLines.Count + " complete bars, production 0.9 px width and subtle gold tint. "
                    + "Legacy baseline explicitly uses 1.4 px width, old minimum length and an unsmoothed envelope. "
                    + "8× previews enlarge the rendered pixels, never the generated layout.");
            }
            finally
            {
                if (before != null) Object.DestroyImmediate(before);
                if (after != null) Object.DestroyImmediate(after);
                if (linePixels != null) Object.DestroyImmediate(linePixels);
                if (diamondPixels != null) Object.DestroyImmediate(diamondPixels);
                if (combinedPreview != null) Object.DestroyImmediate(combinedPreview);
                if (unboundedSmallPreview != null) Object.DestroyImmediate(unboundedSmallPreview);
                if (beforeMesh != null) Object.DestroyImmediate(beforeMesh);
                if (afterMesh != null) Object.DestroyImmediate(afterMesh);
                if (bodyMesh != null) Object.DestroyImmediate(bodyMesh);
                if (diamondMesh != null) Object.DestroyImmediate(diamondMesh);
                if (combinedMesh != null) Object.DestroyImmediate(combinedMesh);
                if (boundedSmallMesh != null) Object.DestroyImmediate(boundedSmallMesh);
                if (bodyMaterial != null) Object.DestroyImmediate(bodyMaterial);
                Object.DestroyImmediate(instance);
            }
        }

        private static void CheckSmallCompleteCaps(Texture2D texture, List<StaticScanLineSegment> lines)
        {
            foreach (StaticScanLineSegment line in lines)
            {
                Vector2 vector = line.End - line.Start;
                Vector2 direction = vector.normalized;
                Require(StrongestSample(texture, Center + (line.Start + line.End) * .5f).a > .15f,
                    "A short small-waste bar lost its readable interior.");
                foreach (Vector2 endpoint in new[] { line.Start, line.End })
                {
                    Vector2 outward = endpoint == line.Start ? -direction : direction;
                    Require(StrongestSample(texture, Center + endpoint - outward).a > .15f,
                        "A short small-waste bar lost a complete flat end.");
                    Require(Sample(texture, Center + endpoint + outward * 2f).a < .01f,
                        "A short small-waste bar extended beyond its flat end.");
                }
            }
        }

        private static void CheckOpacityAnimation(Scene scene, Material material)
        {
            CheckLayerOpacityAnimation(scene, material, false);
            CheckLayerOpacityAnimation(scene, material, true);
        }

        private static void CheckLayerOpacityAnimation(Scene scene, Material material, bool forOutline)
        {
            MethodInfo tick = typeof(ScannableInteractable).GetMethod("TickReveal",
                BindingFlags.Instance | BindingFlags.NonPublic);
            MethodInfo getTint = typeof(BioScanStaticAreaUI).GetMethod(forOutline ? "GetOutlineTint" : "GetAreaTint",
                BindingFlags.Static | BindingFlags.NonPublic);
            Require(tick != null && getTint != null,
                "The production reveal clock and separate layer tint are required for opacity pixel checks.");
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(
                "Assets/Prefabs/Environment/Garbage/Small_Garbage.prefab");
            Require(prefab != null, "Opacity checks require a production scan target.");
            GameObject instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab, scene);
            string layer = forOutline ? "outline" : "hatch";
            try
            {
                ScannableInteractable target = instance.GetComponent<ScannableInteractable>();
                Require(target != null, "Opacity checks require the production static scan attribute.");
                Require(Mathf.Abs(target.FadeInDuration - .65f) < .001f
                    && Mathf.Abs(target.BreathingPeriod - 2f) < .001f
                    && Mathf.Abs(target.BreathingMinAlpha - .14f) < .001f
                    && Mathf.Abs(target.BreathingMaxAlpha - .62f) < .001f
                    && Mathf.Abs(target.OutlineBreathingPeriod - 2.4f) < .001f
                    && Mathf.Abs(target.OutlineBreathingMinAlpha - .42f) < .001f
                    && Mathf.Abs(target.OutlineBreathingMaxAlpha - .5f) < .001f,
                    "The production garbage layer breathing parameters changed unexpectedly.");
                target.Reveal();
                uint seed = target.OutlinePatternSeed;
                Require(seed != 0, "A real scan did not seed its outline arrangement.");
                List<StaticScanLineSegment> lines = MakePattern(106f);
                float width = Width;
                if (forOutline)
                {
                    width = target.OutlineDotSizePixels / Mathf.Sqrt(2f);
                    var hull = new List<Vector2> { new(-106f, -106f), new(106f, -106f),
                        new(106f, 106f), new(-106f, 106f) };
                    var points = new List<Vector2>();
                    Require(BioScanStaticOutlinePattern.BuildDots(hull, target.OutlineDotSpacingPixels,
                        points, target.OutlineDotSizePixels, seed),
                        "The layer opacity fixture could not build seeded diamond outline points.");
                    lines = MakeDiamondBars(points, target.OutlineDotSizePixels);
                }
                float[] distances = RasterDistances(lines, Center, width);
                float[] times = forOutline
                    ? new[] { 0f, .325f, .65f, 1.2f, 2.4f }
                    : new[] { 0f, .325f, .65f, 1.65f, 2.65f };
                const int valley = 3, peak = 4;
                var coreAlpha = new float[times.Length];
                var tints = new Color[times.Length];
                Vector3[] geometry = null;
                float previousTime = 0f;
                for (int frame = 0; frame < times.Length; frame++)
                {
                    tick.Invoke(target, new object[] { times[frame] - previousTime });
                    previousTime = times[frame];
                    Require(Mathf.Abs(target.RevealElapsed - times[frame]) < .0001f
                        && target.OutlinePatternSeed == seed,
                        "Opacity animation changed the reveal clock or regenerated its seeded pattern.");
                    Color tint = (Color)getTint.Invoke(null, new object[] { target });
                    tints[frame] = tint;
                    float age = forOutline ? times[frame] : Mathf.Max(0f, times[frame] - target.FadeInDuration);
                    float period = forOutline ? target.OutlineBreathingPeriod : target.BreathingPeriod;
                    float pulse = .5f + .5f * Mathf.Cos(Mathf.Repeat(age, period) / period * Mathf.PI * 2f);
                    if (!forOutline) pulse *= pulse;
                    float minimum = forOutline ? target.OutlineBreathingMinAlpha : target.BreathingMinAlpha;
                    float maximum = forOutline ? target.OutlineBreathingMaxAlpha : target.BreathingMaxAlpha;
                    float entrance = Mathf.SmoothStep(0f, 1f, times[frame] / target.FadeInDuration);
                    float expectedAlpha = target.AreaColor.a * entrance * Mathf.Lerp(minimum, maximum, pulse);
                    Require(Mathf.Abs(tint.a - expectedAlpha) < .0001f,
                        layer + " used the wrong independent breathing clock, curve or alpha bounds at t=" + times[frame]);
                    Mesh mesh = null;
                    Texture2D pixels = null, preview = null;
                    try
                    {
                        mesh = MakeAreaMesh(scene, lines, Center, tint, width, Viewport);
                        Vector3[] vertices = mesh.vertices;
                        if (geometry == null) geometry = vertices;
                        else
                        {
                            Require(vertices.Length == geometry.Length,
                                "The opacity animation changed the number of layer vertices.");
                            for (int vertex = 0; vertex < vertices.Length; vertex++)
                                Require((vertices[vertex] - geometry[vertex]).sqrMagnitude < .000001f,
                                    "The opacity animation moved a layer endpoint instead of only changing alpha.");
                        }
                        pixels = Capture(mesh, material, Color.clear);
                        float gpuTintAlpha = mesh.colors32[0].a / 255f;
                        Color[] rendered = pixels.GetPixels();
                        int coreSamples = 0;
                        float sum = 0f;
                        for (int index = 0; index < rendered.Length; index++)
                        {
                            float coverage = 1f - Mathf.SmoothStep(0f, 1f,
                                Mathf.InverseLerp(-.5f, .5f, distances[index]));
                            float sourceAlpha = gpuTintAlpha * coverage;
                            Require(Mathf.Abs(rendered[index].a - sourceAlpha * sourceAlpha) < .035f,
                                layer + " pixels disagree with the layer-specific quantized tint and SDF at t=" + times[frame]);
                            if (distances[index] < -.3f) { coreSamples++; sum += rendered[index].a; }
                        }
                        Require(coreSamples > (forOutline ? 10 : 500),
                            "Insufficient high-coverage pixels were sampled for the " + layer + " animation.");
                        coreAlpha[frame] = sum / coreSamples;
                        preview = Capture(mesh, material, Color.black);
                        byte[] png = preview.EncodeToPNG();
                        File.WriteAllBytes(Path.Combine(OutputDirectory, "static-scan-" + layer + "-opacity-"
                            + Mathf.RoundToInt(times[frame] * 1000f) + "ms.png"), png);
                        if (frame == valley)
                            File.WriteAllBytes(Path.Combine(OutputDirectory, "static-scan-" + layer + "-breath-valley.png"), png);
                        if (frame == peak)
                            File.WriteAllBytes(Path.Combine(OutputDirectory, "static-scan-" + layer + "-breath-peak.png"), png);
                    }
                    finally
                    {
                        if (pixels != null) Object.DestroyImmediate(pixels);
                        if (preview != null) Object.DestroyImmediate(preview);
                        if (mesh != null) Object.DestroyImmediate(mesh);
                    }
                }
                Require(tints[0].a < .001f && coreAlpha[0] < .001f,
                    "A freshly scanned layer appeared before its entrance.");
                Require(tints[1].a > .05f && tints[1].a < tints[2].a
                    && coreAlpha[1] > .01f && coreAlpha[1] < coreAlpha[2] - .05f,
                    "The layer did not show a smooth intermediate entrance before its first full appearance.");
                float expectedMinimum = forOutline ? target.OutlineBreathingMinAlpha : target.BreathingMinAlpha;
                float expectedMaximum = forOutline ? target.OutlineBreathingMaxAlpha : target.BreathingMaxAlpha;
                Require(Mathf.Abs(tints[valley].a - target.AreaColor.a * expectedMinimum) < .001f
                    && Mathf.Abs(tints[peak].a - target.AreaColor.a * expectedMaximum) < .001f
                    && coreAlpha[peak] > coreAlpha[valley] + (forOutline ? .04f : .24f),
                    "The " + layer + " did not render its separate intended peak/valley opacity range.");
                Debug.Log("Static scan " + layer + " opacity GPU PASS: smooth entrance; "
                    + (forOutline ? "independent 2.4 s soft cosine, alpha .42-.50" : "2.0 s squared cosine after .65 s entrance, alpha .14-.62")
                    + "; exact layer tint/SDF pixels and unchanged vertices/seed. Previews: " + Path.GetFullPath(OutputDirectory));
            }
            finally { Object.DestroyImmediate(instance); }
        }

        private static void ExportSmallCrop(Texture2D source, string name)
        {
            const int cropSize = 80, zoom = 8;
            Texture2D crop = null, magnified = null;
            try
            {
                // Keep the actual 60 px/world-unit render. Magnification only
                // copies already rendered pixels so it cannot create extra bars.
                Color[] pixels = source.GetPixels((Size - cropSize) / 2, (Size - cropSize) / 2, cropSize, cropSize);
                crop = new Texture2D(cropSize, cropSize, TextureFormat.RGBA32, false);
                crop.SetPixels(pixels); crop.Apply();
                File.WriteAllBytes(Path.Combine(OutputDirectory, name + ".png"), crop.EncodeToPNG());
                var enlarged = new Color[cropSize * zoom * cropSize * zoom];
                int side = cropSize * zoom;
                for (int y = 0; y < side; y++)
                for (int x = 0; x < side; x++) enlarged[y * side + x] = pixels[y / zoom * cropSize + x / zoom];
                magnified = new Texture2D(side, side, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point };
                magnified.SetPixels(enlarged); magnified.Apply();
                File.WriteAllBytes(Path.Combine(OutputDirectory, name + "-8x.png"), magnified.EncodeToPNG());
            }
            finally
            {
                if (crop != null) Object.DestroyImmediate(crop);
                if (magnified != null) Object.DestroyImmediate(magnified);
            }
        }

        private static void Require(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException(message);
        }
    }
}
