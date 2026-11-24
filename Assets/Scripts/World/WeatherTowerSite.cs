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
        private const float CallReach = 1.2f;
        private readonly LiftParking mainParking = new();
        private readonly LiftParking eastParking = new();
        private readonly GameObject[] landingBarriers = new GameObject[4];
        private WeatherTowerSiteLines doorLines;
        private WeatherTowerSiteLines deckLines;
        private bool HasOpenGround => siteLineMaterial != null;

        private sealed class LiftParking
        {
            public bool AtUpper { get; private set; }
            public bool IsCalling => callTime > 0f;
            private float callTime;

            public void Call(float duration) => callTime = duration;
            public void Dock(bool upstairs) => AtUpper = upstairs;

            public bool Tick(float deltaTime)
            {
                if (!IsCalling) return false;
                callTime = Mathf.MoveTowards(callTime, 0f, deltaTime);
                if (IsCalling) return false;
                Dock(!AtUpper);
                return true;
            }
        }

        private void InitializeSite()
        {
            // Preserve the prefab preview in Edit Mode, and require a live gameplay disk at runtime.
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

        private bool IsLandingOpen(Landing landing)
        {
            LiftParking parking = Parking(landing);
            return parking.AtUpper == IsUpper(landing) && !parking.IsCalling
                && !(IsRiding && ridingExitLift == IsEast(landing));
        }

        private void RefreshLandings()
        {
            for (int index = 0; index < landingBarriers.Length; index++)
                SetActive(landingBarriers[index], !IsLandingOpen((Landing)index));
        }

        private void TickLiftCalls(float deltaTime)
        {
            bool mainDocked = mainParking.Tick(deltaTime);
            bool eastDocked = eastParking.Tick(deltaTime);
            if (mainDocked || eastDocked) RefreshLandings();
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
            RefreshLandings();
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
            float below = 1f - UpperFloorOpacity;
            DrawDeckOutline(below);
            DrawEntrances(below);
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

        private void DrawEntrances(float below)
        {
            // Both the painted strips and the lift panels switch together on the gameplay clock.
            float dim = Mathf.Repeat(Time.time, BlinkPeriod) < BlinkOnSeconds ? 0f : .8f;
            Shader.SetGlobalFloat(InteractionLightDim, dim);
            float offset = Time.time * -.76f;
            WeatherTowerPhase view = IsRiding
                ? (OnUpperView ? WeatherTowerPhase.Outdoor : ridingExitLift ? WeatherTowerPhase.Exterior : WeatherTowerPhase.Ground)
                : Phase;
            doorLines.Begin();
            if (below > 0f)
                doorLines.Door(groundDoor.localPosition, groundDoorWidth, true, offset, below);
            DrawLanding(Landing.MainLower, view == WeatherTowerPhase.Ground, offset);
            DrawLanding(Landing.MainUpper, view == WeatherTowerPhase.Outdoor, offset);
            DrawLanding(Landing.EastUpper, view == WeatherTowerPhase.Outdoor, offset);
            DrawLanding(Landing.EastLower, view == WeatherTowerPhase.Exterior, offset);
            DrawLiftControls(WeatherTowerSocket.MainNorth, WeatherTowerSocket.MainSouth, dim);
            DrawLiftControls(WeatherTowerSocket.EastNorth, WeatherTowerSocket.EastSouth, dim);
            doorLines.Draw();
        }

        private void DrawLanding(Landing landing, bool accessible, float offset) =>
            doorLines.Door(liftDoorCenters[(int)landing], liftSize.y, accessible && IsLandingOpen(landing), offset);

        private void DrawLiftControls(WeatherTowerSocket north, WeatherTowerSocket south, float dim)
        {
            doorLines.LiftControl(LiftSocketPosition(north), SocketIsLit(north) ? 0f : dim);
            doorLines.LiftControl(LiftSocketPosition(south), SocketIsLit(south) ? 0f : dim);
        }
    }
}
