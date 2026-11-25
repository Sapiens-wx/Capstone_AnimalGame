using AnimalGame.Animals.UI;
using UnityEngine;
using UnityEngine.UIElements;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace AnimalGame.MainUI
{
    public sealed partial class MainUI
    {
        private DpadHoldHintElement dpadHoldHint;
        private float dpadHintVisibleUntil;
        private float dpadHintOpacity;
        private Vector2 dpadHintDirection;

        private void RefreshDpadHoldHint()
        {
            if (!started || dpadHoldHint == null) return;
            bool blocked = AnimalGame.MapTest.OfficialMapOpening.IsPlaying || WorldPanelOpen
                || AnimalGame.AnimalDebug.DebugUI.BlocksOtherPanels;
            Vector2 direction = Vector2.zero;
#if ENABLE_INPUT_SYSTEM
            foreach (Gamepad pad in Gamepad.all)
            {
                if (!pad.added) continue;
                Vector2 input = pad.dpad.ReadValue();
                if (input.sqrMagnitude > direction.sqrMagnitude) direction = input;
            }
#endif
            const float hintDelay = .2f;
            bool showUp = !blocked && !inventoryOpen && direction.y > .1f
                && AnimalArchiveUI.OpenHoldElapsed >= hintDelay;
            bool showRight = !blocked && !IsCargoLocked && !inventoryOpen && direction.x > .1f
                && InventoryController.OpenHoldElapsed >= hintDelay;
            bool showLeft = !blocked && !IsCargoLocked && inventoryOpen && direction.x < -.1f
                && InventoryController.CloseHoldElapsed >= hintDelay;
            if (showUp || showRight || showLeft)
            {
                dpadHintVisibleUntil = Time.unscaledTime + 1.5f;
                dpadHintDirection = direction;
            }
            if (blocked) dpadHintVisibleUntil = 0;
            bool visible = Time.unscaledTime < dpadHintVisibleUntil;
            dpadHintOpacity = Mathf.MoveTowards(dpadHintOpacity, visible ? 1f : 0f,
                Time.unscaledDeltaTime / .12f);
            dpadHoldHint.style.opacity = dpadHintOpacity;
            dpadHoldHint.style.display = dpadHintOpacity > 0f ? DisplayStyle.Flex : DisplayStyle.None;
            float up = showUp ? AnimalArchiveUI.OpenHoldProgress : 0;
            float right = showRight ? InventoryController.OpenHoldProgress : 0;
            float left = showLeft ? InventoryController.CloseHoldProgress : 0;
            string caption = showUp ? "Photo/Catalog" : showRight ? "Open Cargo"
                : !showLeft ? null : InteractionState == InventoryInteractionState.Info ? "Back" : "Close Cargo";
            dpadHoldHint.SetState(dpadHintDirection, Mathf.Max(up, Mathf.Max(right, left)), caption);
        }
    }
}
