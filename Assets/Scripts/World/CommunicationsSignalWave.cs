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
        private static readonly Vector2[] Spectrum =
        {
            new(2f, .018f), new(3f, .012f), new(5f, .022f),
            new(8f, .052f), new(11f, .032f), new(17f, .006f)
        };

        private readonly CommunicationsStation station;
        private readonly GameObject view;
        private readonly Mesh mesh;
        private readonly MeshRenderer renderer;
        private readonly MaterialPropertyBlock properties = new();
        private readonly Vector4[] curveTerms = new Vector4[Spectrum.Length];
        private readonly float[] curveSamples = new float[ProfileResolution];
        private readonly Texture2D curveProfile;
        private float elapsed;
        private float? previousReceiverGap;
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
            if (!station.enabled || !station.gameObject.activeSelf)
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
            }

            float progress = Mathf.Clamp01(elapsed / station.SignalDuration);
            renderer.enabled = progress < 1f;
            if (!renderer.enabled && previousElapsed >= station.SignalDuration) return;

            float travel = .12f * progress + .88f * progress * progress;
            float front = Mathf.Lerp(14f / 210f, .995f, travel);
            Transform origin = station.SignalOrigin;
            Quaternion rotation = Quaternion.Euler(0f, 0f, origin.eulerAngles.z);
            if (deltaTime > 0f) CheckReception(player, origin.position, rotation, front);
            if (!renderer.enabled) return;

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
            if (!CommunicationsStation.HasSignalReceiver || player == null || !player.isActiveAndEnabled)
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
            if (crossedFront) station.ReportSignalReceived(player);
        }

        private void GenerateCurveProfile()
        {
            for (int index = 0; index < Spectrum.Length; index++)
                curveTerms[index] = new Vector4(Spectrum[index].x,
                    Spectrum[index].y * Mathf.Lerp(.6f, 1.25f, NextRandom()),
                    NextRandom() * (2f * Mathf.PI), 0f);

            // Bake once per emission instead of evaluating six sine waves per screen pixel.
            float maximum = 0f;
            for (int index = 0; index < curveSamples.Length; index++)
            {
                float angle = index * (2f * Mathf.PI / ProfileResolution);
                float radius = 1f;
                foreach (Vector4 term in curveTerms)
                    radius += term.y * Mathf.Sin(term.x * angle + term.z);
                curveSamples[index] = radius;
                maximum = Mathf.Max(maximum, radius);
            }
            for (int index = 0; index < curveSamples.Length; index++)
                curveSamples[index] /= maximum + .0005f;
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
                return (value ^ (value >> 14)) / 4294967296f;
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
