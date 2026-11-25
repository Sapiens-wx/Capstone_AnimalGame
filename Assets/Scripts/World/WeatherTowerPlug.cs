using System;
using AnimalGame.Interaction;
using AnimalGame.RobotMap;
using AnimalGame.Save;
using UnityEngine;
using GameHud = AnimalGame.MainUI.MainUI;

namespace AnimalGame.World
{
    public enum WeatherTowerSocket { Log, Cargo, Refit, Communications, MainNorth, MainSouth, EastNorth, EastSouth, Power }

    public sealed partial class WeatherTower
    {
        [Header("Plug contacts in station-local artwork coordinates")]
        [SerializeField] private Vector2 logContact;
        [SerializeField] private Vector2 cargoContact;
        [SerializeField] private Vector2 refitContact;
        [SerializeField] private Vector2 communicationContact;
        [SerializeField] private RuinDiscovery ruin;
        [SerializeField] private WorldInteractionHint powerSocket;

        private enum PlugPhase { Idle, Approaching, Seated, Withdrawing }
        private PlugPhase plugPhase;
        private WeatherTowerSocket plugSocket;
        private MonoBehaviour plugTarget;
        private Func<bool> activatePlug, plugInUse;
        private Landing? plugLanding;
        private bool callingWithPlug;
        private static readonly int ConnectedSocketUV = Shader.PropertyToID("_ConnectedSocketUV");
        private MaterialPropertyBlock socketProperties;

        public bool UsesStationPlugs => HasOpenGround;
        public bool IsPowered => powerSocket == null || SaveSystem.IsStationPowered(ruin.Id);
        private Vector2 PowerContact => powerSocket.transform.localPosition;

        // Insertion axes point into the equipment. Coordinates are in the registered WX04 floor artwork.
        private Vector2 SocketPosition(WeatherTowerSocket socket) => socket switch
        {
            WeatherTowerSocket.Log => logContact,
            WeatherTowerSocket.Cargo => cargoContact,
            WeatherTowerSocket.Refit => refitContact,
            WeatherTowerSocket.Communications => communicationContact,
            WeatherTowerSocket.Power => PowerContact,
            _ => LiftSocketPosition(socket)
        };

        private Vector2 InsertionAxis(WeatherTowerSocket socket) => socket switch
        {
            WeatherTowerSocket.Cargo or WeatherTowerSocket.MainSouth or WeatherTowerSocket.EastSouth => Vector2.down,
            WeatherTowerSocket.Refit => Vector2.left,
            WeatherTowerSocket.Power => Phase == WeatherTowerPhase.Exterior ? Vector2.right : Vector2.left,
            _ => Vector2.up
        };

        private Vector2 LiftSocketPosition(WeatherTowerSocket socket)
        {
            bool east = socket == WeatherTowerSocket.EastNorth || socket == WeatherTowerSocket.EastSouth;
            Vector2 axis = InsertionAxis(socket);
            Landing landing = east ? EastLanding : MainLanding;
            // An empty shaft exposes the landing-side call contacts outside its closed barrier.
            if (!IsRiding && Parking(landing).AtUpper != IsUpper(landing))
                return liftDoorCenters[(int)landing] + Vector2.right * (.30f * ApproachSide(landing))
                    + axis * (liftSize.y * .5f);
            return (Vector2)(east ? exitLift : lift).localPosition + axis * 1.10f;
        }

        private bool CanReach(WeatherTowerSocket socket)
        {
            Vector2 point = transform.TransformPoint(SocketPosition(socket));
            Vector2 axis = transform.TransformVector(InsertionAxis(socket));
            return Vector2.Dot(point - (Vector2)player.transform.position, axis) > 0f
                && arm.CanReachStationPlug(point, axis);
        }

        public bool CanPlug(WeatherTowerSocket socket, RobotMover requestor) =>
            HasOpenGround && isActiveAndEnabled && plugPhase == PlugPhase.Idle
            && (socket == WeatherTowerSocket.Power ? !IsPowered : EquipmentInteractionReady)
            && requestor == player && IsScenePlayer(player) && arm != null && !arm.StationPlugActive && CanReach(socket);

        internal static bool CanClaimExteriorPlugPress(RobotMover requestor)
        {
            foreach (WeatherTower tower in FindObjectsByType<WeatherTower>(FindObjectsSortMode.None))
            {
                if (tower.player != requestor || tower.Phase != WeatherTowerPhase.Exterior || !tower.UsesStationPlugs)
                    continue;
                if (tower.CanUsePowerSocket()
                    || ((tower.AvailableLift() != null || tower.AvailableLiftCall().HasValue)
                        && tower.TryFindLiftSocket(tower.EastLanding, out _))) return true;
            }
            return false;
        }

        private bool CanUsePowerSocket() => (Phase == WeatherTowerPhase.Exterior || Phase == WeatherTowerPhase.Ground)
            && CanPlug(WeatherTowerSocket.Power, player) && WorldStationInteraction.CanUseControls(player, allowStationPlug: true);

        private void UpdatePowerInteraction(bool pressed)
        {
            bool available = CanUsePowerSocket();
            if (available && pressed)
                available = !TryPlug(WeatherTowerSocket.Power, player, this, RestoreStationPower, () => false);
            powerSocket.enabled = available;
        }

        private bool RestoreStationPower(Vector3? socketWorld)
        {
            try
            {
                if (!SaveSystem.PowerStation(ruin.Id)) return false;
            }
            catch (Exception error) when (error is System.IO.IOException || error is UnauthorizedAccessException)
            {
                Debug.LogException(error, this);
                return false;
            }
            return true;
        }

        public bool TryPlug(WeatherTowerSocket socket, RobotMover requestor, MonoBehaviour device,
            Func<Vector3?, bool> activate, Func<bool> isInUse)
        {
            if (!CanPlug(socket, requestor) || !BeginPlug(socket, out Vector3 socketWorld)) return false;
            plugTarget = device;
            activatePlug = () => activate(socketWorld);
            plugInUse = isInUse;
            return true;
        }

        private bool BeginPlug(WeatherTowerSocket socket, out Vector3 socketWorld)
        {
            socketWorld = transform.TransformPoint(SocketPosition(socket));
            if (!arm.TryBeginStationPlug(socketWorld, transform.TransformVector(InsertionAxis(socket)))) return false;
            plugSocket = socket;
            plugPhase = PlugPhase.Approaching;
            return true;
        }

        private bool LiftInputAvailable()
        {
            var hud = GameHud.Active;
            return !GameHud.BlocksGameplay && (hud == null || (!hud.IsInventoryOpen && hud.InventoryProgress <= .001f))
                && !AnimalGame.AnimalDebug.DebugUI.BlocksOtherPanels
                && !AnimalGame.Tutorial.TutorialTipOverLesson.BlocksGameplayForReading(player.gameObject);
        }

        private bool TryFindLiftSocket(Landing landing, out WeatherTowerSocket socket)
        {
            bool north = transform.InverseTransformPoint(player.transform.position).y >= liftDoorCenters[(int)landing].y;
            var top = IsEast(landing) ? WeatherTowerSocket.EastNorth : WeatherTowerSocket.MainNorth;
            var bottom = IsEast(landing) ? WeatherTowerSocket.EastSouth : WeatherTowerSocket.MainSouth;
            socket = north ? top : bottom;
            if (!LiftInputAvailable()) return false;
            if (CanPlug(socket, player)) return true;
            socket = north ? bottom : top;
            return CanPlug(socket, player);
        }

        private void BeginLiftPlug(WeatherTowerSocket socket, Landing landing, bool call)
        {
            if (!BeginPlug(socket, out _)) return;
            plugLanding = landing;
            callingWithPlug = call;
        }

        private bool LiftRequestStillValid(Landing landing, bool call)
        {
            if (!CanUseLiftControls() || !LiftInputAvailable()) return false;
            if (IsUpper(landing) != (Phase == WeatherTowerPhase.Outdoor)) return false;
            if (!IsUpper(landing) && IsEast(landing) != (Phase == WeatherTowerPhase.Exterior)) return false;
            return call ? CanCall(landing, transform.InverseTransformPoint(player.transform.position))
                : CanBoard(IsEast(landing) ? exitLift : lift, landing);
        }

        private void TickStationPlug()
        {
            if (plugPhase == PlugPhase.Idle) return;
            if (arm == null || (plugPhase != PlugPhase.Withdrawing && !plugLanding.HasValue
                && (plugTarget == null || !plugTarget.isActiveAndEnabled)))
            {
                CancelPlugInteraction();
                return;
            }
            if (!arm.StationPlugActive)
            {
                Landing? request = plugPhase == PlugPhase.Withdrawing ? plugLanding : null;
                bool call = callingWithPlug;
                ClearPlugInteraction();
                if (request.HasValue && Time.timeScale > 0f && LiftRequestStillValid(request.Value, call))
                {
                    if (call) StartLiftCall(request.Value);
                    else TryStartLift(IsEast(request.Value) ? exitLift : lift);
                }
                return;
            }

            if (plugPhase == PlugPhase.Seated)
            {
                if (!plugLanding.HasValue && plugInUse()) return;
                WithdrawPlug();
                return;
            }
            if (plugPhase != PlugPhase.Approaching || Time.timeScale <= 0f) return;
            if (!IsScenePlayer(player) || player.IgnoreCollisionAndTerrain || !CanReach(plugSocket))
            {
                CancelPlugInteraction();
                return;
            }
            if (!arm.StationPlugSeated) return;
            bool accepted = plugLanding.HasValue
                ? LiftRequestStillValid(plugLanding.Value, callingWithPlug)
                : activatePlug();
            if (!accepted)
            {
                plugLanding = null;
                WithdrawPlug();
                return;
            }
            plugPhase = PlugPhase.Seated;
            SetSocketLight(true);
        }

        private void WithdrawPlug()
        {
            if (plugPhase == PlugPhase.Seated) SetSocketLight(false);
            plugPhase = PlugPhase.Withdrawing;
            arm.RetractStationPlug();
        }

        private bool SocketIsLit(WeatherTowerSocket socket) => plugPhase == PlugPhase.Seated && plugSocket == socket;

        private void SetSocketLight(bool on)
        {
            if (plugSocket >= WeatherTowerSocket.MainNorth) return;
            SpriteRenderer art = plugSocket == WeatherTowerSocket.Communications ? roofArt : groundArt;
            Sprite sprite = art.sprite;
            Vector2 local = art.transform.InverseTransformPoint(transform.TransformPoint(SocketPosition(plugSocket)));
            Vector2 pixel = sprite.rect.position + sprite.pivot + local * sprite.pixelsPerUnit;
            Vector2 half = plugSocket == WeatherTowerSocket.Refit ? new Vector2(.13f, .40f) : new Vector2(.60f, .12f);
            half *= sprite.pixelsPerUnit;
            float w = sprite.texture.width, h = sprite.texture.height;
            socketProperties ??= new MaterialPropertyBlock();
            art.GetPropertyBlock(socketProperties);
            socketProperties.SetVector(ConnectedSocketUV, on
                ? new Vector4(pixel.x / w, pixel.y / h, half.x / w, half.y / h) : Vector4.zero);
            art.SetPropertyBlock(socketProperties);
        }

        private void ClearPlugInteraction()
        {
            if (plugPhase == PlugPhase.Seated) SetSocketLight(false);
            plugPhase = PlugPhase.Idle;
            plugTarget = null;
            plugLanding = null;
            activatePlug = null;
            plugInUse = null;
        }

        private void CancelPlugInteraction()
        {
            if (plugPhase == PlugPhase.Idle) return;
            if (arm != null) arm.CancelStationPlug();
            ClearPlugInteraction();
        }
    }
}
