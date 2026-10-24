# Animal inspector

The regular `Prefabs/Resources/UI/MainUI.prefab` creates the inspector automatically in player-map scenes. Its UIDocument, UXML and USS are in `Prefabs/UI/AnimalAi`. The inspector is a separate scene root so its panel settings and layout stay independent of the HUD. For a custom scene, place `AnimalAi.prefab` as a root object in the player's scene.

- Open with **M** or **D-pad Up**. While open, D-pad Up navigates instead of closing. Navigate using D-pad, arrow keys or Tab; WASD and the left stick only control gameplay. Close with M, or B / Esc after dismissing the current list or sampling mode.
- Inventory and expanded tutorials must be closed first. The inspector blocks opening inventory, tutorials and the refit terminal while open. Gameplay and animal simulation continue; no player components or time scale are disabled.
- Select through the animal list, or click **Pick** followed by a world click. Sampling uses a circle centered on the body bounds with **radius = body width × 2**. UI clicks are excluded. Dead, hidden and removed animals are excluded.
- The selected animal keeps its arrow while the inspector is open. Hover or focus another animal row for an additional preview arrow; the selected marker stays visible. Offscreen targets use the camera viewport edge. Arrow placement updates after the camera and HUD projection. All gold accents use `#D4B854`.
- General commands are **Daily, Curious, Fleeing, Aggressive, Hunt and Eat**. Daily delegates to the animal's own scheduler. Eat chooses an eligible corpse or configured plant. Species-specific daily activities are not exposed as commands.
- Commands validate before replacing the old behavior, revalidate queued requests, use normal action cleanup and species overrides, and report rejection/failure below Execute. Hunt and Eat bypass the animal's existing cooldown, then establish normal cooldowns on exit. Normal reactions and group propagation remain active.

Validation: **Animal Game → Validation → Run Animal Inspector Checks**. The batch entry point `AnimalGame.Editor.AnimalDebugUIRegressionChecks.RunBatch` also runs existing animal AI and goat/wolf checks.

`AnimalGame.Editor.AnimalDebugUIPlayModeChecks.RunBatch` runs those checks and then tests real keyboard, gamepad and mouse input in Play Mode, including panel proportions and tutorial ownership. Run batch validation in an isolated project/session, since this entry point opens an empty test scene and exits the editor when complete.
