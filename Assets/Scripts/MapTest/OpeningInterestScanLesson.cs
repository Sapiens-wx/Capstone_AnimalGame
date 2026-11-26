using AnimalGame.RobotMap;
using AnimalGame.Save;
using AnimalGame.World;
using UnityEngine;
using GameHud = AnimalGame.MainUI.MainUI;

namespace AnimalGame.MapTest
{
    [DisallowMultipleComponent]
    internal sealed class OpeningInterestScanLesson : MonoBehaviour
    {
        private WeatherTower tower;
        private RobotMover robot;
        private GameHud hud;
        private ScanChargeUI scanInput;
        private string stationId;
        private Vector2 lineSouth, line, outward, previousPosition;

        internal void Bind(WeatherTower exitTower, RobotMover player, GameHud gameHud, ScanChargeUI scan,
            Vector2 south, Vector2 north)
        {
            tower = exitTower;
            robot = player;
            hud = gameHud;
            scanInput = scan;
            stationId = tower.GetComponentInChildren<StationLogConsole>(true).StationId;
            lineSouth = south;
            line = north - south;
            outward = new Vector2(line.y, -line.x);
            enabled = false;
            tower.PlayerArrivedAtExitLift += OnExitLiftArrived;
        }

        private void OnExitLiftArrived()
        {
            if (!SaveSystem.IsStationSynced(stationId) || !SaveSystem.IsRepairTutorialComplete(stationId)
                || !SaveSystem.HasViewedStationCommunications(stationId))
                return;

            previousPosition = robot.MapPosition;
            enabled = true;
        }

        private void LateUpdate()
        {
            Vector2 previous = previousPosition;
            Vector2 position = robot.MapPosition;
            previousPosition = position;
            if (tower.Phase != WeatherTowerPhase.Exterior || robot.MovementMode != RobotMovementMode.Driven
                || robot.IgnoreCollisionAndTerrain || Time.timeScale <= 0f || GameHud.BlocksGameplay)
                return;

            float before = Vector2.Dot(outward, previous - lineSouth);
            float after = Vector2.Dot(outward, position - lineSouth);
            if (before > 0f || after <= 0f) return;
            Vector2 crossing = Vector2.Lerp(previous, position, -before / (after - before));
            float along = Vector2.Dot(line, crossing - lineSouth);
            if (along < 0f || along > line.sqrMagnitude) return;

            enabled = false;
            tower.PlayerArrivedAtExitLift -= OnExitLiftArrived;
            scanInput.FullyChargedBiologicalScanReleased += OnScanReleased;
            hud.ShowOpeningInterestScanHint();
        }

        private void OnScanReleased()
        {
            scanInput.FullyChargedBiologicalScanReleased -= OnScanReleased;
            hud.DismissOpeningInterestScanHint();
        }

        private void OnDestroy()
        {
            if (tower != null) tower.PlayerArrivedAtExitLift -= OnExitLiftArrived;
            if (scanInput != null) scanInput.FullyChargedBiologicalScanReleased -= OnScanReleased;
        }
    }
}
