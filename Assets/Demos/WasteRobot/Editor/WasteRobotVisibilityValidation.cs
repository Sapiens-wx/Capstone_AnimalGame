using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using AnimalGame.Rendering;
using AnimalGame.World;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace AnimalGame.Demos.WasteRobot.Editor
{
    /// <summary>Disposable Edit Mode fixtures and GPU pixels; does not save or rebuild project assets.</summary>
    public static class WasteRobotVisibilityValidation
    {
        private const int Size = 512;
        private const string OutputDirectory = "output/WasteRobotVisibilityValidation";
        private const string ClipShaderName = "AnimalGame/Player UI Clipped Sprite";
        private static readonly BindingFlags PrivateInstance = BindingFlags.Instance | BindingFlags.NonPublic;

        [MenuItem("Animal Game/Demos/Waste Robot/Validate Circular Visibility")]
        public static void Run()
        {
            Require(!EditorApplication.isPlayingOrWillChangePlaymode, "Exit Play Mode before validation.");
            Require(SystemInfo.graphicsDeviceType != GraphicsDeviceType.Null,
                "GPU pixel checks require a graphics device; omit -nographics.");
            Shader shader = AssetDatabase.LoadAssetAtPath<Shader>("Assets/Shaders/PlayerUiOrganicSprite.shader");
            Require(shader != null && shader.isSupported && !ShaderUtil.ShaderHasError(shader),
                "The production player-circle sprite shader is unavailable or has compilation errors.");
            GameObject asset = AssetDatabase.LoadAssetAtPath<GameObject>(WasteRobotDemoBuilder.PrefabPath);
            Require(asset != null, "Build the Waste Robot prefab first.");
            WasteRobotVisibility[] authored = asset.GetComponentsInChildren<WasteRobotVisibility>(true);
            Require(authored.Length == 1 && authored[0].gameObject == asset && authored[0].enabled,
                "The prefab must contain exactly one enabled root visibility component.");

            float oldEnabled = Shader.GetGlobalFloat("_PlayerUiClipEnabled");
            float oldRadius = Shader.GetGlobalFloat("_PlayerUiClipRadiusPixels");
            float oldSoftness = Shader.GetGlobalFloat("_PlayerUiClipSoftnessPixels");
            Vector4 oldCenter = Shader.GetGlobalVector("_PlayerUiClipCenterPixels");
            Vector4 oldScreen = Shader.GetGlobalVector("_ScreenParams");
            Vector4 oldProjection = Shader.GetGlobalVector("_ProjectionParams");
            Scene preview = EditorSceneManager.NewPreviewScene();
            GameObject root = null;
            WasteRobotVisibility visibility = null;
            try
            {
                Directory.CreateDirectory(OutputDirectory);
                root = (GameObject)PrefabUtility.InstantiatePrefab(asset, preview);
                var machine = root.GetComponent<WasteRobotBase>();
                visibility = root.GetComponent<WasteRobotVisibility>();
                // Non-ExecuteAlways behaviours do not receive Edit Mode lifecycle
                // callbacks. Explicitly drive the production callbacks on this fixture.
                Invoke(visibility, "OnDisable");
                machine.SetInside(false);
                SpriteRenderer[] renderers = root.GetComponentsInChildren<SpriteRenderer>(true);
                Require(renderers.Length >= 2, "The fixture must include exterior and inactive interior sprites.");
                var materials = renderers.ToDictionary(r => r, r => r.sharedMaterials);
                var enabled = renderers.ToDictionary(r => r, r => r.enabled);
                var active = renderers.ToDictionary(r => r, r => r.gameObject.activeSelf);
                Require(!machine.InteriorRoot.activeSelf, "Interior must initially be inactive.");
                WorldInteraction[] walls = root.GetComponentsInChildren<WorldInteraction>(true);
                Require(walls.Length > 0, "The fixture has no collision objects to protect.");
                var available = walls.ToDictionary(w => w, w => w.Available);
                Invoke(visibility, "OnEnable");
                visibility.RefreshRenderers();
                visibility.RefreshRenderers();
                Require(renderers.All(IsClipped), "Registration missed an inactive interior or an exterior sprite.");
                CheckPresentationState(renderers, enabled, active, walls, available);

                SpriteRenderer exterior = Largest(machine.ExteriorRoot);
                SpriteRenderer interior = Largest(machine.InteriorRoot);
                CheckPixels(exterior, "exterior");
                CheckPresentationState(renderers, enabled, active, walls, available);
                machine.SetInside(true);
                Require(interior.gameObject.activeInHierarchy && IsClipped(interior),
                    "The first interior activation must already use the circle shader.");
                available = walls.ToDictionary(w => w, w => w.Available);
                CheckPixels(interior, "interior");
                Require(walls.All(w => w.Available == available[w]),
                    "Changing the clip circle changed interior collision availability.");
                machine.SetInside(false);
                CheckTransitionClone(root, machine);

                // A child added after initialization must be discovered without
                // losing the original materials captured by earlier refreshes.
                var extra = new GameObject("Late sprite visibility fixture");
                extra.transform.SetParent(root.transform, false);
                var lateRenderer = extra.AddComponent<SpriteRenderer>();
                lateRenderer.sprite = exterior.sprite;
                lateRenderer.sharedMaterials = materials[exterior];
                visibility.RefreshRenderers();
                Require(IsClipped(lateRenderer), "RefreshRenderers missed a newly added child sprite.");
                Invoke(visibility, "OnDisable");
                foreach (SpriteRenderer renderer in renderers)
                    Require(renderer.sharedMaterials.SequenceEqual(materials[renderer]),
                        "Disabling visibility failed to restore original materials after repeated refreshes: " + renderer.name);
                Require(lateRenderer.sharedMaterials.SequenceEqual(materials[exterior]),
                    "The late child's original materials were not restored.");
                Require(renderers.All(r => r.enabled == enabled[r]), "Visibility changed renderer.enabled.");
                Require(!ShaderUtil.ShaderHasError(shader), "The circle shader reported GPU compilation errors.");
                Debug.Log("Waste Robot circular visibility validation PASS: enabled prefab ownership; inactive-interior "
                    + "registration; exterior/interior GPU clipping with object center outside the circle; moved and resized "
                    + "circle; no-clip fallback; real outgoing transition clone clipping/cleanup; unchanged renderer "
                    + "and collision state; late-child refresh and original-material restoration. Device: "
                    + SystemInfo.graphicsDeviceType + ". Previews: " + Path.GetFullPath(OutputDirectory));
            }
            finally
            {
                if (visibility != null) Invoke(visibility, "OnDisable");
                if (root != null) Object.DestroyImmediate(root);
                EditorSceneManager.ClosePreviewScene(preview);
                WorldInteractionQuery.InvalidateSpatialIndex();
                Shader.SetGlobalFloat("_PlayerUiClipEnabled", oldEnabled);
                Shader.SetGlobalFloat("_PlayerUiClipRadiusPixels", oldRadius);
                Shader.SetGlobalFloat("_PlayerUiClipSoftnessPixels", oldSoftness);
                Shader.SetGlobalVector("_PlayerUiClipCenterPixels", oldCenter);
                Shader.SetGlobalVector("_ScreenParams", oldScreen);
                Shader.SetGlobalVector("_ProjectionParams", oldProjection);
            }
        }

        public static void RunBatch()
        {
            try { Run(); if (Application.isBatchMode) EditorApplication.Exit(0); }
            catch (Exception error)
            {
                Debug.LogException(error);
                if (Application.isBatchMode) EditorApplication.Exit(1);
                else throw;
            }
        }

        private static void CheckTransitionClone(GameObject root, WasteRobotBase machine)
        {
            WasteRobotTransitionEffects effects = root.GetComponent<WasteRobotTransitionEffects>();
            Require(effects != null, "The machine has no transition effect to validate.");
            FieldInfo field = typeof(WasteRobotTransitionEffects).GetField("machine", PrivateInstance);
            Require(field != null, "Cannot bind the disposable transition fixture.");
            field.SetValue(effects, machine);
            SpriteRenderer[] originals = root.GetComponentsInChildren<SpriteRenderer>(true);
            var enabled = originals.ToDictionary(r => r, r => r.enabled);
            WasteRobotVisibility cloneVisibility = null;
            try
            {
                Invoke(effects, "CaptureOutgoing", machine.ExteriorRoot);
                GameObject outgoing = effects.OutgoingVisualObject;
                Require(outgoing != null && outgoing.GetComponentsInChildren<SpriteRenderer>(true).Length > 0,
                    "The production transition did not capture an outgoing sprite.");
                cloneVisibility = outgoing.GetComponent<WasteRobotVisibility>();
                Require(cloneVisibility != null, "The outgoing transition clone has no visibility lifecycle owner.");
                Invoke(cloneVisibility, "OnEnable");
                Require(outgoing.GetComponentsInChildren<SpriteRenderer>(true).All(IsClipped),
                    "An outgoing transition sprite bypassed circular clipping.");
                Require(outgoing.GetComponentsInChildren<WorldInteraction>(true).Length == 0,
                    "The visual-only transition clone contains collision objects.");
                CheckPixels(Largest(outgoing), "transition-outgoing");
            }
            finally
            {
                if (cloneVisibility != null) Invoke(cloneVisibility, "OnDisable");
                Invoke(effects, "Cleanup");
            }
            Require(effects.OutgoingVisualObject == null, "Transition cleanup retained its visual proxy.");
            Require(originals.All(r => r.enabled == enabled[r]),
                "Transition cleanup failed to restore the original renderer states.");
        }

        private static void CheckPixels(SpriteRenderer renderer, string name)
        {
            Require(IsClipped(renderer), name + " is not using the production circle shader.");
            Mesh mesh = BuildSpriteMesh(renderer);
            var material = new Material(renderer.sharedMaterial) { hideFlags = HideFlags.HideAndDontSave };
            material.mainTexture = renderer.sprite.texture;
            material.SetColor("_Color", Color.white);
            Texture2D baseline = null;
            try
            {
                PlayerUiOrganicVisibility.DisableClip();
                baseline = Capture(mesh, material);
                Color[] reference = baseline.GetPixels();
                // Find substantial artwork away from the object's center. A
                // center-based whole-object culler would incorrectly hide it all.
                Vector2 center = ChooseOffCenterCircle(reference);
                Require(Vector2.Distance(center, new Vector2(Size * .5f, Size * .5f)) > 78f,
                    "The clip fixture does not place the object center outside its circle.");
                CheckCircle(mesh, material, reference, center, 70f, name + "-circle");
                CheckCircle(mesh, material, reference, new Vector2(Size - center.x, center.y), 98f,
                    name + "-moved-resized");
                PlayerUiOrganicVisibility.DisableClip();
                Texture2D restored = Capture(mesh, material);
                try
                {
                    Color[] pixels = restored.GetPixels();
                    Require(pixels.Length == reference.Length && pixels.Select((p, i) => Mathf.Abs(p.a - reference[i].a)).Max() < .01f,
                        "Disabling circle data did not restore the normal fully visible sprite fallback.");
                }
                finally { Object.DestroyImmediate(restored); }
            }
            finally
            {
                if (baseline != null) Object.DestroyImmediate(baseline);
                Object.DestroyImmediate(material);
                Object.DestroyImmediate(mesh);
            }
        }

        private static Vector2 ChooseOffCenterCircle(Color[] pixels)
        {
            Vector2 best = new Vector2(128f, 256f);
            int bestCount = -1;
            foreach (float x in new[] { 112f, 136f, 368f, 392f })
            {
                int count = 0;
                for (int y = 0; y < Size; y++)
                for (int px = 0; px < Size; px++)
                    if ((new Vector2(px + .5f, y + .5f) - new Vector2(x, 256f)).sqrMagnitude < 62f * 62f
                        && pixels[y * Size + px].a > .5f) count++;
                if (count > bestCount) { bestCount = count; best = new Vector2(x, 256f); }
            }
            Require(bestCount > 300, "The rendered sprite has insufficient off-center opaque artwork for the pixel test.");
            return best;
        }

        private static void CheckCircle(Mesh mesh, Material material, Color[] reference, Vector2 center,
            float radius, string name)
        {
            PlayerUiOrganicVisibility.SetClip(center, radius, 2f);
            Texture2D image = Capture(mesh, material);
            try
            {
                Color[] pixels = image.GetPixels();
                int visible = 0, hidden = 0;
                for (int y = 0; y < Size; y++)
                for (int x = 0; x < Size; x++)
                {
                    int index = y * Size + x;
                    float distance = Vector2.Distance(new Vector2(x + .5f, y + .5f), center);
                    if (distance < radius - 5f && reference[index].a > .5f)
                    {
                        Require(Mathf.Abs(pixels[index].a - reference[index].a) < .04f,
                            name + " changed artwork inside the player circle at " + x + "," + y + ".");
                        visible++;
                    }
                    else if (distance > radius + 5f && reference[index].a > .5f)
                    {
                        Require(pixels[index].a < .01f,
                            name + " leaked artwork outside the player circle at " + x + "," + y + ".");
                        hidden++;
                    }
                }
                Require(visible > 150 && hidden > 1000,
                    name + " did not sample enough opaque artwork on both sides of the circle boundary.");
                File.WriteAllBytes(Path.Combine(OutputDirectory, name + ".png"), image.EncodeToPNG());
            }
            finally { Object.DestroyImmediate(image); }
        }

        private static Mesh BuildSpriteMesh(SpriteRenderer renderer)
        {
            Sprite sprite = renderer.sprite;
            Bounds bounds = sprite.bounds;
            float scale = 440f / Mathf.Max(bounds.size.x, bounds.size.y);
            Vector2[] source = sprite.vertices;
            var vertices = new Vector3[source.Length];
            var colors = new Color32[source.Length];
            for (int i = 0; i < source.Length; i++)
            {
                Vector2 point = source[i] - (Vector2)bounds.center;
                if (renderer.flipX) point.x = -point.x;
                if (renderer.flipY) point.y = -point.y;
                vertices[i] = new Vector3(Size * .5f + point.x * scale, Size * .5f + point.y * scale, 0f);
                colors[i] = renderer.color;
            }
            var mesh = new Mesh { hideFlags = HideFlags.HideAndDontSave };
            mesh.vertices = vertices;
            mesh.uv = sprite.uv;
            mesh.colors32 = colors;
            mesh.triangles = Array.ConvertAll(sprite.triangles, value => (int)value);
            return mesh;
        }

        private static Texture2D Capture(Mesh mesh, Material material)
        {
            RenderTexture previous = RenderTexture.active;
            bool oldSrgb = GL.sRGBWrite, pushed = false;
            var target = new RenderTexture(Size, Size, 0, RenderTextureFormat.ARGB32);
            var snapshot = new Texture2D(Size, Size, TextureFormat.RGBA32, false);
            try
            {
                target.Create();
                RenderTexture.active = target;
                GL.sRGBWrite = QualitySettings.activeColorSpace == ColorSpace.Linear;
                GL.Clear(true, true, Color.clear);
                GL.PushMatrix(); pushed = true;
                GL.LoadPixelMatrix(0f, Size, 0f, Size);
                Shader.SetGlobalVector("_ScreenParams", new Vector4(Size, Size, 1f + 1f / Size, 1f + 1f / Size));
                Shader.SetGlobalVector("_ProjectionParams", new Vector4(1f, .1f, 1000f, .001f));
                Require(material.SetPass(0), "The registered sprite material has no supported render pass.");
                Graphics.DrawMeshNow(mesh, Matrix4x4.identity);
                GL.PopMatrix(); pushed = false;
                snapshot.ReadPixels(new Rect(0f, 0f, Size, Size), 0, 0);
                snapshot.Apply();
                return snapshot;
            }
            catch { Object.DestroyImmediate(snapshot); throw; }
            finally
            {
                if (pushed) GL.PopMatrix();
                GL.sRGBWrite = oldSrgb;
                RenderTexture.active = previous;
                target.Release();
                Object.DestroyImmediate(target);
            }
        }

        private static void CheckPresentationState(SpriteRenderer[] renderers, Dictionary<SpriteRenderer, bool> enabled,
            Dictionary<SpriteRenderer, bool> active, WorldInteraction[] walls, Dictionary<WorldInteraction, bool> available)
        {
            Require(renderers.All(r => r.enabled == enabled[r] && r.gameObject.activeSelf == active[r]),
                "Visibility registration changed a renderer's enabled state or presentation root state.");
            Require(walls.All(w => w.Available == available[w]), "Visibility registration changed collision availability.");
        }

        private static bool IsClipped(SpriteRenderer renderer) => renderer.sharedMaterial != null
            && renderer.sharedMaterial.shader != null && renderer.sharedMaterial.shader.name == ClipShaderName;

        private static SpriteRenderer Largest(GameObject root) => root.GetComponentsInChildren<SpriteRenderer>(true)
            .Where(r => r.sprite != null).OrderByDescending(r => r.sprite.bounds.size.sqrMagnitude).First();

        private static void Invoke(object target, string name, params object[] arguments)
        {
            MethodInfo method = target.GetType().GetMethod(name, PrivateInstance);
            Require(method != null, "Missing fixture callback " + name + ".");
            method.Invoke(target, arguments);
        }

        private static void Require(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException("Waste Robot visibility validation: " + message);
        }
    }
}
