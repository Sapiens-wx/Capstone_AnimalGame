# 项目协作约定

## 美术素材交付

- 生成或修改的美术素材默认保存到 `output/imagegen/` 下，并向用户提供预览和文件链接。
- 只有用户明确要求导入游戏时，才将生成素材放入 `Assets/`、创建 Unity 导入元数据，或修改场景、Prefab 和资源引用。
- 黄色用#D4B854

## Editor Window创建
- 如果只是为了一次性完成任务创建的EditorWindow (MenuItem), 用完之后把它删除，不要留着

## Engineering Principles

- Do not restore working directories when implementing new tasks.
- Prefer simple, direct implementations over defensive complexity.
- Do not introduce fallback paths for hypothetical scenarios.
- Avoid redundant null checks and repeated validation of established invariants.
- Maintain a single source of truth for state and configuration.
- Do not preserve legacy implementations unless compatibility is required.
- Prefer fail-fast behavior over silently hiding unexpected errors.
- Make the smallest change that fully solves the problem.
- Optimize operations with meaningful cost. Do not add caches, shared mutable state,
  or synchronization bookkeeping for cheap reads without evidence of a performance problem.
- Judge optimizations by both expected benefit and added complexity. Prefer direct
  computation when caching introduces more state and lifecycle management than it saves.
- Establish and rely on project invariants before adding validation or supporting
  additional scenarios. Do not introduce state or checks for hypothetical configurations.
- Store only necessary state. Prefer local values for cheap, current reads, and derive
  values from their owning source rather than maintaining redundant copies.

## 地形类型

- TerrainKind 新增 Sand = 9，原有 0–8 编号不变；主地图和 RockyMountain 地表 Palette 已添加 Sand，初始沿用 Rock 的平铺与透明度参数。原 Rock.png 已重命名为 Sand.png，GUID 不变，Sand 使用 Sand.png，Rock 已切换为新 Rock.png（GUID bfc7b968cbb6b2943868fa4967dbc3f4）；两者均直接采样原图并参与等高线淡化。

## 动态草地

- Rock 在地表烘焙 Shader 中直接采样原贴图 RGBA，绕过小尺寸图集和透明度归一化，参与等高线区域透明度渐变（边缘乘 23.5%、内部乘 5%）；Rock Palette 的 Opacity 为 0.5，TutorialTemporaryMap（MainHeightMapLevel）平铺尺寸为约 4.5255 米，RockyMountain 为约 9.051 米，地形交界继续使用原有融合。

- `HeightMapSurfaceBaker` 将 Grassland 草叶从静态底图分离，另存带地形混合权重、色调和透明度的 GrasslandMask；切片来自草地源图的八个 Sprite，保持地表面板的平铺大小。
- `GrasslandDynamics.cginc` 由地图 Shader 合成草叶，使用全地图坐标保持分块连续；`GrasslandTrampleController` 在玩家预制体上提供固定 8 槽踩踏记录、暂停同步时钟和摆动/压伏参数。编辑器无动态，明确离地时不新增踩踏。
- 默认风摆为草叶长度的 12%、周期 1.1–2 秒；践踏半径为身径的 0.9 倍，强压覆盖到身体外侧，尖端偏移 45%、长度压至 45%，0.2 秒恢复。草叶按平铺宽度 ÷ √(8×密度系数) 的布局单元独立定位（单元内 ±40% 随机偏移），地图密度系数为 1.365，平均每个原平铺面积 10.92 根，增加数量与重叠而不缩小单根草叶；草叶长度为原切片的 56%，宽度不变；固定种子随机切片，区域朝向以 20 倍平铺宽度平滑变化，单叶叠加 ±10°。旋转后的根部固定，踩踏沿草叶局部横轴偏倒，3×3 邻域采样保证跨单元不截断。
- `Animal Game/Checks/Grassland Dynamics` 检查两张地图的 GPU 排列确定性、种子、区域朝向连续性、摆动、暂停、旋转压缩/恢复、分块连续与 Shader 错误；修改烘焙逻辑后重新烘焙地图，避免草叶重复。

- Grassland 额外覆盖层在地图资产的 Grassland Overlay Patches 配置 Grass1/Grass2/Grass3，当前 Grass1 槽位使用 Grassland.png 整图，已绑定纹理按固定世界坐标随机选择。高草生长区域由两层连续 Value Noise 加域扭曲决定，覆盖参数为 0.95（不再代表逐格出现概率）；搜索格宽为 6 倍贴图宽度，草丛中心在格内作 ±45% 固定随机偏移，消除等距分组。每片 4–8 簇，围绕中心以随机相位、角度扰动、半径与长宽变化分散，基准簇间距仍为平铺宽度的 80%；位置按种子固定，跨地图分块连续。基础尺寸为平铺宽度的 65%，每簇固定随机水平翻转（50% 概率），横向 0.8–1.2 倍、纵向 0.96–2.4 倍，覆盖层按簇使用固定随机相位、草高 2.5%–4% 的风摆幅度及小草周期的 1.5 倍，根部固定；接入同一组踩踏记录与暂停时钟，经过时根据实际移动方向反向轻摆（尖端最大约草高 30%，不压短），停止移动后约 0.2 秒恢复；检查 3×3 草丛搜索格并剔除超出旋转/弯曲范围的候选，避免跨格截断；沿用 GrassDirection 区域朝向，源 Alpha 全强度叠加，不乘基础草叶的不透明度。

## 玩家标记

- `RobotMarkerView.InnerRing.cs` 管理独立的三段内圈：静止呼吸、按带方向的扳机驾驶输入旋转（满输入 665.28°/秒，前进顺时针、后退逆时针）；使用 `RobotMover.CurrentThrottleInput` 的死区/方向处理结果，不受实际速度、碰撞或抓握阻力影响；扳机输入同方向减小（包括松开过程中的非零值）时以恒定角减速度滑停，满输入约 1.2 秒停稳，重新输入立即跟随，运动锁定、侧翻或禁用时清空余速，使用游戏时间同步暂停。侧翻时偶发 0.4 秒内左右左右各 12° 的抖转并回到原角度；外圈固定。源 SVG 和渲染脚本位于 `output/imagegen/`，导入贴图位于 `Assets/Arts/Robot/`。
- 内圈通过 `Shaders/RobotInnerRing.shader` 绘制等长三段圆弧；按绝对移动速度平滑从静止的 50% 尺寸 / 每段 68° 过渡到移动的 82% / 每段 90°，静止呼吸幅度 ±12%；静止弧长保持与原 68% / 50° 相同，正反向按各自额定速度归一化。
- `Tumbling` 翻滚阶段内圈保留 100% 基础尺寸 / 每段 100°，以 900°/秒快转，按 0.28 秒周期做 ±12% 缩放；进入落地收尾 `FinalRocking` / `Fallen` 后切回小角度间歇抖转。
- 机械臂展开时内圈过渡为左右各 120° 的白色括号弧线，固定 62% 尺寸且不旋转；跟随有效操控向量偏移至多半径的 35%，停止输入平滑回中。实际抓握时两弧以共同中心纵向压至 55% 高度，横向宽度保持不变，松爪复原，收臂后恢复三段内圈；全部使用游戏时间同步暂停。
- `Recycling` 吃垃圾期间，括号以 1 秒周期反复纵向开合，叠加最高半径 22% 的双轴抖动和 ±12° 抖转；抖动频率独立于开合周期，压合时更强，张开时减弱。回收结束或取消后立即停止抖动，恢复普通抓握显示并重置计时；只影响内圈，使用游戏时间同步暂停。
- 相机展开时三段内圈连成无缝整圆（每段 120°），使用移动时正常尺寸的 80%；RB 对焦进度使其平滑放大至该尺寸的 1.2 倍，基础尺寸和长按上限均相较原相机模式缩小 20%。实际 `PhotoCaptured` 事件触发后 0.07 秒压缩至相机正常尺寸的 60%，0.18 秒复原；关闭相机或禁用标记时清理动画，沿用游戏时间暂停。
- `RobotMarkerView.ScanRing.cs` 由现有 LB 扫描手势驱动：按下先缩小，长按逐渐压至当前半径的 72%；长按释放或双击扫描触发时，0.08 秒弹至 130%，0.2 秒复原。首次短击在双击窗口内保持缩小，超时平滑复原；输入锁取消不触发弹大。仅普通移动模式、站立且机械臂收起/相机关闭时显示，切换高优先级状态即清空，禁止回放旧动画。
- `RobotMarkerView.BodyHatches.cs` 与 `RobotBodyHatches.shader` 从原外圈分出左右弧形盖，闭合时拼回原图；手动展臂仅在垃圾进入回收判定区或回收进行中开盖，离开区域关盖，物品栏按原开关演示；开启时顶部残段立即隐藏，舱门显示双层弧形描边并连接两端，绕镜像铰点转开 50°，开启 0.4 秒、关闭 0.2 秒。`RobotArmController.Inventory.cs` 订阅物品栏开关，沿用拍照接管规则安全放下抓持物、取消未完成回收，再将空右臂朝向前方 18% 臂长的内收目标弯折，左臂伸出至普通展臂默认位置并保持不动；连接点保持普通展臂位置，右臂身体侧臂段相对前方朝左内偏 15°，手侧臂段朝中心目标；物品栏不再对大小臂或连接环使用额外缩放；关闭后收臂合盖。仅此物品栏演示及其指针淡出使用非缩放时间，不运行世界接触推挤、抓取、回收或身体移动；指针隐藏至演示收尾结束。

- 照片结算地图截图在 PhotoContourCapture 的临时材质中将 _GrassOverlaySettings 置零，仅隐藏高草覆盖层；正常地图材质保持原有显示。

- 岸边地形贴图由 MapWater.cginc 的 MapShoreTerrainOpacity 使用实际水体遮罩、约 1.5 米的加权邻域生成透明度淡出，邻域水体覆盖量在 0–0.25 间进行 SmoothStep，再将剩余不透明度平方，使接触岸线时接近零；DynamicHeightContours 在合成静态地表与大小草后统一应用，仅影响地形贴图，等高线与水体自身保持原绘制方式，无需重烘焙。

- Grassland 运行时保留靠近等高线的原不透明度，小草与高草的远端内部最低 Alpha 分别设为 2% 和 3.5%；_GrassContourOpacity 传入烘焙近/远端系数与两层目标值，水体岸边淡出继续叠加。

- Grassland 当前平铺尺寸：主地图/Tutorial 为 1.6 米，RockyMountain 为 0.8 米；按参考图将原平铺边长减半，底层草与高草均使用此世界尺度。
