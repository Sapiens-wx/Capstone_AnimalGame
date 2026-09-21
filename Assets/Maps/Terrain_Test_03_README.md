# Terrain Test 03

打开 `Assets/Scenes/TerrainHeightMapTestScene.unity`，进入 Play 即可测试。
场景现在使用独立的 `Terrain_Test_03_MapData.asset` 及其 HeightPrebake。

- 源文件：`Terrain_Test_03.raw`，2049 × 2049，16 位、小端、Unity 导出时未翻转。
- 灰度图：`Terrain_Test_03_height_R16.png`，16 位单通道 PNG；逐像素保留 RAW 数值，不拉伸灰度、不缩放。
- Unity 导入：R16、线性、Read/Write 开启、无压缩、无 Mipmap、NPOT 不缩放、Max Size 4096。
- 原 Terrain：`MainMapReplica/MainMapReplicaTerrainData.asset`，X/Z 为 250 × 250 米，Y 高度范围 0～70 米。
- 实际地面高度约 0.173～69.921 米。Map Data 使用 0～70 米映射，关闭 Normalize Source Range，避免再次拉伸。
- 原 Terrain 已包含平滑处理，Map Data 的两项 Smoothing Sigma 均为 0，避免再次平滑。
- 物理烘焙：2048 × 2048，沿用当前 Map Data 支持的上限，从 2049 源图双线性重采样。
- 编辑预览：1024 高度采样；Play 使用完整的 2048 烘焙数据。
- 显示图：2048 × 2048，Pixels Per Unit 8.192，对应 250 × 250 世界单位。
- 沿用测试场景现有黑色地图样式及 7 米等高线间隔。
- 保留现有出生点地图坐标 (104.6, 21.4) 米，对应 Terrain 的本地 X/Z；地面高约 22.13 米，源 Terrain 坡度约 15.08°。

W/S 前后移动，A/D 转向，快速双击 E 进行地形扫描。
旧 Test 02 和原始 Terrain 数据仍保留。

## 更新同一高度图

重新导出相同尺寸和设置的 RAW 后，在项目根目录运行（Python 需要 Pillow）：

```powershell
python Tools/convert_unity_heightmap.py Assets/Maps/Terrain_Test_03.raw Assets/Maps/Terrain_Test_03_height_R16.png --width 2049 --height 2049 --overwrite
```

等 Unity 导入完成，选择 `Terrain_Test_03_MapData.asset`，执行
`Animal Game > Pre-Bake Height Map`，再进入 Play。
若改变 Terrain 宽、长或高度范围，需要同步更新 Map Data。

`Terrain Height Map Test > Validate Active Scene` 是旧 64 米样例的固定参考点检查，
不适用于当前地图。本次导入和运行验证报告放在 `Temp/TerrainTest03/`。

## 导入验证

4,198,401 个导入后的 R16 像素与 RAW 逐个一致，方向与源 Terrain 相同。
4,194,304 个烘焙采样与原 Terrain 相同位置的双线性采样逐个对照，最大高度误差
约 0.001473 米；重采样仍可能改变小于采样间距的细节。
编辑及 Play 检查通过，运行时确认直接加载 HeightPrebake，机器人、通行判断、
相机跟随和 2D Renderer 均正常连接。地形扫描生成 59 个可见标记。
