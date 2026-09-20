# Main Map Replica Terrain

`Assets/Scenes/3DMapTestScene.unity` 中的 `Main Map Replica Terrain` 是可继续雕刻的
Unity Terrain，使用本目录独立的 `MainMapReplicaTerrainData.asset`。
场景原来的 `Terrain` 已停用保留，原 TerrainData 没有修改。
切回旧地形时，先停用新 Terrain，再启用旧 Terrain，避免两块地形重叠。

## 生成参数

- 源图：`Assets/Maps/grey_height_map.png`。
- 配置来源：`Assets/Maps/MainHeightMapLevel.asset`。
- 尺寸：250 × 250 米；高度映射范围 0～70 米；位置 (0, 0, 0)。
- 高度分辨率：2049 × 2049，水平采样间距约 0.12207 米。
- 沿用原系统的灰度归一化与 Sigma = 0.75 米的表面平滑。
- 从原系统 2048 × 2048 的表面高度场双线性采样，生成 Terrain 高度数据。
- TerrainCollider 使用同一 TerrainData；相机、灯光、材质和 Scene 视角没有另行配置。

生成只执行一次，不会在打开场景或进入 Play 时覆盖手工雕刻。
`Animal Game > Main Map Replica > Create Terrain in 3DMapTestScene`
在该资源已存在时会拒绝重复创建。

## 校验

首次生成时，4198401 个高度采样与源表面的最大误差为 0.001072 米，
均方根误差为 0.000606 米；同时验证 X/Z 方向及碰撞器引用。
平滑后的实际高度约为 0.1730～69.9210 米，保留该范围，没有再次拉伸到 0～70 米。
最高点约在 (X=98.877, Z=187.744)，高度 69.9210 米。

4096 处一米跨度的坡度检查：源地图平均 25.1124°，Terrain 平均 25.1115°，
最大差异 0.1577°。新计算的源高度与主地图现有预烘焙数据最大差异约 0.000004 米。
完整报告：`Temp/MainMapReplica/Validation.txt`。

`Animal Game > Main Map Replica > Validate Terrain Against Main Map` 可重新对照当前源地图。
后续手动雕刻 Terrain 或修改 MainHeightMapLevel 后，报告差异是预期行为。

## 导出后回到游戏测试

使用 Terrain 的 Export Raw 导出 16 位高度图。默认小端且未勾选 Flip Vertically 时：

```powershell
python Tools/convert_unity_heightmap.py input.raw output_height_R16.png --width 2049 --height 2049
```

新测试 Map Data 设置为 250 × 250 米、0～70 米，关闭 Normalize Source Range，
Surface/Detail Smoothing 均设为 0，再执行 Pre-Bake Height Map。
归一化与平滑已包含在 Terrain 中，无需重复处理。
