# Terrain Grid V1: tiled level

Edit the four Terrain objects in `Assets/Scenes/3DMapTestScene.unity`. Each tile is 250 x 250 m, with a 2049 x 2049 source and a 0..70 m elevation range. The grid starts at world (300, 0, 0) in the 3D authoring scene; the gameplay map uses its own local meter coordinates starting at (0, 0). Initial terrain generation happens only during creation; export reads the current sculpted TerrainData.

## Assets

```text
Assets/Maps/TerrainGridV1/
  TerrainGridV1_TiledLevel.asset
  Tile_0_0_MapData.asset
  Tile_1_0_MapData.asset
  Tile_0_1_MapData.asset
  Tile_1_1_MapData.asset
  data/
    Tile_*_TerrainData.asset
    Tile_*.raw
    Tile_*_height_R16.png
    Tile_*_MapData_HeightPrebake.asset   (local, ignored by Git)
    ...water and surface outputs...
    world-manifest.json
    validation-report.txt
```

The old Exports and Combined folders have been retired. No combined height image is generated. The four MapData assets retain their original GUIDs and authored water/surface data. All generated data belongs in `data/`; `.meta` files stay beside their corresponding assets.

`TiledLevelAsset` stores explicit grid coordinates and MapData references. It derives tile count, grid dimensions and world bounds from the list. Complete rectangles starting at (0,0) are supported, including 2 x 3; holes, negative coordinates and rotated tiles are unsupported. Entries may be in any order. The current TerrainGridV1 authoring/export command still exports its specific 2 x 2 grid; other rectangular layouts can be assembled in the TiledLevelAsset inspector from valid MapData assets.

The explicit **Presentation** reference selects shared contour/color/elevation-filter and traversal settings. Each tile keeps its own height source, physical bake settings, water depth, water appearance, surface masks and surface visuals. To edit shared presentation for this grid, edit the asset assigned to Presentation (initially Tile_0_0); assigning a different Presentation is an intentional choice, independent of list order. It does not load a second full-map image.

**World Units Per Meter** controls rendering scale independently of pixel resolution. V1 uses 2048 / 30 / 250 = 0.27306667 on both axes, giving a 136.5333-unit-wide 500 m world. Keep the controller unrotated with positive scale; existing scenes use unit scale.

## Export and play

1. Sculpt the Terrain tiles in `3DMapTestScene`. Use connected-terrain painting and Reconnect Neighbors as needed. Height references and their refresh buttons remain available.
2. Run **Animal Game > Terrain Grid V1 > Export V1 Height Maps**. This updates four RAW/PNG files, four MapData assets, four local prebakes and the tiled definition. Export preserves existing MapData/cache GUIDs and water/surface painting. It updates source-derived size, elevation and bake settings; existing presentation/mask defaults are retained.
3. To export and switch to the gameplay scene in one operation, run **Export and Deploy Continuous 500m Map**. This now deploys the tiled definition to `Assets/Scenes/ContinousMapTestScene.unity`.
4. Open that scene by itself and press Play. One controller manages four height fields/renderers and one player/camera/scan setup. The original spawn is at map (250,100).

Source PNG is linear, readable, uncompressed R16 with no mipmaps. RAW remains unsigned 16-bit little-endian with Z=0 first. Each tile bakes/previews at 2048 x 2048 in Play. Editor preview limits apply per tile. The V1 source is not normalized or additionally smoothed. The export verifies the imported source samples, but does not scan/repair shared edges; authored edges are assumed continuous. The separate legacy **Validate Grid and Seams** diagnostic is still available if explicitly requested from the menu.

The new runtime never builds a whole-world height array or high-resolution texture. Height, water and playable-area queries route through the tile containing each sample. Interior boundaries belong to the tile in the positive direction; the world maximum stays on the last tile. Paths and scans crossing a seam query both tiles. Photo contour capture includes every visible tile and keeps its existing bounded output resolution.

All tiles are resident in this version. Total memory still grows with tile count. The coordinate query interface is separate from residency, so future streaming can add load/unload policies without changing gameplay query coordinates. Streaming, asynchronous loading and LOD are not implemented.

## Caches and painting

Select a tile MapData and run **Animal Game > Pre-Bake Height Map** to rebuild that tile. Select the TiledLevelAsset or its controller to rebuild every referenced tile sequentially. Cache paths resolve under the selected level's `data/`, never by a project-wide short-name search.

V1 caches and their .meta files remain generated local files ignored by Git, with no serialized cache reference in MapData. After a fresh checkout, use Pre-Bake or export to regenerate them. Editor Play uses matching local caches automatically. Without a cache, each tile bakes from its PNG. Player builds use the PNG fallback for these Editor-only local caches. Source PNGs, MapData, TerrainData and manifests remain version-controlled.

Select an individual tile MapData to open the existing water/surface painter. The scene painter converts between global map coordinates and that selected tile's local coordinates; brush positions outside that tile are rejected. One stroke does not paint neighboring tiles. Generated water/surface images go under `data/`. Shared textures may live in another level's data directory and retain GUID-based references.

## Validation

Use **Animal Game > Tiled Levels > Run Regression Checks** for synthetic 2 x 3 layout, query, scan, water, cache and renderer checks. Use **Animal Game > Terrain Grid V1 > Validate Continuous Map Test Scene** on the actual test scene. The migration utility under **Animal Game > Tiled Levels** is for converting the old Main/V1 directory layout and can be rerun; save modified scenes first.

Only MainHeightMapLevel and TerrainGridV1 were migrated. Controller input is exclusively TiledLevelAsset; unrelated legacy test scenes are not migrated and require a single-tile wrapper before reuse.

### Implementation verification

- Editor and non-Editor C# configurations compile without errors (existing project warnings remain); this is not a packaged Player build.
- Tiled regression checks and the existing terrain scan, climbable and bush checks passed in Unity.
- The real Export and Deploy command completed with four independent 2048 prebakes. All four original TerrainData file hashes are unchanged after relocation; 31 moved resource GUIDs were verified.
- Main's single-tile runtime loads and samples successfully, retaining its 67.5-unit width.
- V1 Play smoke passed: four local caches, one player/camera, cross-tile traversal and scan at map Y=300, and multi-tile photo capture into a bounded 256px target. This check is available as **Animal Game > Tiled Levels > Run V1 Play Smoke Check** after exporting/prebaking. Current sculpted terrain near the Y=100 spawn is steep and can correctly be rejected by traversal slope rules.
- The broader HeldCollisionRegressionChecks suite stopped at its existing arm-only resistance assertion (0.2792138 versus 0.2695252), before the map fixture check. That assertion does not use the map. It is not reported as passing.

Compilation also required replacing stale references to the previously removed AnimalSimulation pause API with the project's Time.timeScale pause behavior, and removing the obsolete animal-specific pause regression.
