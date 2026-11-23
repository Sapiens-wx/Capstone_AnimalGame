using System;
using AnimalGame.RobotMap;
using UnityEngine;
using Object = UnityEngine.Object;

namespace AnimalGame.MapTest
{
    // The prologue runs with gameplay paused. Reuse the unknown animal artwork without spawning AI.
    internal sealed class OpeningWoodpecker : IDisposable
    {
        private const float PeckRecovery = .22f;
        private const float FlightDuration = .94f;
        private static readonly Vector3 PerchDirection = new Vector3(.54f, .84f, 0f).normalized;
        private static readonly int SeedId = Shader.PropertyToID("_Seed");
        private static readonly int AnimationPhaseId = Shader.PropertyToID("_AnimationPhase");
        private static readonly int ShapePhaseId = Shader.PropertyToID("_ShapePhase");

        private readonly SpriteRenderer appearance;
        private readonly RobotMarkerView marker;
        private readonly Camera camera;
        private SpriteRenderer bird;
        private Material foregroundMaterial;
        private MaterialPropertyBlock staticProperties;
        private Vector3 baseScale;
        private Vector3 flightOrigin, flightDirection;
        private float age;
        private float peckAge = PeckRecovery;
        private float flightAge = -1f;

        internal OpeningWoodpecker(SpriteRenderer appearance, Transform robot, Camera camera)
        {
            this.appearance = appearance;
            marker = robot.GetComponent<RobotMarkerView>();
            this.camera = camera;
        }

        private Vector3 ContactLocal => PerchDirection * (marker.VisualBodyDiameter * .49f);

        internal void Show()
        {
            bird = Object.Instantiate(appearance, marker.MarkerVisualRoot, false);
            bird.gameObject.name = "Opening Woodpecker";
            foregroundMaterial = new Material(appearance.sharedMaterial)
            {
                name = "Opening Woodpecker Static",
                hideFlags = HideFlags.DontSave,
                renderQueue = marker.ForegroundSpriteMaterial.renderQueue
            };
            bird.sharedMaterial = foregroundMaterial;
            baseScale = appearance.transform.localScale * .64f;
            staticProperties = new MaterialPropertyBlock();
            staticProperties.SetFloat(SeedId, 2.37f);
            Tick(0f);
        }

        internal Vector3 Peck()
        {
            peckAge = 0f;
            PoseOnPerch();
            return marker.MarkerVisualRoot.TransformPoint(ContactLocal);
        }

        internal Vector3 BeginDeparture()
        {
            flightAge = 0f;
            flightOrigin = bird.transform.position;
            flightDirection = (camera.transform.right * .57f + camera.transform.up * .82f).normalized;
            return flightOrigin;
        }

        internal void Tick(float unscaledDeltaTime)
        {
            if (bird == null) return;

            age += unscaledDeltaTime;
            staticProperties.SetFloat(AnimationPhaseId, age);
            staticProperties.SetFloat(ShapePhaseId, age * 2.2f);
            bird.SetPropertyBlock(staticProperties);

            if (flightAge < 0f)
            {
                peckAge += unscaledDeltaTime;
                PoseOnPerch();
                return;
            }

            flightAge += unscaledDeltaTime;
            float travel = Mathf.Clamp01(flightAge / FlightDuration);
            if (travel >= 1f)
            {
                Dispose();
                return;
            }

            float flap = Mathf.Sin(flightAge * 11f * 2f * Mathf.PI);
            bird.transform.position = flightOrigin + flightDirection * (marker.VisualBodyDiameter * travel * (1f + 5f * travel));
            bird.transform.localRotation = Quaternion.Euler(0f, 0f, -26f + 10f * flap);
            bird.transform.localScale = Vector3.Scale(baseScale, new Vector3(1.12f + .2f * flap, .92f - .12f * flap, 1f));
            bird.color = new Color(1f, 1f, 1f, 1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(.52f, 1f, travel)));
        }

        private void PoseOnPerch()
        {
            float recoil = 1f - Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(peckAge / PeckRecovery));
            float reach = Mathf.Lerp(.26f, .07f, recoil);
            bird.transform.localPosition = ContactLocal + PerchDirection * (marker.VisualBodyDiameter * reach);
            bird.transform.localRotation = Quaternion.Euler(0f, 0f, 20f * recoil);
            bird.transform.localScale = Vector3.Scale(baseScale, new Vector3(1f - .13f * recoil, 1f + .12f * recoil, 1f));
        }

        public void Dispose()
        {
            if (bird != null)
            {
                bird.gameObject.SetActive(false);
                Object.Destroy(bird.gameObject);
                bird = null;
            }
            Object.Destroy(foregroundMaterial);
            foregroundMaterial = null;
        }
    }
}
