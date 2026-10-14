# Four-tile V1 Terrain authoring test

Open `Assets/Scenes/3DMapTestScene.unity`. The `Terrain Grid V1 - 500m` root is a 2 x 2 grid of four independent, editable TerrainData assets. The original `Main Map Replica Terrain` is preserved at the world origin; the new grid starts at world `(300, 0, 0)` so the two surfaces do not overlap. Grid-local coordinates still run from 0 to 500 meters.

| Tile | Grid-local origin (X, Z), meters | 3D world origin (X, Y, Z) |
|---|---|---|
| Tile_0_0 | (0, 0) | (300, 0, 0) |
| Tile_1_0 | (250, 0) | (550, 0, 0) |
| Tile_0_1 | (0, 250) | (300, 0, 250) |
| Tile_1_1 | (250, 250) | (550, 0, 250) |

The starting surface contains a continuous hill through the central junction, a valley through the lower pair of tiles, and a smaller shoulder. It is generated only once. Reopening the scene and exporting never regenerate or overwrite sculpted TerrainData.

## Height and size references

`Height Reference - Original V1 Terrain` measures the original Terrain; `Height Reference - Four Tile Grid` under the grid root measures all four tiles. References draw directly in the Scene view during Edit Mode, independently of the main and per-script Gizmos switches. Use the component's **Show Reference** checkbox to hide them.

Both show a fixed 70 m ruler with 1 m ticks and 10 m labels, a 1 m wireframe cube, a 2 m figure, actual lowest/highest points, and a vertical height-difference arrow. These are editor-only graphics with no renderer or collider. The height range 0..70 m is not a promise that the terrain occupies both endpoints.

Select a reference and click **Refresh Measurements / 刷新实际高差** after sculpting. Click **Show and Frame Height Reference / 显示并聚焦高度参照** to enable the reference and switch to an oblique 3D view of the 70 m ruler. **Animal Game > Terrain Grid V1 > Show Height Reference** finds and frames the grid reference. Measurements are deliberately cached instead of scanning heightmaps every frame.

## Continuous sculpting

Use **Animal Game > Terrain Grid V1 > Frame Grid** to view the new grid. Select a tile, open **Paint Terrain > Raise or Lower Terrain**, and start with a 20-40 m brush. Drag across the shared edge; Shift-drag lowers the surface. Use Unity's connected-terrain painting tools to edit both sides in the same brush operation.

Each tile has Grouping ID 100, Auto Connect enabled, and four-way neighbor links. **Reconnect Neighbors** restores these links if needed. Each TerrainCollider uses its tile's own TerrainData. Keep terrain rotations at zero and all parent/terrain scales at one.

After editing, run **Validate Grid and Seams**. It checks tile sizes, resolution, scale, neighbor links, colliders, shared edge values, and the four-way corner. A source seam mismatch blocks export; export does not silently repair or normalize it.

## Standard export and rebake

Run **Animal Game > Terrain Grid V1 > Export V1 Height Maps**. Outputs are in `Exports/`:

- Per tile: a RAW, a 16-bit grayscale PNG, independent MapData, and a local HeightPrebake cache.
- `world-manifest.json`: logical tile coordinates, actual 3D origins, V1 parameters, source hashes, and height ranges.
- `validation-report.txt`: source/export/prebake verification results.

| Setting | Value per tile |
|---|---|
| TerrainData size | 250 x 70 x 250 m |
| Source heightmap | 2049 x 2049, 16-bit grayscale |
| RAW | unsigned 16-bit little-endian; Z=0 row first; no Flip Vertically |
| PNG import | R16; linear; readable; uncompressed; no mipmaps; NPOT None; Max Size 4096 |
| Map size | 250 x 250 m |
| Height mapping | 0..70 m |
| Normalize Source Range | Off |
| Surface / Detail Smoothing Sigma | 0 / 0 m |
| Bake / Preview resolution | 2048 / 2048 |
| Pixels Per Unit | 30 |
| Contour interval | 7 m |

The PNG top row corresponds to Terrain Z=250; Unity's import restores pixel Y to Terrain Z. Every imported R16 sample is compared against the RAW, and exported and prebaked edges are checked. The 2049 source becomes 2048 through the existing bilinear bake; fine detail can change slightly in that resampling.

Export always rebakes physical heights, retaining an existing local prebake root GUID. It overwrites only this grid's generated outputs and preserves per-tile water/surface painting on later exports. It does not modify the original Terrain, the existing Test03 map, cameras, lighting, or persisted renderer settings.

## Local caches and source control

The five `*_HeightPrebake.asset` files under `Exports/` and `Combined/`, and their `.meta` files, are generated local caches excluded from Git. Keep the TerrainData, RAW, PNG/import settings, MapData, scenes, and export manifests in source control. Previously tracked prebakes outside this grid are unaffected.

These five MapData assets store no serialized prebake reference. In the Unity Editor, the level finds its matching local cache next to the MapData by filename, so rebuilding a cache cannot introduce a missing or machine-specific GUID into the committed MapData. Exporting and using **Animal Game > Pre-Bake Height Map** leave that serialized reference empty.

After cloning, select `Combined/Combined_500m_MapData.asset` (or the scene's Map Test Controller) and run **Animal Game > Pre-Bake Height Map** to create the combined local cache. Select each `Exports/Tile_*_MapData.asset` and use the same command if you need its local tile cache. This only rebakes the committed PNG and MapData settings; it does not re-export or change the sculpted Terrain. The existing export commands also rebuild their corresponding caches.

You can press **Play** without a cache: the controller computes the full height field and preview from the PNG on startup. That first startup takes longer, and without a local cache each later Play session computes them again. A matching local cache is used automatically in Editor Play. Player builds do not discover Editor-only local caches and use the same PNG-based runtime bake fallback; the map dimensions, precision, colors, and gameplay measurements stay the same.

## Gameplay testing

The four MapData assets can each be assigned to an existing single-map test controller. **Select Map Data** in the grid menu selects each asset. These separate assets test one tile at a time. For continuous gameplay across the full 500 m grid, use the combined map and scene described below. A world that loads separate tiles at runtime still requires the shared world sampling/loading system discussed in the design.

## Continuous 500 m test scene

Run **Animal Game > Terrain Grid V1 > Export and Deploy Continuous 500m Map** to export the current four Terrain tiles and configure `Assets/Scenes/ContinousMapTestScene.unity`. Save any modified unrelated scene first. This command uses the current sculpted terrain; it never regenerates the initial surface.

`Combined/Combined_500m_MapData.asset` uses one continuous 500 x 500 m height field. The 4097 x 4097 16-bit source stores shared tile edges once; bake and runtime preview are 4096 x 4096. Height mapping remains 0..70 m, PPU remains 30, contours remain 7 m, and normalization and both smoothing channels remain disabled. Water/mask authoring resolutions are 1024, surface visual resolution is 4096, and all meter-based pattern/transition sizes retain their V1 values.

The runtime map covers 136.5333 Unity world units per side, preserving the original 0.2730667 rendered units per logical map meter. Player size, movement, traversal thresholds, camera base size 11, and scan settings keep the existing Resources prefab values. The player starts at logical map `(250, 100)`, near an original tile seam. The default scene camera is retained but inactive.

Open this scene by itself and press **Play** to test continuous movement and scans across all four original tile areas. Use **Validate Continuous Map Test Scene** for an Edit/Play report. The editor uses a lighter 1024 physical / 2048 visual preview; Play uses the full 4096 field, loading a matching local prebake when available or baking from the PNG otherwise. The joined field makes this prototype continuous without runtime tile loading. Much larger worlds still need the separate streaming architecture. The full prebake and runtime field use more memory than a single 250 m tile.
