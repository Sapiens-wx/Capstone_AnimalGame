# Animal photos

## Setup

1. Create one **Animal Game > Photos > Animal Photo Library** asset per species using the Create menu. Set its species.
2. Select that asset and open **Animal Game > Animals > Animal Photo Library**. The left panel lists state groups and image entries (Texture2D assets); select **Edit** beside an image to edit it in the right panel. Each panel scrolls independently.
3. Drag over the image to define the required subject rectangle, or enter its normalized Rect. The origin is the bottom left. Green is the required subject area. Set saturation (0 = grayscale, 1 = original, 2 = enhanced). **Preview Another random crop** draws an orange square on the source preview, using the runtime crop algorithm. A crop is generated only on button clicks; changing the subject rectangle or saturation does not regenerate it. There is no separate processed-crop preview. Changes support Undo/Redo; **Save library** writes the asset.
4. Create a **Photo Library** asset and add the per-species assets to its Animals list.
5. Assign this asset to **PhotoResultUI > Photo Library**. For scenes that create this component dynamically, assign **HeightMapPlayerSceneBootstrap > Photo Library** instead; it passes the reference to the instantiated UI.
6. On each **AnimalPhotoSubject**, configure **Species** and **Photo State**. Photo State is currently manual; it does not follow AnimalAgent.CurrentState. The enums live in separate files under Scripts/Animals. Existing enum values keep their numeric IDs.

Texture Read/Write is not required. Use individual source textures; do not supply a sprite atlas containing multiple photographs. Subject rectangles default to the whole image. For non-square sources, narrow the subject rectangle until an in-image square can contain it; the editor warns otherwise and runtime selection skips those entries. Empty/missing matching libraries log a warning and skip the result. Legacy subject-local photos remain serialized for manual migration but are no longer selected by PhotoResultUI.

## Capture and rendering

PhotoModeController.PhotoCaptured invokes PhotoResultUI.HandlePhotoCaptured. Main-subject detection is unchanged. The library samples uniformly from valid, square-croppable images matching the captured species and state. It samples a square side length between the subject's longest pixel dimension and the source's shortest pixel dimension, then samples a position that contains the subject. Every crop stays within the image. Square means equal width and height in source pixels, not normalized UV units.

PhotoResultSnapshot freezes species, state, time, animal position, photographer elevation and an AnimalPhoto. Animal position uses logical map meters when available, otherwise world XY; HasMapPosition distinguishes them. Elevation is sampled at the robot's shutter location, not at the animal or the visual camera. HasHeight is false if that sample is unavailable, and the UI displays a dash rather than a fabricated altitude. Coordinates are the raw Vector2 for now, not geographic degrees.

AnimalPhoto holds the source reference and immutable crop/saturation values. Render() returns an owned RenderTexture processed with the shader in Shaders/Resources/AnimalPhoto. The result view generates this once when opened and releases it when closed. Perspective redraws reuse the processed texture. The card fits the complete image with padding to preserve both aspect ratio and subject visibility.

The existing in-memory PhotoAlbumService retains snapshots and their processing choices, not GPU textures. Re-rendering a saved AnimalPhoto uses the same crop/saturation. Disk album persistence/export is not implemented here.
