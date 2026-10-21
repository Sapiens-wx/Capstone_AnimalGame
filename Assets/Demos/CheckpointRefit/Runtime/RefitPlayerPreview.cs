using System;
using System.Collections.Generic;
using AnimalGame.RobotMap;
using AnimalGame.World;
using UnityEngine;
using UnityEngine.Rendering;

namespace AnimalGame.Demos.CheckpointRefit
{
    /// <summary>
    /// Visual-only snapshot of the bound player's initialized artwork. No player
    /// object, gameplay behaviour, camera settings, or shared material is changed.
    /// Capture before taking the gameplay lease, which retracts the player's arms.
    /// </summary>
    public sealed class RefitPlayerPreview : IDisposable
    {
        private const int Resolution = 1536;
        private const int CircleSegments = 512;
        private readonly List<DrawItem> items = new List<DrawItem>();
        private RenderTexture texture;

        public RenderTexture Texture => texture;
        public bool HasImage { get; private set; }
        public int CapturedRendererCount => items.Count;
        public int ReconstructedBodyRendererCount { get; private set; }
        public int ExcludedDirectionIndicatorCount { get; private set; }

        public bool Capture(RobotMover player)
        {
            Clear();
            if (player == null || !player.gameObject.activeInHierarchy) return false;
            RobotMarkerView marker = player.GetComponent<RobotMarkerView>();
            Transform visualRoot = marker != null ? marker.MarkerVisualRoot : null;
            if (visualRoot == null) return false;
            Shader shader = Shader.Find("Sprites/Default");
            if (shader == null || !shader.isSupported) return false;

            // Keep the real pose and proportions, facing up in the inspection
            // frame regardless of the player's heading in the world.
            Matrix4x4 worldToPreview = Matrix4x4.TRS(visualRoot.position,
                Quaternion.Euler(0f, 0f, player.transform.eulerAngles.z), Vector3.one).inverse;
            GameObject bakeCameraObject = null;
            try
            {
                foreach (SpriteRenderer source in visualRoot.GetComponentsInChildren<SpriteRenderer>(false))
                {
                    if (IsDirectionIndicator(source.transform, visualRoot))
                    {
                        ExcludedDirectionIndicatorCount++;
                        continue;
                    }
                    if (!ShouldCapture(source) || source.sprite == null || source.color.a <= 0f) continue;
                    CaptureSprite(source, worldToPreview, shader);
                }

                Camera bakeCamera = null;
                foreach (LineRenderer source in visualRoot.GetComponentsInChildren<LineRenderer>(false))
                {
                    if (IsDirectionIndicator(source.transform, visualRoot)) continue;
                    if (!ShouldCapture(source) || source.positionCount < 2) continue;
                    if (bakeCamera == null)
                    {
                        // BakeMesh needs a viewing direction, but this camera
                        // never renders, has no audio listener, and stays inactive.
                        bakeCameraObject = new GameObject("Refit line snapshot camera")
                        {
                            hideFlags = HideFlags.HideAndDontSave
                        };
                        bakeCameraObject.SetActive(false);
                        bakeCamera = bakeCameraObject.AddComponent<Camera>();
                        bakeCamera.enabled = false;
                        bakeCamera.orthographic = true;
                        bakeCamera.transform.SetPositionAndRotation(
                            visualRoot.position + Vector3.back * 10f, Quaternion.identity);
                    }
                    CaptureLine(source, worldToPreview, shader, bakeCamera);
                }

                if (items.Count == 0) return false;
                items.Sort(CompareDrawOrder);
                DrawSnapshot();
                HasImage = true;
                return true;
            }
            catch (Exception exception)
            {
                Clear();
                Debug.LogWarning("Refit unit preview could not capture the player: " + exception.Message, player);
                return false;
            }
            finally
            {
                DestroyOwned(bakeCameraObject);
            }
        }

        private static bool ShouldCapture(Renderer source)
        {
            // Held or nested world objects are never part of the unit artwork.
            return source != null && source.enabled && !source.forceRenderingOff
                && source.gameObject.activeInHierarchy
                && source.GetComponentInParent<WorldInteraction>() == null;
        }

        private static bool IsDirectionIndicator(Transform source, Transform visualRoot)
        {
            // Heading is a navigation overlay, not a fitted part of the unit.
            for (Transform node = source; node != null && node != visualRoot; node = node.parent)
                if (node.name == "Direction Indicator") return true;
            return false;
        }

        private void CaptureSprite(SpriteRenderer source, Matrix4x4 worldToPreview, Shader shader)
        {
            Sprite sprite = source.sprite;
            if (TryCaptureCircularBody(source, worldToPreview, shader)) return;
            Vector2[] sourceVertices = sprite.vertices;
            if (sourceVertices.Length == 0) return;
            Vector3[] vertices = new Vector3[sourceVertices.Length];
            Color[] colors = new Color[sourceVertices.Length];
            Matrix4x4 matrix = worldToPreview * source.transform.localToWorldMatrix;
            for (int index = 0; index < vertices.Length; index++)
            {
                Vector2 vertex = sourceVertices[index];
                if (source.flipX) vertex.x = -vertex.x;
                if (source.flipY) vertex.y = -vertex.y;
                vertices[index] = Flatten(matrix.MultiplyPoint3x4(vertex));
                colors[index] = source.color;
            }

            ushort[] sourceTriangles = sprite.triangles;
            int[] triangles = new int[sourceTriangles.Length];
            for (int index = 0; index < triangles.Length; index++) triangles[index] = sourceTriangles[index];
            Mesh mesh = new Mesh
            {
                name = "Refit snapshot / " + source.name,
                hideFlags = HideFlags.HideAndDontSave,
                vertices = vertices,
                uv = sprite.uv,
                colors = colors,
                triangles = triangles
            };
            mesh.RecalculateBounds();
            Material material = CreateMaterial(source, shader, sprite.texture);
            if (sprite.associatedAlphaSplitTexture != null)
            {
                material.SetTexture("_AlphaTex", sprite.associatedAlphaSplitTexture);
                material.SetFloat("_EnableExternalAlpha", 1f);
                material.EnableKeyword("ETC1_EXTERNAL_ALPHA");
            }
            items.Add(new DrawItem(source, mesh, material));
        }

        private bool TryCaptureCircularBody(SpriteRenderer source, Matrix4x4 worldToPreview, Shader shader)
        {
            Sprite sprite = source.sprite;
            if (source.transform.parent == null || source.transform.parent.name != "Body Visual") return false;

            // The live chassis uses small circular bitmap assets (128 px canvas,
            // 72.5 px visible ring). Enlarging those pixels loses the original
            // circle. Rebuild only these known circles as smooth preview-owned
            // geometry, retaining their authored diameter, stroke, tint and pose.
            // Any other body art or attached real parts keep their actual sprite.
            string artwork = sprite.texture.name;
            float diameterPixels;
            float strokePixels;
            if (source.name == "Body Artwork" && (artwork == "robot_body_new" || artwork == "Test_Newbody"))
            {
                diameterPixels = 72.5f;
                strokePixels = 1f;
            }
            else if (source.name == "Body Artwork" && artwork == "robot_body")
            {
                diameterPixels = 84f;
                strokePixels = 1.4f;
            }
            else if (source.name == "Body Fill (robot_body_fill)" && artwork == "robot_body_fill")
            {
                diameterPixels = 82.5f;
                strokePixels = 0f;
            }
            else return false;

            float radius = diameterPixels * .5f / sprite.pixelsPerUnit;
            float innerRadius = strokePixels > 0f ? radius - strokePixels / sprite.pixelsPerUnit : 0f;
            // An explicit alpha fringe survives downsampling to the UI and avoids
            // replacing source pixel steps with hard polygon edges.
            float feather = radius * 1.5f / Resolution;
            float[] radii = innerRadius > 0f
                ? new[] { innerRadius - feather, innerRadius + feather, radius - feather, radius + feather }
                : new[] { 0f, radius - feather, radius + feather };
            float[] alphas = innerRadius > 0f ? new[] { 0f, 1f, 1f, 0f } : new[] { 1f, 1f, 0f };
            Vector2 centre = (sprite.rect.size * .5f - sprite.pivot) / sprite.pixelsPerUnit;
            Vector3[] vertices = new Vector3[radii.Length * CircleSegments];
            Color[] colors = new Color[vertices.Length];
            Vector2[] uv = new Vector2[vertices.Length];
            int[] triangles = new int[(radii.Length - 1) * CircleSegments * 6];
            Matrix4x4 matrix = worldToPreview * source.transform.localToWorldMatrix;
            for (int band = 0; band < radii.Length; band++)
            {
                for (int segment = 0; segment < CircleSegments; segment++)
                {
                    int index = band * CircleSegments + segment;
                    float angle = segment * Mathf.PI * 2f / CircleSegments;
                    Vector2 point = centre + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * radii[band];
                    if (source.flipX) point.x = -point.x;
                    if (source.flipY) point.y = -point.y;
                    vertices[index] = Flatten(matrix.MultiplyPoint3x4(point));
                    Color color = source.color;
                    color.a *= alphas[band];
                    colors[index] = color;
                    uv[index] = new Vector2(.5f, .5f);
                    if (band == radii.Length - 1) continue;
                    int next = (segment + 1) % CircleSegments;
                    int triangle = (band * CircleSegments + segment) * 6;
                    int nextBand = (band + 1) * CircleSegments;
                    triangles[triangle] = index;
                    triangles[triangle + 1] = nextBand + segment;
                    triangles[triangle + 2] = nextBand + next;
                    triangles[triangle + 3] = index;
                    triangles[triangle + 4] = nextBand + next;
                    triangles[triangle + 5] = band * CircleSegments + next;
                }
            }

            Mesh mesh = new Mesh
            {
                name = "Refit smooth source circle / " + source.name,
                hideFlags = HideFlags.HideAndDontSave,
                vertices = vertices,
                uv = uv,
                colors = colors,
                triangles = triangles
            };
            mesh.RecalculateBounds();
            items.Add(new DrawItem(source, mesh, CreateMaterial(source, shader, Texture2D.whiteTexture)));
            ReconstructedBodyRendererCount++;
            return true;
        }

        private void CaptureLine(LineRenderer source, Matrix4x4 worldToPreview, Shader shader, Camera camera)
        {
            Mesh mesh = new Mesh
            {
                name = "Refit snapshot / " + source.name,
                hideFlags = HideFlags.HideAndDontSave
            };
            source.BakeMesh(mesh, camera, false);
            if (mesh.vertexCount == 0)
            {
                DestroyOwned(mesh);
                return;
            }
            Vector3[] vertices = mesh.vertices;
            Matrix4x4 matrix = worldToPreview * (source.useWorldSpace
                ? Matrix4x4.identity : source.transform.localToWorldMatrix);
            for (int index = 0; index < vertices.Length; index++)
                vertices[index] = Flatten(matrix.MultiplyPoint3x4(vertices[index]));
            mesh.vertices = vertices;
            mesh.RecalculateBounds();
            Texture lineTexture = source.sharedMaterial != null ? source.sharedMaterial.mainTexture : null;
            Material material = CreateMaterial(source, shader, lineTexture != null ? lineTexture : Texture2D.whiteTexture);
            items.Add(new DrawItem(source, mesh, material));
        }

        private static Material CreateMaterial(Renderer source, Shader shader, Texture image)
        {
            Material material = new Material(shader)
            {
                name = "Refit snapshot material / " + source.name,
                hideFlags = HideFlags.HideAndDontSave,
                mainTexture = image
            };
            Material original = source.sharedMaterial;
            Color tint = original != null && original.HasProperty("_Color")
                ? original.GetColor("_Color") : Color.white;
            material.SetColor("_Color", tint);
            material.SetColor("_RendererColor", Color.white);
            material.SetVector("_Flip", Vector4.one);
            return material;
        }

        private void DrawSnapshot()
        {
            Bounds bounds = items[0].Mesh.bounds;
            for (int index = 1; index < items.Count; index++) bounds.Encapsulate(items[index].Mesh.bounds);
            Vector3 centre = bounds.center;
            float halfSize = Mathf.Max(.01f, Mathf.Max(bounds.size.x, bounds.size.y) * .59f);
            texture = new RenderTexture(Resolution, Resolution, 0, RenderTextureFormat.ARGB32)
            {
                name = "Refit actual player preview",
                hideFlags = HideFlags.HideAndDontSave,
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp,
                antiAliasing = 1,
                useMipMap = false
            };
            if (!texture.Create()) throw new InvalidOperationException("The preview render target could not be created.");

            Matrix4x4 projection = Matrix4x4.Ortho(centre.x - halfSize, centre.x + halfSize,
                centre.y - halfSize, centre.y + halfSize, -1f, 1f);
            RenderTexture previousTarget = RenderTexture.active;
            RenderTexture multisampledTarget = null;
            try
            {
                multisampledTarget = RenderTexture.GetTemporary(Resolution, Resolution, 0,
                    RenderTextureFormat.ARGB32, RenderTextureReadWrite.Default, 4);
                using (var command = new CommandBuffer { name = "Refit actual player artwork snapshot" })
                {
                    command.SetRenderTarget(multisampledTarget);
                    command.ClearRenderTarget(true, true, Color.clear);
                    command.SetViewport(new Rect(0f, 0f, Resolution, Resolution));
                    // This is a direct mesh draw into a texture sampled by a
                    // UI Toolkit Image, not a camera render. Camera-style target
                    // flipping would invert the unit's forward/up direction.
                    command.SetViewProjectionMatrices(Matrix4x4.identity, GL.GetGPUProjectionMatrix(projection, false));
                    foreach (DrawItem item in items)
                        command.DrawMesh(item.Mesh, Matrix4x4.identity, item.Material, 0, 0);
                    Graphics.ExecuteCommandBuffer(command);
                }
                // Publish a resolved texture so both UI Toolkit and readback
                // consumers receive the same image, without retaining MSAA memory.
                Graphics.Blit(multisampledTarget, texture);
            }
            finally
            {
                RenderTexture.active = previousTarget;
                if (multisampledTarget != null) RenderTexture.ReleaseTemporary(multisampledTarget);
            }
        }

        private static Vector3 Flatten(Vector3 point) => new Vector3(point.x, point.y, 0f);

        private static int CompareDrawOrder(DrawItem left, DrawItem right)
        {
            int layer = left.Layer.CompareTo(right.Layer);
            if (layer != 0) return layer;
            int order = left.Order.CompareTo(right.Order);
            if (order != 0) return order;
            return right.Depth.CompareTo(left.Depth);
        }

        public void Clear()
        {
            HasImage = false;
            ReconstructedBodyRendererCount = 0;
            ExcludedDirectionIndicatorCount = 0;
            foreach (DrawItem item in items)
            {
                DestroyOwned(item.Mesh);
                DestroyOwned(item.Material);
            }
            items.Clear();
            if (texture != null)
            {
                texture.Release();
                DestroyOwned(texture);
                texture = null;
            }
        }

        public void Dispose() => Clear();

        private static void DestroyOwned(UnityEngine.Object target)
        {
            if (target == null) return;
            if (Application.isPlaying) UnityEngine.Object.Destroy(target);
            else UnityEngine.Object.DestroyImmediate(target);
        }

        private readonly struct DrawItem
        {
            public readonly Mesh Mesh;
            public readonly Material Material;
            public readonly int Layer;
            public readonly int Order;
            public readonly float Depth;

            public DrawItem(Renderer source, Mesh mesh, Material material)
            {
                Mesh = mesh;
                Material = material;
                Layer = SortingLayer.GetLayerValueFromID(source.sortingLayerID);
                Order = source.sortingOrder;
                Depth = source.transform.position.z;
            }
        }
    }
}
