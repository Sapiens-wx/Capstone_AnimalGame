using UnityEngine;

namespace AnimalGame.Animals
{
    [DisallowMultipleComponent]
    [AddComponentMenu("Animal Game/Animals/Animal Sound Emitter")]
    public sealed class AnimalSoundEmitter : MonoBehaviour
    {
        [Header("Shared Sound-Wave Appearance")]
        [SerializeField] private Shader soundWaveShader;
        [SerializeField] private Color soundWaveColor =
            new Color(0.88f, 0.88f, 0.88f, 0.9f);
        [Tooltip("Amount of short, irregular gaps around each ring.")]
        [SerializeField, Range(0f, 1f)] private float ringBreakup = 0.38f;
        [Tooltip("Amount of subtle radial wobble and uneven brightness.")]
        [SerializeField, Range(0f, 1f)] private float ringIrregularity = 0.28f;
        [SerializeField] private Vector2 soundOriginOffset;
        [SerializeField] private int sortingOrder = 995;

        [Header("Overall Restraint")]
        [Tooltip("Applied after each behaviour's configured radius. 0.5 makes every sound wave half-sized.")]
        [SerializeField, Range(0.05f, 2f)]
        private float overallRadiusMultiplier = 0.5f;
        [Tooltip("Applied after each behaviour's configured ring count. Results are rounded to the nearest whole ring and never drop below one.")]
        [SerializeField, Range(0.1f, 2f)]
        private float overallRingCountMultiplier = 0.5f;
        [Tooltip("Multiplier for repeating sounds per second. 0.5 doubles repeat intervals. Contact-driven pecking and one-shot transitions keep their action timing.")]
        [SerializeField, Range(0.1f, 2f)]
        private float overallEmissionFrequencyMultiplier = 0.5f;

        [Header("Passive And Observing Sounds")]
        [SerializeField] private AnimalSoundWaveSettings idleSound =
            new AnimalSoundWaveSettings(0.28f, 0.5f, 3, 1.6f, 2.4f, 0.55f);
        [SerializeField] private AnimalSoundWaveSettings lookingSound =
            new AnimalSoundWaveSettings(0.4f, 0.58f, 4, 0.8f, 1.25f, 0.68f);
        [SerializeField] private AnimalSoundWaveSettings curiousSound =
            new AnimalSoundWaveSettings(0.58f, 0.68f, 5, 1.1f, 1.65f, 0.72f);
        [SerializeField] private AnimalSoundWaveSettings eatingSound =
            new AnimalSoundWaveSettings(0.55f, 0.68f, 5, 0.75f, 1.1f, 0.76f);

        [Header("Movement Sounds")]
        [SerializeField] private AnimalSoundWaveSettings landMovementSound =
            new AnimalSoundWaveSettings(0.78f, 0.62f, 5, 0.48f, 0.68f, 0.7f);
        [SerializeField] private AnimalSoundWaveSettings waterMovementSound =
            new AnimalSoundWaveSettings(0.68f, 0.72f, 5, 0.58f, 0.82f, 0.68f);
        [SerializeField] private AnimalSoundWaveSettings fleeingSound =
            new AnimalSoundWaveSettings(1.35f, 0.78f, 5, 0.22f, 0.34f, 0.84f);

        [Header("Flight And Tree Sounds")]
        [SerializeField] private AnimalSoundWaveSettings flyingSound =
            new AnimalSoundWaveSettings(0.65f, 0.45f, 4, 1.2f, 1.8f, 0.68f, false);
        [Tooltip("Pecking cadence is controlled by Pecks Per Sound Wave on Pileated Woodpecker Behaviour; this profile controls the wave appearance.")]
        [SerializeField] private AnimalSoundWaveSettings peckingSound =
            new AnimalSoundWaveSettings(0.52f, 0.35f, 3, 0.55f, 0.55f, 0.76f, false);
        [SerializeField] private AnimalSoundWaveSettings takeoffSound =
            new AnimalSoundWaveSettings(0.75f, 0.4f, 4, 1f, 1f, 0.76f, false);
        [SerializeField] private AnimalSoundWaveSettings landingSound =
            new AnimalSoundWaveSettings(0.6f, 0.4f, 4, 1f, 1f, 0.7f, false);
        [SerializeField] private AnimalSoundWaveSettings enteringTreeSound =
            new AnimalSoundWaveSettings(0.5f, 0.4f, 3, 1f, 1f, 0.7f, false);
        [SerializeField] private AnimalSoundWaveSettings emergingFromTreeSound =
            new AnimalSoundWaveSettings(0.45f, 0.45f, 3, 1f, 1f, 0.65f, false);

        [Header("Water Transition Sounds")]
        [SerializeField] private AnimalSoundWaveSettings submergingSound =
            new AnimalSoundWaveSettings(1.8f, 1f, 5, 1f, 1f, 0.9f);
        [SerializeField] private AnimalSoundWaveSettings surfacingSound =
            new AnimalSoundWaveSettings(2.2f, 1.1f, 5, 1f, 1f, 0.94f);

        [Header("Editor Visualization")]
        [SerializeField] private bool showSoundRangeGizmos = true;

        private AnimalAgent agent;
        private float movementSoundCountdown;
        private AnimalSoundKind currentMovementSoundKind;
        private bool hasMovementSoundKind;

        public void Initialize(AnimalAgent owner)
        {
            agent = owner;
            movementSoundCountdown = 0f;
            hasMovementSoundKind = false;
        }

        public void Tick(float deltaTime)
        {
            if (agent == null || agent.Motor == null)
                return;

            if (agent.Motor.CurrentSpeedMetersPerSecond <= 0.03f)
            {
                movementSoundCountdown = 0f;
                hasMovementSoundKind = false;
                return;
            }

            AnimalSoundKind movementKind = ChooseMovementSoundKind();
            if (hasMovementSoundKind
                && movementKind != currentMovementSoundKind
                && (movementKind == AnimalSoundKind.Flying
                    || currentMovementSoundKind == AnimalSoundKind.Flying))
            {
                movementSoundCountdown = 0f;
            }
            currentMovementSoundKind = movementKind;
            hasMovementSoundKind = true;

            movementSoundCountdown -= Mathf.Max(0f, deltaTime);
            if (movementSoundCountdown > 0f)
                return;

            Emit(movementKind);
            movementSoundCountdown = ChooseRepeatInterval(movementKind);
        }

        public void Emit(AnimalSoundKind soundKind)
        {
            Vector3 position = transform.TransformPoint(
                new Vector3(soundOriginOffset.x, soundOriginOffset.y, 0f));
            EmitAt(soundKind, position);
        }

        /// <summary>Emits a wave anchored at the contact point, rather than following the animal.</summary>
        public void EmitAt(AnimalSoundKind soundKind, Vector3 worldPosition)
        {
            AnimalSoundWaveSettings settings = GetSettings(soundKind);
            if (settings == null || !settings.Enabled)
                return;

            AnimalSoundWaveManager.Emit(
                worldPosition,
                settings,
                soundWaveShader,
                soundWaveColor,
                ringBreakup,
                ringIrregularity,
                sortingOrder,
                overallRadiusMultiplier,
                overallRingCountMultiplier);
        }

        /// <summary>Schedules the next movement pulse after a separate takeoff or alarm pulse.</summary>
        public void BeginMovementSound(AnimalSoundKind soundKind)
        {
            currentMovementSoundKind = soundKind;
            hasMovementSoundKind = true;
            movementSoundCountdown = ChooseRepeatInterval(soundKind);
        }

        public void TickRepeated(
            AnimalSoundKind soundKind,
            ref float countdown,
            float deltaTime)
        {
            countdown -= Mathf.Max(0f, deltaTime);
            if (countdown > 0f)
                return;

            Emit(soundKind);
            countdown = ChooseRepeatInterval(soundKind);
        }

        public float ChooseRepeatInterval(AnimalSoundKind soundKind)
        {
            AnimalSoundWaveSettings settings = GetSettings(soundKind);
            if (settings == null)
                return 1f;

            return settings.ChooseRepeatInterval()
                   / Mathf.Max(0.1f, overallEmissionFrequencyMultiplier);
        }

        private AnimalSoundKind ChooseMovementSoundKind()
        {
            if (agent.CurrentState == AnimalState.Fleeing)
                return AnimalSoundKind.Fleeing;

            // Flight over a river is still wing movement, not swimming.
            if (agent.Motor.IsAerialMovement)
                return AnimalSoundKind.Flying;

            if (agent.Map != null
                && agent.Map.TrySampleStaticWaterMapPosition(
                    agent.Motor.CurrentMapPosition,
                    out float depthMeters)
                && depthMeters > 0.01f)
            {
                return AnimalSoundKind.MovingInWater;
            }

            return AnimalSoundKind.MovingOnLand;
        }

        private AnimalSoundWaveSettings GetSettings(AnimalSoundKind soundKind)
        {
            switch (soundKind)
            {
                case AnimalSoundKind.Idle:
                    return idleSound;
                case AnimalSoundKind.Looking:
                    return lookingSound;
                case AnimalSoundKind.Curious:
                    return curiousSound;
                case AnimalSoundKind.Eating:
                    return eatingSound;
                case AnimalSoundKind.MovingOnLand:
                    return landMovementSound;
                case AnimalSoundKind.MovingInWater:
                    return waterMovementSound;
                case AnimalSoundKind.Fleeing:
                    return fleeingSound;
                case AnimalSoundKind.Submerging:
                    return submergingSound;
                case AnimalSoundKind.Surfacing:
                    return surfacingSound;
                case AnimalSoundKind.Flying:
                    return flyingSound;
                case AnimalSoundKind.Pecking:
                    return peckingSound;
                case AnimalSoundKind.Takeoff:
                    return takeoffSound;
                case AnimalSoundKind.Landing:
                    return landingSound;
                case AnimalSoundKind.EnteringTree:
                    return enteringTreeSound;
                case AnimalSoundKind.EmergingFromTree:
                    return emergingFromTreeSound;
                default:
                    return null;
            }
        }

        private void OnDrawGizmosSelected()
        {
            if (!showSoundRangeGizmos)
                return;

            DrawSoundRange(landMovementSound, new Color(0.7f, 0.85f, 1f, 0.6f));
            DrawSoundRange(eatingSound, new Color(0.75f, 1f, 0.65f, 0.55f));
            DrawSoundRange(fleeingSound, new Color(1f, 0.7f, 0.35f, 0.65f));
            DrawSoundRange(surfacingSound, new Color(0.3f, 0.75f, 1f, 0.65f));
            DrawSoundRange(flyingSound, new Color(0.7f, 0.85f, 1f, 0.6f));
            DrawSoundRange(peckingSound, new Color(0.75f, 1f, 0.65f, 0.55f));
        }

        private void DrawSoundRange(
            AnimalSoundWaveSettings settings,
            Color color)
        {
            if (settings == null || !settings.Enabled)
                return;

            const int segments = 64;
            float radius = settings.MaximumRadiusMeters
                           * Mathf.Max(0.05f, overallRadiusMultiplier);
            Vector3 centre = transform.TransformPoint(
                new Vector3(soundOriginOffset.x, soundOriginOffset.y, 0f));
            Gizmos.color = color;
            Vector3 previous = centre + Vector3.right * radius;
            for (int index = 1; index <= segments; index++)
            {
                float angle = index / (float)segments * Mathf.PI * 2f;
                Vector3 next = centre
                               + new Vector3(
                                   Mathf.Cos(angle),
                                   Mathf.Sin(angle),
                                   0f)
                               * radius;
                Gizmos.DrawLine(previous, next);
                previous = next;
            }
        }

        private void OnValidate()
        {
            ringBreakup = Mathf.Clamp01(ringBreakup);
            ringIrregularity = Mathf.Clamp01(ringIrregularity);
            overallRadiusMultiplier = Mathf.Clamp(
                overallRadiusMultiplier,
                0.05f,
                2f);
            overallRingCountMultiplier = Mathf.Clamp(
                overallRingCountMultiplier,
                0.1f,
                2f);
            overallEmissionFrequencyMultiplier = Mathf.Clamp(
                overallEmissionFrequencyMultiplier,
                0.1f,
                2f);
            idleSound?.ClampValues();
            lookingSound?.ClampValues();
            curiousSound?.ClampValues();
            eatingSound?.ClampValues();
            landMovementSound?.ClampValues();
            waterMovementSound?.ClampValues();
            fleeingSound?.ClampValues();
            submergingSound?.ClampValues();
            surfacingSound?.ClampValues();
            flyingSound?.ClampValues();
            peckingSound?.ClampValues();
            takeoffSound?.ClampValues();
            landingSound?.ClampValues();
            enteringTreeSound?.ClampValues();
            emergingFromTreeSound?.ClampValues();
        }
    }
}
