using System;
using UnityEngine;

namespace AnimalGame.RobotMap
{
    [DisallowMultipleComponent]
    public sealed class PlayerCombatReceiver : MonoBehaviour
    {
        [SerializeField, Min(1f)] private float maximumHealth = 100f;
        public float CurrentHealth { get; private set; }
        public event Action<float> Damaged;
        private RobotMover mover;
        private AnimalGame.MainUI.MainUI ui;
        private Vector2 velocity;
        public bool IsDisplaced => velocity.sqrMagnitude > .001f;
        private void Awake()
        {
            mover = GetComponent<RobotMover>();
            CurrentHealth = maximumHealth;
            ui = FindFirstObjectByType<AnimalGame.MainUI.MainUI>();
            if (ui != null) CurrentHealth = ui.Integrity * maximumHealth;
        }
        public void Damage(float damage)
        {
            if (damage <= 0f) return;
            if (ui != null) CurrentHealth = ui.Integrity * maximumHealth;
            CurrentHealth = Mathf.Max(0f, CurrentHealth - damage);
            if (ui != null) ui.Integrity = CurrentHealth / maximumHealth;
            Damaged?.Invoke(damage);
        }
        public void Knockback(Vector2 worldDirection, float worldDistance)
        { velocity += worldDirection.normalized * worldDistance * 5f; }
        private void LateUpdate()
        {
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
