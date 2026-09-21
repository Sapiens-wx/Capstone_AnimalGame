# Terrain Test 02

打开 `Assets/Scenes/TerrainHeightMapTestScene.unity` 并进入 Play 即可测试。
场景使用 `Terrain_Test_02_MapData.asset`，沿用原机器人的移动、扫描、相机和通行判断。

- 源文件：`Terrain_Test_02.raw`，2049 × 2049，16 位，小端，Unity 导出时未翻转。
- 灰度图：`Terrain_Test_02_height_R16.png`，保留所有原始采样值，没有拉伸灰度。
- Unity 导入：R16、线性、Read/Write 开启、无压缩、无 Mipmap、NPOT 不缩放，Max Size 4096。
- 对照 Terrain：`Assets/New Terrain 1.asset`，X/Z 为 200 × 200 米，Y 范围 0～70 米。
- 运行高度数据：2048 × 2048（现有 Map Data 支持的上限），双线性重采样，无额外平滑或归一化。
- Scene 编辑预览：1024 高度采样；实际测试请进入 Play 使用完整烘焙数据。
- 显示图：2048 × 2048，Pixels Per Unit 10.24，对应 200 × 200 世界单位。
- 出生点：地图坐标 (60, 60) 米，对应源 Terrain 的本地 X/Z；地面高约 5.6354 米。
- 保留原测试 Map Data 的黑色配色和 1 米等高线间隔。

W/S 前后移动，A/D 转向，快速双击 E 进行地形扫描；右上角显示坐标、高度和坡度。

## 更新高度图

在项目根目录执行（Python 需要 Pillow）：

```powershell
python Tools/convert_unity_heightmap.py Assets/Maps/Terrain_Test_02.raw Assets/Maps/Terrain_Test_02_height_R16.png --width 2049 --height 2049 --overwrite
```

Unity 导入完成后，选择 `Terrain_Test_02_MapData.asset`，执行
`Animal Game > Pre-Bake Height Map`，然后重新进入 Play。
如果改变 Terrain 的宽、长或高度范围，应同步更新这份 Map Data。

旧的 `TerrainPrototypeHeightMapLevel.asset` 及其烘焙资源保留，可通过场景中
Map Test Scene Controller 的 Level Asset 切回旧地图，并同步恢复出生点和预览配置。
`Terrain Height Map Test > Validate Active Scene` 是旧 64 米样例的固定参考点检查，
不适用于这张新地形；它报告旧样例不匹配并不表示新地图损坏。

## 本次导入校验

4198401 个 PNG 像素与 RAW 逐个一致，且方向与源 Terrain 一致。
4194304 个烘焙采样与相同位置的原 Terrain 双线性采样逐个对照，最大高度误差
约 0.001469 米。此误差检查针对采样点，不意味着重采样后的所有细小地形特征完全不变。
编辑及运行校验报告位于 `Temp/TerrainTest02/`。
