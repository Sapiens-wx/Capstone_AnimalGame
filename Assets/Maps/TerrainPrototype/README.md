# Terrain prototype

打开 `Assets/Scenes/3DMapTestScene.unity`，展开 `Terrain Prototype - 64m`，选择
`Test Terrain (select to sculpt)` 即可使用 Inspector 中的 Terrain 笔刷继续雕刻。
Scene 视图开启 Gizmos 时显示测试区域标签；Game 视图显示斜向俯视画面。

- 尺寸：X/Z 为 64 × 64 米；Y 高度范围 20 米。
- Terrain 原点为 `(0, 0, 0)`，基础地面高 4 米，谷底高 2 米。
- 高度采样：513 × 513，相邻采样间距 0.125 米。
- `TestTerrainData.asset` 是实际保存的地形数据，TerrainCollider 使用同一资源。
- 草地、土路和岩石是三个可继续绘制的 TerrainLayer。
- 地形只在创建时生成。重新打开场景或进入 Play 都不会覆盖后续的手工雕刻。

## 初始地形参考点

下表使用 Terrain 本地 X/Z 坐标，高度单位为米；坡度在坡面中段测量。

| 区域 | X | Z | 初始高度 / 坡度 |
|---|---:|---:|---|
| 入口平地 | 8 | 9 | 4 m |
| 缓坡中点 | 20 | 约 21.986 | 10°，约 7 m |
| 中坡中点 | 30 | 约 32.566 | 25°，约 7 m |
| 陡坡中点 | 40 | 36 | 45°，约 7 m |
| 浅谷中心 | 52 | 23 | 2 m |
| 低陡坎 | 7 | 25.75 → 26.25 | 4 → 4.4 m |
| 高陡坎 | 13 | 25.75 → 26.25 | 4 → 5 m |
| 东北角方向小丘 | 58 | 58 | 5.4 m |

两处陡坎使用约一个采样间距的短坡面表达，不包含悬挑或多层结构。
三条坡道宽约 6 米，侧边平滑衔接；中段坡度保持固定，顶部连接到小山。

## 检查和预览

`Animal Game > Terrain Prototype > Validate and Capture` 会核对上表的初始数据，
检查碰撞器引用，并在 `Temp/TerrainPrototype/` 写入 `Validation.txt` 和 `Overview.png`。
这是初始地形的基准检查；手工改变这些参考点之后，报告差异是预期行为。

项目默认使用 2D Renderer。测试相机显式使用新增的 `TerrainPrototypeRenderer`。
URP 的 Scene 视图始终使用默认渲染器，因此编辑器脚本在编辑本场景时临时使用
一份默认渲染器为 3D 的管线副本；保存、切换场景、进入 Play 或重新编译之前恢复。
这份副本不保存成资源，项目持久化的默认渲染器仍然是原有 2D Renderer。
如果 Unity 刷新品质设置导致临时管线引用丢失，预览脚本会自动重新建立副本。
预览按 `3DMapTestScene` 的场景路径启用，不依赖原测试物体的名字；在该场景中
新建或替换 Terrain 也会使用 3D 预览，无需保留原来的测试物体。

## 下一步接入 Heightmap

此阶段完成的是 Terrain 制作。导出时使用 Terrain 原始高度数据；X 对应地图横轴，
Z 对应地图纵轴。源高度值 0～1 对应 0～20 米，关卡尺寸设为 64 × 64 米，
关闭 `Normalize Source Range`，首次对照时关闭额外平滑。
东北角的小丘用于检测方向错误。Terrain 材质图层与水域需要独立转换。

## RAW 转换为 16 位 PNG

在项目根目录运行（Python 需要安装 Pillow）：

```powershell
python Tools/convert_unity_heightmap.py Assets/Maps/TerrainPrototype/terrain.raw Assets/Maps/TerrainPrototype/terrain_height_R16.png
```

脚本默认输入为 513 × 513、16 位、Windows 小端、Unity 导出时未勾选
`Flip Vertically` 的 RAW。尺寸可通过 `--width` / `--height` 指定；Mac 字节序
使用 `--byte-order big`；如果导出时已勾选垂直翻转，添加 `--raw-top-down`。
重复转换到同一文件时添加 `--overwrite`，原 PNG 的 `.meta` 应保留。

转换只调整 PNG 所需的字节序和行存储顺序，不拉伸灰度、不缩放图片。
脚本会重新读取 PNG，检查文件为 16 位灰度，并逐个核对高度采样值。
`terrain_height_R16.png.meta` 已配置为线性、可读、无压缩、无 Mipmap 的 R16
纹理，并关闭非 2 次幂缩放；其它新文件名需要设置同样的导入参数。

## 在原游戏系统中测试

单独打开 `Assets/Scenes/TerrainHeightMapTestScene.unity`，进入 Play。
它使用原来的 2D Renderer、机器人、跟随相机、扫描 UI 和通行判断。
编辑模式下在 Scene 视图查看地图；Game 视图的相机由原有启动脚本在 Play 时创建。
关卡数据为本文件夹中的 `TerrainPrototypeHeightMapLevel.asset`；出生点是
地图坐标 `(8, 9)`，对应 Terrain 的 `(X=8, Z=9)`，高度约 4 米。

- W/S 前进、后退，A/D 转向，快速双击 E 触发地形扫描。
  长按 E 是原有的生物扫描操作。
- 地图尺寸 64 × 64 米，高度范围 0～20 米，高度采样 513 × 513。
- Normalize Source Range 关闭，Surface/Detail Smoothing 都为 0。
- 等高线间隔 1 米；无水域、边界遮罩或表面绘制数据。
- 1024 像素显示图与 16 Pixels Per Unit 让 1 个 Unity 世界单位对应 1 地图米。
- 更新 PNG 后，选中这份新关卡数据，执行 `Animal Game > Pre-Bake Height Map`。
  只重建它自己的 `TerrainPrototypeHeightMapLevel_HeightPrebake.asset`。

`Animal Game > Terrain Height Map Test > Validate Active Scene` 可在编辑或 Play
模式检查 R16、2D Renderer、预烘焙引用、全图与 TerrainData 的高度误差及初始参考点。
Play 模式还检查原有玩家/扫描/相机是否初始化，并记录坡道与陡坎的通行反馈。
报告写入 `Temp/TerrainHeightMapTest/`。手工改变初始参考点后，基准检查会报告差异。
`Check Scan Connection (Play)` 直接触发原有地形扫描入口，检查扫描标记是否生成；
它验证场景连接，不模拟键盘双击时间。成功后同时保存 Game 画面到该报告目录。
首次创建工具 `Create Scene` 不会覆盖已经存在的测试场景或关卡数据。

初始实测：10°/25°/45° 坡道分别为 LevelOne/LevelTwo/LevelThree。
全图 263169 个采样与 TerrainData 的最大高度误差约 0.000267 米；
运行时使用这份关卡的预烘焙数据，入口高度约 4 米，扫描连接检查生成 120 个标记。
两处陡坎在现有算法下均可通行；测得台阶残差约 0.107/0.268 米，低于原有的
0.65 米阈值。该值经过机器人接触范围内的坡面拟合，不等同于 0.4/1.0 米的
总高差。此测试保留原算法与通行阈值，后续可据此单独评估台阶检测。

测试场景应单独加载：原有启动脚本会在已加载对象中寻找地图控制器，多个测试
场景同时加载时可能找到另一个场景的地图。
