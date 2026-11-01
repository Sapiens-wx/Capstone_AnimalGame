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

PhotoResultSnapshot freezes species, state, the MainUI game clock, photographer position/elevation/geographic coordinates, and an AnimalPhoto. Photographer position uses logical map meters when available, otherwise world XY; HasMapPosition distinguishes them. Elevation and latitude/longitude are sampled together at the robot's shutter location through MapTestSceneController.TrySampleGeographicWorldPosition, using the entire TiledLevelAsset's geographic ranges. HasHeight and HasGeographicCoordinates are false if that sample is unavailable, and the UI displays placeholders. The coordinates row retains the photographer's captured Vector2; the longitude and latitude rows display the photographer's captured degrees and hemispheres. Moving the player or changing the map afterward does not change a saved snapshot.

AnimalPhoto holds the source reference and immutable crop/saturation values. Render() returns an owned RenderTexture processed with the shader in Shaders/Resources/AnimalPhoto. The result view generates this once when opened and releases it when closed. Perspective redraws reuse the processed texture. The card fits the complete image with padding to preserve both aspect ratio and subject visibility.

PhotoAlbumService now writes serializable AnimalPhotoRecord entries through SaveSystem. The single default ScriptableObject is Data/Saves/GameSave.asset; a runtime clone loads animal-save.json from Application.persistentDataPath before scenes load. SaveSystem.Save() persists the current state. Saving a photo stores it locally; it does not send it or grant recognition. Sending is outside this implementation.

## Photos and Catalog

The player bootstrap creates Prefabs/Resources/UI/AnimalCatalog/AnimalArchiveUI.prefab. Hold D-pad Up (or G on keyboard) for 0.5 seconds to open Photos. P remains the existing camera toggle. Arrow keys navigate, Enter confirms, Y opens the selected species, Q/E select Photos/Catalog, brackets switch collection/region/species, X opens a species photo collection, and B/Escape returns. Controller inputs follow the GDD; the right stick tilts inspected photographs using the result screen composite shader.

AnimalInfoLibrary.asset in Data/AnimalInfo holds per-species Catalog information and optional illustration/distribution textures. Bison text is seeded from the supplied reference; remaining species have identity/region entries and editable empty information fields. Unauthored unlocked fields display Not documented. Add the intended diagram and map assets to those fields to complete the Catalog artwork.

AnimalArchiveAssets in the UI resource folder references the default save, photo library, info library, all species configs and stable GUID-to-source-image mappings for player builds. Dictionaries are built at startup. After changing source photo assets, use Animal Game > Photos > Update Archive Asset References; builds also refresh these references automatically. Keep source texture GUIDs stable so existing saves resolve correctly. New species also need an info entry (Create Archive Assets adds missing entries without replacing authored ones).

Animal Game > Validation > Run Animal Archive Checks verifies disk round trips, effect reconstruction, tier boundaries, selection/scrolling and UXML bindings. Saving/loading errors surface rather than silently resetting player progress.
