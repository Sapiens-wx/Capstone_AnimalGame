# MainHeightMapLevel

`MainHeightMapLevel_TiledLevel.asset` is the controller input. It contains one tile referencing the original `MainHeightMapLevel.asset`, which retains its GUID, physical settings and painted water/surface data.

The explicit Presentation source is that same tile. Rendering scale remains 0.27 world units per map meter (67.5 units across 250 m), independent of preview resolution. Source textures, shared patterns and generated outputs live in `data/`. References from other levels remain valid by GUID.

Select the tile to paint water/surfaces. Select either the tile or tiled definition and run **Animal Game > Pre-Bake Height Map** to rebuild heights. The existing Main prebake remains a tracked, serialized asset, preserving the pre-migration cache policy; TerrainGridV1's generated prebakes remain local and ignored.

All controller inputs now use TiledLevelAsset. A standalone map is represented by a tiled definition with one entry at (0,0).
