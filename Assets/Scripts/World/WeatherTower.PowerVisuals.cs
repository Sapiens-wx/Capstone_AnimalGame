using System.Collections.Generic;
using AnimalGame.Garbage;
using UnityEngine;

namespace AnimalGame.World
{
    public sealed partial class WeatherTower
    {
        [SerializeField] private Sprite unpoweredFloor;
        [SerializeField] private Sprite unpoweredUpper;

        private const float LightsReadyAt = .36f;
        private const float EquipmentVisibleAt = 1.2f;
        private const float EquipmentAccentAt = 1.44f;
        private const float PowerSequenceEnd = 1.66f;
        private static readonly int TowerPowerReveal = Shader.PropertyToID("_TowerPowerReveal");
        private static readonly int TowerRoomLight = Shader.PropertyToID("_TowerRoomLight");
        private static readonly int TowerInteractionReveal = Shader.PropertyToID("_TowerInteractionReveal");
        private static readonly int TowerUnpoweredTex = Shader.PropertyToID("_TowerUnpoweredTex");
        private readonly List<GameObject> hiddenGarbage = new();
        private MaterialPropertyBlock powerProperties;
        private WeatherTowerSiteLines powerLines;
        private float powerOnTime = PowerSequenceEnd;

        internal bool EquipmentInteractionReady => IsPowered && powerOnTime > EquipmentAccentAt;
        internal float EquipmentAccentVisibility => PowerVisibility(EquipmentAccentAt, PowerSequenceEnd);
        private float EquipmentVisibility => PowerVisibility(LightsReadyAt, EquipmentVisibleAt);

        private float PowerVisibility(float start, float end) =>
            Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(start, end, powerOnTime));

        private void InitializePowerVisuals()
        {
            powerProperties = new MaterialPropertyBlock();
            foreach (SpriteRenderer part in GetComponentsInChildren<SpriteRenderer>(true))
            {
                if (part.sharedMaterial != groundRoofArt.sharedMaterial) continue;
                part.GetPropertyBlock(powerProperties);
                powerProperties.SetFloat(RequirePlayerDisk, 1f);
                part.SetPropertyBlock(powerProperties);
            }

            if (powerSocket == null) return;
            if (!IsPowered)
            {
                powerOnTime = 0f;
                HideUnlitGarbage(groundView);
                HideUnlitGarbage(roofView);
            }
            ApplyFloorPower(groundArt, unpoweredFloor);
            ApplyFloorPower(roofArt, unpoweredUpper);
            // Keep the wall-mounted indicator in front of the entrance artwork.
            powerLines = new WeatherTowerSiteLines(transform, siteLineMaterial, "Power socket light", 1);
        }

        private void TickPowerVisuals(float deltaTime)
        {
            if (powerOnTime >= PowerSequenceEnd || !IsPowered || deltaTime <= 0f) return;
            powerOnTime = Mathf.MoveTowards(powerOnTime, PowerSequenceEnd, deltaTime);
            ApplyFloorPower(groundArt, unpoweredFloor);
            ApplyFloorPower(roofArt, unpoweredUpper);
            if (powerOnTime < EquipmentVisibleAt || hiddenGarbage.Count == 0) return;
            foreach (GameObject garbage in hiddenGarbage)
                if (garbage != null) garbage.SetActive(true);
            hiddenGarbage.Clear();
        }

        private void HideUnlitGarbage(GameObject floor)
        {
            foreach (GarbageItem item in floor.GetComponentsInChildren<GarbageItem>(true))
            {
                if (!item.gameObject.activeSelf) continue;
                hiddenGarbage.Add(item.gameObject);
                item.gameObject.SetActive(false);
            }
        }

        private void ApplyFloorPower(SpriteRenderer floor, Sprite structure)
        {
            floor.GetPropertyBlock(powerProperties);
            powerProperties.SetTexture(TowerUnpoweredTex, structure.texture);
            powerProperties.SetFloat(TowerPowerReveal, EquipmentVisibility);
            powerProperties.SetFloat(TowerRoomLight, Mathf.Lerp(.65f, 1f, PowerVisibility(0f, LightsReadyAt)));
            powerProperties.SetFloat(TowerInteractionReveal, EquipmentAccentVisibility);
            floor.SetPropertyBlock(powerProperties);
        }

        private void DrawPowerSocket(float dim, float opacity)
        {
            if (opacity <= 0f)
            {
                powerLines.Hide();
                return;
            }

            powerLines.Begin();
            float light = PowerVisibility(0f, LightsReadyAt);
            float pulse = 4f * light * (1f - light);
            powerLines.SocketIndicator(PowerContact, Vector2.up, dim, opacity, 1f + .8f * pulse);
            powerLines.Draw();
        }
    }
}
