# Debug UI

The regular `Prefabs/Resources/UI/MainUI.prefab` creates the inspector automatically in player-map scenes. Its UIDocument, UXML and USS are in `Prefabs/UI/DebugUI`. The inspector is a separate scene root so its panel settings and layout stay independent of the HUD. For a custom scene, place `DebugUI.prefab` as a root object in the player's scene.

- Open/close with **M** or the **Close** button. Use arrow keys or Tab to navigate controls and lists, Enter to select, and Esc to dismiss a popup or sampling mode before closing. Click the top tabs to switch pages. Debug UI accepts only keyboard and mouse input.
- Inventory and expanded tutorials must be closed first. The inspector blocks opening inventory, tutorials and the refit terminal while open. Gameplay and animal simulation continue; no player components or time scale are disabled.
- Select through the animal list, or click **Pick** followed by a world click. Sampling uses a circle centered on the body bounds with **radius = body width × 2**. UI clicks are excluded. Dead, hidden and removed animals are excluded.
- The selected animal keeps its arrow while the inspector is open. Hover or focus another animal row for an additional preview arrow; the selected marker stays visible. Offscreen targets use the camera viewport edge. Arrow placement updates after the camera and HUD projection. All gold accents use `#D4B854`.
- General commands are **Daily, Curious, Fleeing, Aggressive, Hunt and Eat**. Daily delegates to the animal's own scheduler. Eat chooses an eligible corpse or configured plant. Species-specific daily activities are not exposed as commands.
- Commands validate before replacing the old behavior, revalidate queued requests, use normal action cleanup and species overrides, and report rejection/failure below Execute. Hunt and Eat bypass the animal's existing cooldown, then establish normal cooldowns on exit. Normal reactions and group propagation remain active.

Validation: **Animal Game → Validation → Run Animal Inspector Checks**. The batch entry point `AnimalGame.Editor.AnimalDebugUIRegressionChecks.RunBatch` also runs existing animal AI and goat/wolf checks.

`AnimalGame.Editor.AnimalDebugUIPlayModeChecks.RunBatch` runs those checks and then tests real keyboard and mouse input, verifies gamepad input is ignored, and checks mouse tab switching in Play Mode, including panel proportions and tutorial ownership. Run batch validation in an isolated project/session, since this entry point opens an empty test scene and exits the editor when complete.

`DebugUI.cs` owns window visibility, cursor restoration, modal ownership, tab buttons and input. `Animal/AnimalDebugUI.cs` implements the Animal Inspector tab. To add a page, derive a component from `DebugUITab`, implement its title, content binding, show/hide, back and input methods, and add it beside `DebugUI` on the prefab. Components are listed in Inspector order; the first tab is selected initially. Hide must dismiss temporary interactions and overlays.

`Player/PlayerDebugUI.cs` adds the Player tab: ignore collision and terrain, disable animal detection, teleport to signed (longitude, latitude) coordinates with a live yellow cross, and override movement speed. Overrides stay active when closing or switching tabs; disabling the speed override uses the configured default again.
