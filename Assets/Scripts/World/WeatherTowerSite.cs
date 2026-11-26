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
        private const float RoofViewReach = .30f;
        private const float RoofViewCornerBlend = .12f;
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
            var properties = new MaterialPropertyBlock();
            foreach (SpriteRenderer part in GetComponentsInChildren<SpriteRenderer>(true))
            {
                if (part.sharedMaterial != groundRoofArt.sharedMaterial) continue;
                part.GetPropertyBlock(properties);
                properties.SetFloat(RequirePlayerDisk, 1f);
                part.SetPropertyBlock(properties);
            }
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
            return parking.AtUpper == IsUpper(landing) && !parking.IsCalling
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
            float below = 1f - UpperFloorOpacity;
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

        private Vector2 RoofViewBias(Vector2 playerWorldPosition, float viewRadius)
        {
            Matrix4x4 roofToWorld = transform.localToWorldMatrix;
            Vector2 last = (Vector2)roofToWorld.MultiplyPoint3x4(upperOutline[^1]) - playerWorldPosition;
            float nearestDistance = float.PositiveInfinity;
            float winding = 0f;
            Vector2 start = last;
            foreach (Vector2 vertex in upperOutline)
            {
                Vector2 end = (Vector2)roofToWorld.MultiplyPoint3x4(vertex) - playerWorldPosition;
                winding += start.x * end.y - start.y * end.x;
                nearestDistance = Mathf.Min(nearestDistance, DistanceToRoofEdge(start, end - start));
                start = end;
            }

            float reach = viewRadius * RoofViewReach;
            if (nearestDistance >= reach) return Vector2.zero;

            // Blend outward normals where two sides are similarly close, so corners turn continuously
            // and opposing edges cancel near the middle of the roof.
            float blendRange = viewRadius * RoofViewCornerBlend;
            float outward = Mathf.Sign(winding);
            float totalWeight = 0f;
            Vector2 bias = Vector2.zero;
            start = last;
            foreach (Vector2 vertex in upperOutline)
            {
                Vector2 end = (Vector2)roofToWorld.MultiplyPoint3x4(vertex) - playerWorldPosition;
                Vector2 edge = end - start;
                float distance = DistanceToRoofEdge(start, edge);
                float weight = 1f - Mathf.SmoothStep(0f, 1f, (distance - nearestDistance) / blendRange);
                bias += new Vector2(edge.y, -edge.x).normalized * (outward * weight);
                totalWeight += weight;
                start = end;
            }

            float strength = 1f - Mathf.SmoothStep(0f, 1f, nearestDistance / reach);
            return bias * (strength / totalWeight);
        }

        // The edge endpoints are already relative to the player.
        private static float DistanceToRoofEdge(Vector2 start, Vector2 edge)
        {
            float along = Mathf.Clamp01(-Vector2.Dot(start, edge) / edge.sqrMagnitude);
            return (start + along * edge).magnitude;
        }

        private void DrawEntrances(float below, float dim)
        {
            // Both the painted strips and the lift panels switch together on the gameplay clock.
            Shader.SetGlobalFloat(InteractionLightDim, dim);
            float offset = Time.time * -.76f;
            doorLines.Begin();
            if (below > 0f)
                doorLines.Door(groundDoor.localPosition, groundDoorWidth, 1f, offset, below);
            for (int index = 0; index < landingDoorProgress.Length; index++)
                doorLines.Door(liftDoorCenters[index], liftSize.y,
                    Mathf.SmoothStep(0f, 1f, landingDoorProgress[index]), offset);
            DrawLiftControls(WeatherTowerSocket.MainNorth, WeatherTowerSocket.MainSouth, dim);
            DrawLiftControls(WeatherTowerSocket.EastNorth, WeatherTowerSocket.EastSouth, dim);
            DrawEquipmentReminders();
            doorLines.Draw();
        }

        private void DrawEquipmentReminders()
        {
            if (Phase == WeatherTowerPhase.Ground)
            {
                if (logConsole != null && logConsole.isActiveAndEnabled && logConsole.HasPendingInteraction)
                    DrawEquipmentReminder(WeatherTowerSocket.Log, 0f);
                if (repairTerminal != null && repairTerminal.isActiveAndEnabled && repairTerminal.HasPendingInteraction)
                    DrawEquipmentReminder(WeatherTowerSocket.Refit, .33f);
            }
            else if (Phase == WeatherTowerPhase.Outdoor && communicationsConsole != null
                && communicationsConsole.isActiveAndEnabled && communicationsConsole.HasPendingInteraction)
                DrawEquipmentReminder(WeatherTowerSocket.Communications, 0f);
        }

        private void DrawEquipmentReminder(WeatherTowerSocket socket, float offset)
        {
            float elapsed = Mathf.Repeat(Time.time - offset, ReminderInterval);
            if (elapsed >= ReminderDuration) return;

            float progress = elapsed / ReminderDuration;
            float radius = Mathf.Lerp(.28f, 1.22f, 1f - (1f - progress) * (1f - progress));
            float fade = Mathf.SmoothStep(0f, 1f, progress / .12f) * (1f - progress);
            doorLines.ReminderRing(SocketPosition(socket), radius, .035f, fade * .82f);
        }

        private void DrawLiftControls(WeatherTowerSocket north, WeatherTowerSocket south, float dim)
        {
            doorLines.SocketIndicator(LiftSocketPosition(north), SocketIsLit(north) ? 0f : dim);
            doorLines.SocketIndicator(LiftSocketPosition(south), SocketIsLit(south) ? 0f : dim);
        }
    }
}
