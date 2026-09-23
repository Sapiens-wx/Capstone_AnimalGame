using System;
using System.Collections.Generic;
using AnimalGame.MapTest;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace AnimalGame.RobotMap
{
    /// <summary>Owns a frozen shutter-time texture. The review view borrows it until closed.</summary>
    internal sealed class PhotoContourCapture : IDisposable
    {
        public RenderTexture Texture { get; private set; }
        public Vector2 CenterViewport { get; private set; }
        public float RadiusViewportHeight { get; private set; }

        public bool Capture(Camera source, MapTestSceneController map, int resolution)
        {
            Dispose();
            CenterViewport = Vector2.one * 0.5f;
            RadiusViewportHeight = 0.4f;
            if (source == null || map == null || map.PhotoContourRenderer == null) return false;
            SpriteRenderer terrain = map.PhotoContourRenderer;
            Material original = terrain.sharedMaterial;
            if (original == null) return false;
            map.GetPhotoSnapshotCircle(out Vector2 center, out float radius);
            radius = Mathf.Max(1, radius);
            CenterViewport = new Vector2(center.x / Screen.width, center.y / Screen.height);
            RadiusViewportHeight = radius / Screen.height;

            var cameraObject = new GameObject("Photo Contour Snapshot Camera") { hideFlags = HideFlags.HideAndDontSave };
            Camera camera = cameraObject.AddComponent<Camera>();
            var material = new Material(original) { hideFlags = HideFlags.HideAndDontSave };
            var hidden = new List<Renderer>();
            RenderTexture previousActive = RenderTexture.active;
            try
            {
                camera.CopyFrom(source);
                camera.enabled = false;
                camera.transform.SetPositionAndRotation(source.transform.position, source.transform.rotation);
                camera.clearFlags = CameraClearFlags.SolidColor;
                camera.backgroundColor = Color.black;
                camera.rect = new Rect(0, 0, 1, 1);
                camera.allowHDR = false;
                camera.allowMSAA = false;
                camera.cullingMask = 1 << terrain.gameObject.layer;
                var data = camera.GetUniversalAdditionalCameraData();
                data.renderPostProcessing = false;
                data.renderShadows = false;
                data.requiresColorOption = CameraOverrideOption.Off;
                data.requiresDepthOption = CameraOverrideOption.Off;

                // Crop the source projection, preserving camera roll, zoom and the actual UI circle.
                Rect pixelRect = source.pixelRect;
                float cx = (center.x - pixelRect.x) / pixelRect.width * 2 - 1;
                float cy = (center.y - pixelRect.y) / pixelRect.height * 2 - 1;
                Matrix4x4 crop = Matrix4x4.identity;
                crop.m00 = pixelRect.width / (2 * radius);
                crop.m11 = pixelRect.height / (2 * radius);
                crop.m03 = -cx * crop.m00;
                crop.m13 = -cy * crop.m11;
                camera.projectionMatrix = crop * source.projectionMatrix;

                material.SetFloat("_SurfaceEnabled", 0);
                material.SetFloat("_PhotoContoursOnly", 1);
                material.SetFloat("_SurfaceRevealEnabled", 0);
                material.SetFloat("_WaterEnabled", 0);
                material.SetFloat("_ElevationFilterEnabled", 0);
                material.SetColor("_ContourColor", Color.white);
                terrain.sharedMaterial = material;
                // Synchronous render: exclude every other renderer, restoring exactly in finally.
                foreach (Renderer renderer in UnityEngine.Object.FindObjectsByType<Renderer>(FindObjectsSortMode.None))
                {
                    if (renderer == terrain || renderer.forceRenderingOff
                        || (camera.cullingMask & (1 << renderer.gameObject.layer)) == 0) continue;
                    renderer.forceRenderingOff = true;
                    hidden.Add(renderer);
                }
                Texture = new RenderTexture(Mathf.Clamp(resolution, 128, 2048),
                    Mathf.Clamp(resolution, 128, 2048), 24, RenderTextureFormat.ARGB32)
                { name = "Frozen Photo Contours", hideFlags = HideFlags.HideAndDontSave };
                Texture.Create();
                var request = new UniversalRenderPipeline.SingleCameraRequest { destination = Texture };
                if (!RenderPipeline.SupportsRenderRequest(camera, request))
                    throw new InvalidOperationException("The active renderer does not support URP SingleCameraRequest.");
                RenderPipeline.SubmitRenderRequest(camera, request);
                return true;
            }
            catch (Exception exception)
            {
                Debug.LogWarning($"Photo contour capture failed: {exception.Message}", source);
                Dispose();
                return false;
            }
            finally
            {
                terrain.sharedMaterial = original;
                foreach (Renderer renderer in hidden)
                    if (renderer != null) renderer.forceRenderingOff = false;
                RenderTexture.active = previousActive;
                UnityEngine.Object.Destroy(material);
                UnityEngine.Object.Destroy(cameraObject);
            }
        }

        public void Dispose()
        {
            if (Texture == null) return;
            Texture.Release();
            UnityEngine.Object.Destroy(Texture);
            Texture = null;
        }
    }
}
