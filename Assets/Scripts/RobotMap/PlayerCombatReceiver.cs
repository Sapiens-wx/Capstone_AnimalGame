using System;
using AnimalGame.MainUI.Inventory;
using UnityEngine;
using Hud = AnimalGame.MainUI.MainUI;

namespace AnimalGame.RobotMap
{
    [DisallowMultipleComponent]
    public sealed class PlayerCombatReceiver : MonoBehaviour
    {
        private const float OpeningDriveFaultRepairThreshold = .60f;

        [SerializeField, Min(1f)] private float maximumHealth = 100f;
        public float CurrentHealth { get; private set; }
        internal float Integrity => ui != null ? ui.Integrity : CurrentHealth / maximumHealth;
        internal Hud Hud => ui;
        public event Action<float> Damaged;
        public event Action<Vector2> Impacted;
        private RobotMover mover;
        private Hud ui;
        private Vector2 velocity;
        public bool IsDisplaced => velocity.sqrMagnitude > .001f;
        private void Awake()
        {
            mover = GetComponent<RobotMover>();
            CurrentHealth = maximumHealth;
            Hud sceneHud = FindFirstObjectByType<Hud>();
            if (sceneHud != null) BindHud(sceneHud);
        }

        internal void BindHud(Hud hud)
        {
            ui = hud;
            CurrentHealth = hud.Integrity * maximumHealth;
        }

        internal void InitializeIntegrity(Hud hud, float integrity)
        {
            hud.Integrity = integrity;
            BindHud(hud);
        }

        internal bool TryRepairIntegrity(InventoryItem material, int cost, float restoration)
        {
            if (ui == null) return false;
            float current = ui.Integrity;
            float repaired = Mathf.Clamp01(current + restoration);
            if (!(repaired > current) || !ui.Inventory.TryConsume(material, cost)) return false;

            ui.Integrity = repaired;
            CurrentHealth = repaired * maximumHealth;
            if (repaired >= OpeningDriveFaultRepairThreshold)
            {
                if (mover != null) mover.SetOpeningDriveFault(false);
                ui.HideDriveDamageWarning();
            }
            return true;
        }

        public void Damage(float damage)
        {
            if (damage <= 0f || (mover != null && mover.IgnoreAnimalDetection)) return;
            if (ui != null) CurrentHealth = ui.Integrity * maximumHealth;
            CurrentHealth = Mathf.Max(0f, CurrentHealth - damage);
            if (ui != null) ui.Integrity = CurrentHealth / maximumHealth;
            Damaged?.Invoke(damage);
        }
        public void Knockback(Vector2 worldDirection, float worldDistance)
        {
            if (mover != null && mover.IgnoreAnimalDetection) return;
            velocity += worldDirection.normalized * worldDistance * 5f;
            Impacted?.Invoke(worldDirection);
        }
        private void LateUpdate()
        {
            if (mover != null && mover.IgnoreAnimalDetection) { velocity = Vector2.zero; return; }
            if (mover == null || Time.deltaTime <= 0f || velocity.sqrMagnitude <= .00001f) return;
            float damping = Mathf.Exp(-5f * Time.deltaTime);
            Vector2 displacement = velocity * ((1f - damping) / 5f);
            velocity *= damping;
            int steps = Mathf.Max(1, Mathf.CeilToInt(displacement.magnitude / .1f));
            for (int i = 0; i < steps; i++)
                if (!mover.ApplyAnimalImpact(displacement / steps)) { velocity = Vector2.zero; break; }
        }
    }
}
