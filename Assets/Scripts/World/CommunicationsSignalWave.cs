using AnimalGame.RobotMap;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

namespace AnimalGame.World
{
    // Owned by the station, but ticked by its tower even while the roof is hidden.
    internal sealed class CommunicationsSignalWave
    {
        private static readonly int FrontProperty = Shader.PropertyToID("_Front");
        private static readonly int OpacityProperty = Shader.PropertyToID("_Opacity");
        private static readonly int CurveProfileProperty = Shader.PropertyToID("_CurveProfile");
        private static readonly int CurveTexelSizeProperty = Shader.PropertyToID("_CurveProfile_TexelSize");
        private const int ProfileResolution = 512;
        private const int MinLobes = 4;
        private const int MaxLobes = 9;

        private readonly CommunicationsStation station;
        private readonly GameObject view;
        private readonly Mesh mesh;
        private readonly MeshRenderer renderer;
        private readonly MaterialPropertyBlock properties = new();
        private readonly float[] curveSamples = new float[ProfileResolution];
        private readonly Texture2D curveProfile;
        private float elapsed;
        private float? previousReceiverGap;
        private bool receivedThisPulse;
        private uint randomState;
        private bool disposed;

        internal CommunicationsSignalWave(CommunicationsStation owner)
        {
            station = owner;
            // World-space scale keeps the signal circular when the device is scaled or rotated.
            view = new GameObject(owner.name + " Signal Wave");
            SceneManager.MoveGameObjectToScene(view, owner.gameObject.scene);
            mesh = new Mesh
            {
                name = "Communications Signal Quad",
                vertices = new[]
                {
                    new Vector3(-.5f, -.5f, 0f), new Vector3(.5f, -.5f, 0f),
                    new Vector3(.5f, .5f, 0f), new Vector3(-.5f, .5f, 0f)
                },
                uv = new[] { Vector2.zero, Vector2.right, Vector2.one, Vector2.up },
                triangles = new[] { 0, 1, 2, 0, 2, 3 }
            };
            mesh.RecalculateBounds();
            view.AddComponent<MeshFilter>().sharedMesh = mesh;
            renderer = view.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = owner.SignalMaterial;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            renderer.lightProbeUsage = LightProbeUsage.Off;
            renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
            renderer.sortingOrder = 950;
            renderer.enabled = false;

            randomState = unchecked((uint)System.Environment.TickCount ^ (uint)owner.GetInstanceID() * 747796405u);
            curveProfile = new Texture2D(ProfileResolution, 1, TextureFormat.RFloat, false, true)
            {
                name = "Communications Signal Curve",
                hideFlags = HideFlags.HideAndDontSave,
                wrapMode = TextureWrapMode.Repeat,
                filterMode = FilterMode.Bilinear
            };
            GenerateCurveProfile();
            properties.SetTexture(CurveProfileProperty, curveProfile);
            properties.SetVector(CurveTexelSizeProperty,
                new Vector4(1f / ProfileResolution, 1f, ProfileResolution, 1f));
        }

        internal void Tick(float deltaTime, RobotMover player)
        {
            if (disposed) return;
            if (station == null)
            {
                Dispose();
                return;
            }
            // activeSelf distinguishes an explicitly disabled device from a hidden parent floor.
            if (!station.enabled || !station.gameObject.activeSelf || station.HasViewedCommunications)
            {
                Hide();
                return;
            }

            float previousElapsed = elapsed;
            elapsed += deltaTime;
            int completed = Mathf.FloorToInt(elapsed / station.SignalInterval);
            elapsed -= completed * station.SignalInterval;
            if (completed > 0)
            {
                GenerateCurveProfile();
                previousElapsed = 0f;
                previousReceiverGap = null;
                receivedThisPulse = false;
            }

            float progress = Mathf.Clamp01(elapsed / station.SignalDuration);
            bool transmitting = progress < 1f;
            renderer.enabled = transmitting;
            // Test reception on the final frame that completes each pulse.
            if (!transmitting && previousElapsed >= station.SignalDuration) return;

            float travel = .12f * progress + .88f * progress * progress;
            float front = Mathf.Lerp(14f / 210f, .995f, travel);
            Transform origin = station.SignalOrigin;
            Quaternion rotation = Quaternion.Euler(0f, 0f, origin.eulerAngles.z);
            if (deltaTime > 0f) CheckReception(player, origin.position, rotation, front);
            if (!transmitting) return;

            float fade = Mathf.SmoothStep(0f, 1f, progress / .08f)
                * (1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(.84f, 1f, travel)));
            view.transform.SetPositionAndRotation(origin.position, rotation);
            view.transform.localScale = Vector3.one * (station.SignalRadius * 2f);
            properties.SetFloat(FrontProperty, front);
            properties.SetFloat(OpacityProperty, station.SignalOpacity * fade);
            renderer.SetPropertyBlock(properties);
        }

        private void CheckReception(RobotMover player, Vector3 origin, Quaternion rotation, float front)
        {
            if (receivedThisPulse) return;
            if (player == null || !player.isActiveAndEnabled)
            {
                previousReceiverGap = null;
                return;
            }

            Vector2 offset = Quaternion.Inverse(rotation) * (player.transform.position - origin);
            float angle = Mathf.Atan2(offset.y, offset.x) / (2f * Mathf.PI);
            float sample = Mathf.Repeat(angle, 1f) * ProfileResolution;
            int index = Mathf.FloorToInt(sample);
            float profile = Mathf.Lerp(curveSamples[index % ProfileResolution],
                curveSamples[(index + 1) % ProfileResolution], sample - index);
            float gap = offset.magnitude - front * profile * station.SignalRadius;
            // Compare consecutive positions on the same pulse, so a fast-moving robot can
            // intercept the wave even when neither frame places it exactly on the front.
            float previousGap = previousReceiverGap.GetValueOrDefault(float.PositiveInfinity);
            bool crossedFront = (previousGap > 0f && gap <= 0f) || (previousGap < 0f && gap >= 0f);
            previousReceiverGap = gap;
            if (!crossedFront) return;

            receivedThisPulse = true;
            if (player.TryGetComponent(out BioScanAnimalMarkerUI feedback))
                feedback.PlayHitBurst(player.GetInstanceID(), player.transform.position);
            station.ReportSignalReceived(player);
        }

        private void GenerateCurveProfile()
        {
            int lobeCount = MinLobes + (int)(NextRandom() * (MaxLobes - MinLobes + 1));
            int arcCount = lobeCount * 2;
            float nominalWidth = (float)ProfileResolution / arcCount;
            int rotation = (int)(NextRandom() * ProfileResolution);
            int start = 0;
            float startRadius = 1f;

            // Alternate crests and troughs at uneven angles. The last arc returns to the
            // first crest with the same zero slope, including where the texture wraps.
            for (int arc = 1; arc <= arcCount; arc++)
            {
                int end = ProfileResolution;
                float endRadius = 1f;
                if (arc < arcCount)
                {
                    float angleJitter = Mathf.Lerp(-.35f, .35f, NextRandom());
                    end = Mathf.RoundToInt((arc + angleJitter) * nominalWidth);
                    endRadius = (arc & 1) == 0
                        ? Mathf.Lerp(.93f, 1f, NextRandom())
                        : Mathf.Lerp(.76f, .89f, NextRandom());
                }
                for (int sample = start; sample < end; sample++)
                {
                    float t = (float)(sample - start) / (end - start);
                    curveSamples[(sample + rotation) % ProfileResolution] = Mathf.SmoothStep(startRadius, endRadius, t);
                }
                start = end;
                startRadius = endRadius;
            }
            curveProfile.SetPixelData(curveSamples, 0);
            curveProfile.Apply(false, false);
        }

        private float NextRandom()
        {
            // Each device owns its random sequence; broadcasting does not alter world randomness.
            unchecked
            {
                randomState += 0x6D2B79F5u;
                uint value = (randomState ^ (randomState >> 15)) * (1u | randomState);
                value ^= value + (value ^ (value >> 7)) * (61u | value);
                // Use the 24 representable fraction bits so the float is always below one.
                return ((value ^ (value >> 14)) >> 8) * (1f / 16777216f);
            }
        }

        internal void Hide()
        {
            previousReceiverGap = null;
            if (!disposed) renderer.enabled = false;
        }

        internal void Dispose()
        {
            if (disposed) return;
            disposed = true;
            renderer.enabled = false;
            Object.Destroy(view);
            Object.Destroy(mesh);
            Object.Destroy(curveProfile);
        }
    }
}
