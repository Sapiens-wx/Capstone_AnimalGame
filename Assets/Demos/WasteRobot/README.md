# 大型垃圾处理机器人：进出舱 Demo

这是独立的俯视角测试场景：四周实线阻挡玩家和机械臂，顶部虚线入口允许通行。玩家中心穿过入口后，原地切换外观、内部图和对应碰撞；从同一入口退出时恢复外观。没有传送、场景加载或玩家控制器替换。

## 开始测试

1. 打开 `Scenes/WasteRobotTraversalDemo.unity`，点击 Play。
2. 使用现有玩家操作：W/S 前进和后退，A/D 转向。出生点位于顶部入口外，初始朝向机身，按 W 可以进入，按 S 可以退回。
3. 左上角显示当前内外状态和切换次数。勾选 `Show active collision outlines` 可以核对正在使用的碰撞范围；绿色为硬边，黄色为入口。
4. 测试机身、轮子和机械臂的阻挡，再进舱测试内墙及右侧两个空舱。反复进出或在入口附近小幅移动，画面应保持稳定。

也可以通过菜单 `Animal Game > Demos > Waste Robot > Create Or Open Demo` 打开。

## 交付内容

- `Prefabs/WasteRobotBase.prefab`：可复用的机械主体，不包含玩家或摄像机。
- `Art/Exterior.png`：带顶部太阳能板的外部素材。
- `Art/Interior.png`：与外图同画布的空内部素材，保留机械臂和四轮，右侧两个舱室的通路已经打开。源图为 `output/imagegen/waste-robot-interior-aligned-2026-10-21/interior-aligned-v3.png`。
- `Runtime/WasteRobotBase.cs`：入口穿越检测及内外状态切换。
- `Runtime/WasteRobotCollisionLayout.cs`：依据两张图手工标定的分段碰撞数据。
- `Runtime/WasteRobotDemoScene.cs`：仅测试场景使用的玩家、摄像机初始化及调试显示。
- `Runtime/WasteRobotTransitionEffects.cs`：Prefab 自带的世界画面淡黑转场和局部摄像机协调。
- `Runtime/WasteRobotInteriorCameraZoom.cs`：仅在运行时为受控玩家的摄像机叠加室内缩放，退出时恢复原尺寸。
- `Runtime/WasteRobotScan.cs`：整车静态扫描目标的外景资格、转场隐藏及剩余显示时间继承。
- `Runtime/WasteRobotScanLayout.cs`：整车扫描外包络，独立于碰撞形状。
- `Editor/`：仅在明确调用菜单或批处理入口时执行的生成和验证工具。

## Prefab 结构与接入

`WasteRobotBase` 的局部原点位于虚线入口中心。`Exterior` 和 `Interior` 各有 `Visual` 与 `Collision` 子物体，同一时刻只启用一组。两图共用 1779×884 画布、入口像素中心 (991.5,205)、289 像素入口宽度，映射到 2.4 个世界单位；PPU、Pivot、本地位置、缩放和世界边界完全一致。内部图不再按自己的门宽单独放大。

四轮、完整机械臂、断臂及共同外壳边缘复用同一份碰撞坐标，在两态下位置和大小不变。内部只重建舱内结构和右侧两扇开门，中央屋顶设备的碰撞随屋顶隐藏。左侧收集口与主舱之间保留图中实线墙，不作为入口。

将 Prefab 放入其他场景后，默认启用 `Auto Bind Player`：Target 为空时，会等待并绑定**同一场景**地图 Bootstrap 在运行时生成的玩家；普通场景则绑定唯一活动的 `RobotMover`。不需要再放置 `WasteRobotDemoScene`，也不会创建第二个玩家或摄像机。

也可以在 `WasteRobotBase` 的 Target 字段指定玩家 Transform，或调用 `Bind(player.transform)`，显式目标优先。初始在内部时调用 `Bind(player.transform, true)`。同场景存在多个玩家、且没有地图 Bootstrap 指明受控玩家时，不会任意选择；应明确指定 Target。自动绑定的玩家销毁、禁用或移出场景后，会等待可用的新玩家，并重新建立入口采样。

`Bind(null)` 清空当前目标并保留自动绑定设置。需要完全手动管理时，先关闭 `Auto Bind Player` 再清空 Target。建议保持均匀缩放。门口身体净空会依据已绑定地图玩家的实际碰撞半径和地图单位比例计算，平地场景则使用玩家身体尺寸。

碰撞使用本项目玩家现有的 `WorldInteraction` 系统，每段是固定的 `Collision` 盒形或圆形，不能抓取或推动。它们直接参与玩家及机械臂已有的移动检查，不依赖 Unity 的 Rigidbody2D / Collider2D。这里只标定实体轮廓和内墙；舱内地面分区、太阳能板面板线等装饰线不作为墙。

入口检测检查上一帧到当前帧的运动线段，要求穿越入口的有效宽度，并保留玩家半径余量。越过门槛 0.1 单位后才切换，避免在门线上停留时反复闪烁。根物体保持启用，只切换两组视图和碰撞。

## 进出舱转场与镜头

Prefab 根物体的 `WasteRobotTransitionEffects` 默认开启两项独立效果：

| Inspector 参数 | 默认值 | 行为 |
| --- | --- | --- |
| Fade Enabled | 开启 | 淡黑、切换显示、淡入；现有 Overlay HUD 保持显示 |
| Fade Out Duration | 0.18 秒 | 当前世界画面淡至黑色 |
| Black Hold Duration | 0.04 秒 | 保持全黑，且至少跨过一个完整渲染帧 |
| Fade In Duration | 0.22 秒 | 显示最新的内外状态 |
| Zoom Enabled | 开启 | 室内缩放与室外恢复 |
| Interior Camera Size | 6.5 | 没有扫描、拍照或震动倍率时的室内正交尺寸 |
| Zoom Duration | 0.3 秒 | 平滑改变室内倍率，退出回到原有尺寸 |
| Camera Override | 留空 | 自动使用已绑定玩家的地图相机或跟随相机；也可指定同场景正交相机 |

例如外部尺寸原为 8，进入后平滑到 6.5，退出恢复 8；外部为其他尺寸时则恢复那个尺寸。扫描、拍照和震动仍由原组件组合，室内倍率叠加在结果之上，不改它们保存的基准尺寸。原有跟随、旋转和操作保持原样。

跨门槛时碰撞与内外状态仍立即切换。淡黑前保留一份只含 SpriteRenderer 的旧图代理，遮罩全黑后再显示当前内部或外部图；代理不复制碰撞或脚本。快速反向以最后状态为准，不堆叠协程，也不暂停玩家或修改 `Time.timeScale`。效果使用未缩放时间，暂停时不会把缩放逐帧累乘。

`Bind`、禁用/销毁组件、目标失效或相机替换时会清理旧黑幕和代理，并释放自身缩放。初次加载或 `Bind(player, true)` 直接显示对应状态，不额外播放进舱转场。所有遮罩和相机适配器均为运行时创建，不写回现有摄像机 Prefab 或正式场景。

## 整车静态扫描

把此 Prefab 放入原本支持扫描的地图后，直接使用现有扫描操作。扫描波碰到车轮、远端机械臂或车身的整车包络时，会触发同一个目标，显示垃圾现有的淡黄色斜线填充、外围菱形点、淡入、呼吸及淡出。时长、颜色和密度使用现有静态扫描默认配置；大车不启用小垃圾的额外密度补偿。

根节点只有一个 `ScannableInteractable`，明确绑定 `Exterior/Visual`。12 个标定点覆盖四轮、完整臂、断臂和后部舱体，排除透明画布四角。沿用现有凸包规则，因此机械臂与车身之间的部分空隙会被包含。扫描包络不产生碰撞，也不赋予抓取、推动、拆解或拾取行为。

`WasteRobotScan` 管理此车的扫描组件开关：外景可扫描，进入室内或淡黑期间退出扫描注册，避免 Screen Space Overlay 扫描图案浮在黑幕上。暂停显示时保存剩余寿命、动画阶段、世界方向和菱形点随机种子；回到外景时按实际经过的模拟时间扣除寿命后继承，已经过期则不再出现。暂停游戏时寿命也暂停，进出舱不会刷新显示时间。`Bind`、禁用/销毁适配器或停用机械控制器会清除旧扫描状态。

临时关闭整车扫描请关闭根节点的 `WasteRobotScan`；适配器负责 `ScannableInteractable.enabled`，不要用后者的启用框控制功能。外部的扫描纹理沿用垃圾原有显示规则；机械 Sprite 的圆形视野裁剪与扫描 UI 各由原系统处理。

独立进出舱 Demo 仍保留原来未配置扫描输入的设置；完整输入体验在已接入玩家扫描系统的地图中测试。新增 `Validate Scanning` 菜单检查目标、轮廓和状态；Play Mode 验证工具使用真实扫描波命中函数及正式扫描 UI，不模拟蓄力输入。

## 隔离范围

全部新增资源、脚本及材质位于 `Assets/Demos/WasteRobot/`。测试场景引用原有玩家和摄像机 Prefab，仅保存实例级配置：平地移动、不随玩家旋转的正交摄像机，以及关闭本测试未配置的拍照和生物扫描组件。

本 Demo 不修改原玩家 Prefab、现有场景、移动或碰撞代码、Input 设置、Build Settings、存档或正式地图，也不自动生成/加载到正式游戏。充电、加工、存储、通讯和改装功能本轮没有接入；内部保留为空舱。

## 编辑与验证

修改图片后，先更新 `WasteRobotCollisionLayout` 的像素标定，再运行菜单 `Rebuild Demo Assets`。重建前先关闭 Demo 场景，并保存其他未命名场景。该操作会重建本目录下的 Demo Prefab 和场景，因此不要将手工场景修改留在重建目标中。

菜单 `Validate` 检查两态完整画布和世界边界、轮臂碰撞坐标一致、双仓身体净空、分段碰撞、入口净空、显示和碰撞切换、入口抖动、斜向/高速穿越、门外穿越、20 次往返、变换后的入口，以及场景对原玩家/摄像机的引用。

批处理入口：

```text
AnimalGame.Demos.WasteRobot.Editor.WasteRobotDemoBuilder.BuildBatch
AnimalGame.Demos.WasteRobot.Editor.WasteRobotDemoValidation.RunBatch
AnimalGame.Demos.WasteRobot.Editor.WasteRobotDemoPlayModeSmoke.RunBatch
AnimalGame.Demos.WasteRobot.Editor.WasteRobotMapSceneSmoke.RunBatch
AnimalGame.Demos.WasteRobot.Editor.WasteRobotTransitionSmoke.RunBatch
AnimalGame.Demos.WasteRobot.Editor.WasteRobotScanValidation.RunBatch
AnimalGame.Demos.WasteRobot.Editor.WasteRobotScanSmoke.RunBatch
```

项目已有编辑器初始化工具会在 Unity 启动时维护其他资源。自动化验证应在完整的项目副本中运行，仅将本 Demo 的生成结果取回；不要为了验证而启动正式工作目录的 Unity 批处理。

## 本次验证结果

使用 Unity 6000.0.66f2 在完整项目副本中验证：

- 编译、Prefab/场景生成及 11 组编辑器检查通过，包含完整画布注册、两态世界边界、固定轮臂坐标、双仓通道和保存重开后的实例配置检查。
- Play Mode 使用虚拟手柄驱动原有 `RobotMover.Update`，63 项检查通过：真实驶入、倒退退出、准确切换两次、两右仓前进/倒车往返、内外碰撞注册/注销，以及外部轮子和内部四轮/两臂逐个真实撞墙阻挡。
- 追加 `-wasteRobotSmokeFps 15 -wasteRobotSmokeThrottle 1`，以 1/15 秒模拟帧步长、满油门重复上述驾驶和撞墙检查，63 项通过。这里验证的是模拟步长下的行为，不是性能帧率指标。
- 已检查实际摄像机在相同世界边界下渲染的外部与内部截图，机械臂及轮子均保留。报告和截图保存在工作目录 `output/validation/waste-robot-aligned/` 及其 `15fps/` 子目录中。
- 原玩家/摄像机 Prefab 及相关项目设置与验证副本内容一致；实现改动限于本 Demo，用户自行摆放机械的地图场景修改予以保留。

### 跨场景直接放置验证

- Prefab 默认开启同场景自动绑定。新增 10 组玩家绑定检查：延迟生成、跨场景隔离、多玩家歧义、Bootstrap 受控玩家优先、显式目标、手动关闭自动和目标失效后重找。
- 自动绑定修复后，原平地 Demo 的 63 项实机回归通过。
- 已在隔离项目副本中加载用户保存的 `Official_Map_Test_02`，验证原机器实例（位置 -78.96,-270.52，缩放 1.89，旋转约 -13.176°）在 Target 为空的情况下自动绑定地图运行时生成的真实玩家，并由真实手柄输入驶入/倒退出舱。测试没有调用 Bind、传送穿门或替换地形评估器；仅将玩家放到门外作为测试起点。
- 原摆放点的前后路径均通过真实地形评估，无需移动机械；玩家地图碰撞半径换算为约 0.210261 世界单位。临时以 0.25 缩放检查门口净宽仍有效，随后恢复原值。
- 地图实测 15 项检查通过，Play 前后场景文件哈希和原实例变换不变。原工作目录中的用户场景不写入、不覆盖。报告与截图位于 `output/validation/waste-robot-scene-binding/`。

### 转场与室内缩放验证

- Unity 6000.0.66f2 编译、重建 Demo 及 25 组编辑器检查通过（原 11 组、自动绑定 10 组、转场默认配置/隔离开关/即时碰撞/清理 4 组）。
- 新增 Play Mode 转场检查在正常时序及 1/15 秒模拟帧步长下各通过 30 项：真实驾驶进出、6.5 室内尺寸、退出恢复外部尺寸、原扫描/拍照倍率叠加、完整黑帧、快速反向请求、独立开关、Bind 中断以及禁用/销毁清理。这里的 15 FPS 是模拟帧步长验证，不是性能帧率指标。
- 验证暂停时转场仍完成且连续 20 帧缩放不累乘；相机替换及仅销毁 Camera 组件时，旧遮罩、代理和缩放所有权均释放。
- 保持实际运行镜头构图渲染外部、全黑和室内截图。全黑验证临时改用明亮蓝色背景，同步读回的画面 100% 像素为黑，随后立即恢复背景；不是只检查透明度数值。
- 原 Demo 的 63 项驾驶/轮臂/内墙碰撞回归通过；当前用户保存的地图副本 15 项真实进出检查通过。最新机器位置 (-76.53,-269.68)、缩放 2.76、旋转约 -16.074°，无需改变摆放点。原地图文件字节不变。
- 本轮报告和实际相机截图：`output/validation/waste-robot-transitions/`。

### 整车扫描验证

- 使用独立副本 `output/validation/waste-robot-scan-unity-project/` 编译和重建，仅取回本 Demo 生成结果。另一项并行制作的 `CheckpointRefit` Demo 从此验证副本排除，源工程中的该目录保持原样。
- 32 组编辑器检查通过：原 25 组及新增扫描 7 组，覆盖唯一目标、明确绑定外图、既有样式、20 个整车采样点、透明画布四角排除、旋转/缩放、多车和跨场景、内景暂停及清理。
- 正式 `StaticInteractableScanRegressionChecks` 通过，包含原垃圾 Prefab、波命中、方向/种子继承、寿命、暂停、斜线/菱形几何、重叠处理及投影等既有检查。
- Play Mode 使用真实 `BioScanController.RevealTargetsCrossedByWave` 和原 `BioScanStaticAreaUI`，验证远端机械臂/车轮命中整车、再次扫描连续刷新、内景/淡黑全程注销、返回后扣时与种子/角度继承、室内期满、快速反向以及禁用/销毁。测试未模拟扫描蓄力输入。
- 正常时序和 1/15 秒模拟帧步长各通过 25 项 Play Mode 检查；低帧率模式同时按真实时间节流，保持原生 7 秒扫描寿命与未缩放转场时间一致。这是行为检查，不是性能基准。
- 截图由原摄像机和实际 UGUI Canvas RendererList 渲染，保持相机位置、旋转、尺寸及分辨率。`-overlay.png` 是世界与扫描图案合成结果的 GPU 副本；单次绘制后复制，避免重复执行 UI RendererList。室内同时检查扫描双网格清空和金黄色扫描像素为零。
- 本轮报告与截图保存在 `output/validation/waste-robot-scanning/`，源地图文件与原摆放均未写入。
