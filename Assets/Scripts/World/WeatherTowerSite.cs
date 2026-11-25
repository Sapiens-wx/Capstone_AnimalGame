using AnimalGame.Demos.CheckpointRefit;
using AnimalGame.Interaction;
using UnityEngine;

namespace AnimalGame.World
{
    public sealed partial class WeatherTower
    {
        [Header("Open Ground Under Elevated Deck")]
        [SerializeField] private SpriteRenderer groundRoofArt;
        [SerializeField] private Material siteLineMaterial;
        [SerializeField] private Vector2[] upperOutline;
        [SerializeField] private Vector2[] upperInset;
        [Tooltip("In order: main lift lower, main lift upper, east lift upper, east lift lower.")]
        [SerializeField] private Vector2[] liftDoorCenters;

        private enum Landing { MainLower, MainUpper, EastUpper, EastLower }
        private static readonly int RequirePlayerDisk = Shader.PropertyToID("_RequirePlayerUiClip");
        private static readonly int InteractionLightDim = Shader.PropertyToID("_WeatherTowerInteractionDim");
        private const float BlinkPeriod = 1.8f;
        private const float BlinkOnSeconds = 1.1f;
        private const float DoorThickness = .16f;
        private const float DoorSlideSeconds = .36f;
        private const float CallReach = 1.2f;
        private const float ReminderInterval = 2.2f;
        private const float ReminderDuration = .9f;
        private readonly LiftParking mainParking = new();
        private readonly LiftParking eastParking = new();
        private readonly GameObject[] landingBarriers = new GameObject[4];
        private readonly float[] landingDoorProgress = new float[4];
        private WeatherTowerSiteLines doorLines;
        private WeatherTowerSiteLines deckLines;
        private StationLogConsole logConsole;
        private CheckpointRefitTerminal repairTerminal;
        private CommunicationsStation communicationsConsole;
        private bool HasOpenGround => siteLineMaterial != null;

        private sealed class LiftParking
        {
            public bool AtUpper { get; private set; }
            public bool IsCalling => callTime > 0f;
            private float callTime;

            public void Call(float duration) => callTime = duration;
            public void Dock(bool upstairs) => AtUpper = upstairs;

            public void Tick(float deltaTime)
            {
                if (!IsCalling) return;
                callTime = Mathf.MoveTowards(callTime, 0f, deltaTime);
                if (!IsCalling) Dock(!AtUpper);
            }
        }

        private void InitializeSite()
        {
            doorLines = new WeatherTowerSiteLines(transform, siteLineMaterial, "Animated entrances", -16);
            deckLines = new WeatherTowerSiteLines(transform, siteLineMaterial, "Overhead deck outline", -18);
            logConsole = groundView.GetComponentInChildren<StationLogConsole>(true);
            repairTerminal = groundView.GetComponentInChildren<CheckpointRefitTerminal>(true);
            communicationsConsole = GetComponentInChildren<CommunicationsStation>(true);
            AddDoorBarrier(Landing.MainLower, groundView.transform);
            AddDoorBarrier(Landing.MainUpper, roofView.transform);
            AddDoorBarrier(Landing.EastUpper, roofView.transform);
            AddDoorBarrier(Landing.EastLower, exteriorView.transform);
            WorldInteractionHint.ReplaceLegacyPrompt(liftPrompt);
            WorldInteractionHint.ReplaceLegacyPrompt(exitLiftPrompt);
        }

        private void AddDoorBarrier(Landing landing, Transform floor)
        {
            var barrier = new GameObject($"{landing} lift door");
            barrier.SetActive(false);
            barrier.transform.SetParent(floor, false);
            barrier.transform.position = transform.TransformPoint(liftDoorCenters[(int)landing]);
            var interaction = barrier.AddComponent<WorldInteraction>();
            interaction.RequiresQueryScope = floor != exteriorView.transform;
            interaction.SetKind(WorldInteractionKind.Collision);
            interaction.ColliderType = ColliderType.Custom;
            interaction.ColliderShape = WorldInteractionColliderShape.Box;
            interaction.LocalSize = new Vector2(DoorThickness, liftSize.y + DoorThickness);
            landingBarriers[(int)landing] = barrier;
        }

        private LiftParking Parking(Landing landing) => IsEast(landing) ? eastParking : mainParking;
        private static bool IsEast(Landing landing) => landing == Landing.EastLower || landing == Landing.EastUpper;
        private static bool IsUpper(Landing landing) => landing == Landing.MainUpper || landing == Landing.EastUpper;

        private bool IsLiftDockedAt(Landing landing)
        {
            LiftParking parking = Parking(landing);
            return EquipmentInteractionReady && parking.AtUpper == IsUpper(landing) && !parking.IsCalling
                && !(IsRiding && ridingExitLift == IsEast(landing));
        }

        private void OnEnable()
        {
            if (doorLines != null) UpdateLandingDoors(1f);
        }

        private void UpdateLandingDoors(float normalizedStep)
        {
            WeatherTowerPhase view = IsRiding
                ? (OnUpperView ? WeatherTowerPhase.Outdoor : ridingExitLift ? WeatherTowerPhase.Exterior : WeatherTowerPhase.Ground)
                : Phase;
            for (int index = 0; index < landingBarriers.Length; index++)
            {
                var landing = (Landing)index;
                bool docked = IsLiftDockedAt(landing);
                bool onFloor = view == (IsUpper(landing) ? WeatherTowerPhase.Outdoor
                    : IsEast(landing) ? WeatherTowerPhase.Exterior : WeatherTowerPhase.Ground);
                float open = Mathf.MoveTowards(landingDoorProgress[index], onFloor && docked ? 1f : 0f, normalizedStep);
                landingDoorProgress[index] = open;
                // Only the active floor waits for the visible panels to clear the doorway.
                SetActive(landingBarriers[index], !docked || (onFloor && open < 1f));
            }
        }

        private bool LiftDoorsClosed(bool east)
        {
            Landing lower = east ? Landing.EastLower : Landing.MainLower;
            Landing upper = east ? Landing.EastUpper : Landing.MainUpper;
            return landingDoorProgress[(int)lower] == 0f && landingDoorProgress[(int)upper] == 0f;
        }

        private void TickLiftSite(float deltaTime)
        {
            if (LiftDoorsClosed(false)) mainParking.Tick(deltaTime);
            if (LiftDoorsClosed(true)) eastParking.Tick(deltaTime);
            UpdateLandingDoors(deltaTime / DoorSlideSeconds);
        }

        private void DockRiddenLift(bool upstairs) => (ridingExitLift ? eastParking : mainParking).Dock(upstairs);

        private Landing MainLanding => Phase == WeatherTowerPhase.Outdoor ? Landing.MainUpper : Landing.MainLower;
        private Landing EastLanding => Phase == WeatherTowerPhase.Outdoor ? Landing.EastUpper : Landing.EastLower;

        private Landing? AvailableLiftCall()
        {
            if (!HasOpenGround || !CanUseLiftControls()) return null;
            Vector2 local = transform.InverseTransformPoint(player.transform.position);
            if (Phase != WeatherTowerPhase.Exterior && CanCall(MainLanding, local))
                return MainLanding;
            return Phase != WeatherTowerPhase.Ground && CanCall(EastLanding, local) ? EastLanding : null;
        }

        private bool CanCall(Landing landing, Vector2 local)
        {
            LiftParking parking = Parking(landing);
            if (parking.IsCalling || parking.AtUpper == IsUpper(landing)) return false;
            Vector2 door = liftDoorCenters[(int)landing];
            float approachDistance = (local.x - door.x) * ApproachSide(landing);
            return approachDistance >= 0f && approachDistance <= CallReach
                && Mathf.Abs(local.y - door.y) + Clearance(transform, Vector2.up) < liftSize.y * .5f;
        }

        private static float ApproachSide(Landing landing) =>
            landing == Landing.MainLower || landing == Landing.EastUpper ? -1f : 1f;

        private void StartLiftCall(Landing landing)
        {
            Parking(landing).Call(travelSeconds);
            UpdateLandingDoors(0f);
            SetLiftPrompt(null);
        }

        private bool ClearOfLiftDoors(Transform anchor)
        {
            int west = anchor == lift ? (int)Landing.MainLower : (int)Landing.EastUpper;
            float x = transform.InverseTransformPoint(player.transform.position).x;
            float margin = Clearance(transform, Vector2.right) + DoorThickness * .5f;
            return x > liftDoorCenters[west].x + margin && x < liftDoorCenters[west + 1].x - margin;
        }

        private void PositionLiftPrompt(GameObject prompt, Transform anchor, Landing? call)
        {
            float offset = 0f;
            if (call.HasValue)
            {
                Landing landing = call.Value;
                offset = liftDoorCenters[(int)landing].x - anchor.localPosition.x + .43f * ApproachSide(landing);
            }
            Vector3 position = prompt.transform.localPosition;
            position.x = offset;
            prompt.transform.localPosition = position;
        }

        private void LateUpdate()
        {
            if (doorLines == null) return;
            float dim = Mathf.Repeat(Time.time, BlinkPeriod) < BlinkOnSeconds ? 0f : .8f;
            bool powered = IsPowered;
            float below = 1f - UpperFloorOpacity;
            if (powerLines != null)
                DrawPowerSocket(powered || SocketIsLit(WeatherTowerSocket.Power) ? 0f : dim, below);
            DrawDeckOutline(below);
            DrawEntrances(below, dim);
        }

        private void DrawDeckOutline(float opacity)
        {
            if (opacity <= 0f)
            {
                deckLines.Hide();
                return;
            }
            float offset = Time.time * -.62f;
            deckLines.Begin();
            deckLines.UpperOutline(upperOutline, opacity * .55f, offset);
            deckLines.UpperOutline(upperInset, opacity * .36f, offset);
            deckLines.Draw();
        }

        private void DrawEntrances(float below, float dim)
        {
            // Both the painted strips and the lift panels switch together on the gameplay clock.
            Shader.SetGlobalFloat(InteractionLightDim, dim);
            float accent = EquipmentAccentVisibility;
            float visibility = EquipmentVisibility;
            float offset = Time.time * -.76f;
            doorLines.Begin();
            if (below > 0f)
                doorLines.Door(groundDoor.localPosition, groundDoorWidth, 1f, offset, below, accent);
            for (int index = 0; index < landingDoorProgress.Length; index++)
                doorLines.Door(liftDoorCenters[index], liftSize.y,
                    Mathf.SmoothStep(0f, 1f, landingDoorProgress[index]), offset, light: accent);
            if (visibility > 0f)
            {
                float blink = accent >= 1f ? dim : 0f;
                DrawLiftControls(WeatherTowerSocket.MainNorth, WeatherTowerSocket.MainSouth, blink, visibility, accent);
                DrawLiftControls(WeatherTowerSocket.EastNorth, WeatherTowerSocket.EastSouth, blink, visibility, accent);
            }
            if (accent > 0f) DrawEquipmentReminders(accent);
            doorLines.Draw();
        }

        private void DrawEquipmentReminders(float opacity)
        {
            if (Phase == WeatherTowerPhase.Ground)
            {
                if (logConsole != null && logConsole.isActiveAndEnabled && logConsole.HasPendingInteraction)
                    DrawEquipmentReminder(WeatherTowerSocket.Log, opacity, 0f);
                if (repairTerminal != null && repairTerminal.isActiveAndEnabled && repairTerminal.HasPendingInteraction)
                    DrawEquipmentReminder(WeatherTowerSocket.Refit, opacity, .33f);
            }
            else if (Phase == WeatherTowerPhase.Outdoor && communicationsConsole != null
                && communicationsConsole.isActiveAndEnabled && communicationsConsole.HasPendingInteraction)
                DrawEquipmentReminder(WeatherTowerSocket.Communications, opacity, 0f);
        }

        private void DrawEquipmentReminder(WeatherTowerSocket socket, float opacity, float offset)
        {
            float elapsed = Mathf.Repeat(Time.time - offset, ReminderInterval);
            if (elapsed >= ReminderDuration) return;

            float progress = elapsed / ReminderDuration;
            float radius = Mathf.Lerp(.28f, 1.22f, 1f - (1f - progress) * (1f - progress));
            float fade = Mathf.SmoothStep(0f, 1f, progress / .12f) * (1f - progress);
            doorLines.ReminderRing(SocketPosition(socket), radius, .035f, opacity * fade * .82f);
        }

        private void DrawLiftControls(WeatherTowerSocket north, WeatherTowerSocket south, float dim, float opacity, float accent)
        {
            doorLines.SocketIndicator(LiftSocketPosition(north), Vector2.right, SocketIsLit(north) ? 0f : dim,
                opacity, accentBlend: accent);
            doorLines.SocketIndicator(LiftSocketPosition(south), Vector2.right, SocketIsLit(south) ? 0f : dim,
                opacity, accentBlend: accent);
        }
    }
}
