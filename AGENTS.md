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

## 动态草地

- `HeightMapSurfaceBaker` 将 Grassland 草叶从静态底图分离，另存带地形混合权重、色调和透明度的 GrasslandMask；切片来自草地源图的四个 Sprite，保持地表面板的平铺大小。
- `GrasslandDynamics.cginc` 由地图 Shader 合成草叶，使用全地图坐标保持分块连续；`GrasslandTrampleController` 在玩家预制体上提供固定 8 槽踩踏记录、暂停同步时钟和摆动/压伏参数。编辑器无动态，明确离地时不新增踩踏。
- 默认风摆为草叶长度的 12%、周期 1.1–2 秒；践踏半径为身径的 0.9 倍，强压覆盖到身体外侧，尖端偏移 45%、长度压至 45%，0.2 秒恢复。强弯折按最近的草叶平铺实例采样，跨重复单元边缘不截断。
- `Animal Game/Checks/Grassland Dynamics` 检查 GPU 摆动、暂停、压缩、0.2 秒恢复、分块连续与 Shader 错误；修改烘焙逻辑后重新烘焙地图，避免草叶重复。

## 玩家标记

- `RobotMarkerView.InnerRing.cs` 管理独立的三段内圈：静止呼吸、按带方向的移动速度旋转、侧翻时偶发 0.4 秒内左右左右各 12° 的抖转并回到原角度；外圈固定。源 SVG 和渲染脚本位于 `output/imagegen/`，导入贴图位于 `Assets/Arts/Robot/`。
- 内圈通过 `Shaders/RobotInnerRing.shader` 绘制等长三段圆弧；按绝对移动速度平滑从静止的 68% 尺寸 / 每段 50° 过渡到移动的 82% / 每段 90°，静止呼吸幅度 ±6%；正反向按各自额定速度归一化。
- `Tumbling` 翻滚阶段内圈保留 100% 基础尺寸 / 每段 100°，以 900°/秒快转，按 0.28 秒周期做 ±12% 缩放；进入落地收尾 `FinalRocking` / `Fallen` 后切回小角度间歇抖转。
- 机械臂展开时内圈过渡为左右各 120° 的白色括号弧线，固定 62% 尺寸且不旋转；跟随有效操控向量偏移至多半径的 35%，停止输入平滑回中。实际抓握时两弧以共同中心纵向压至 55% 高度，横向宽度保持不变，松爪复原，收臂后恢复三段内圈；全部使用游戏时间同步暂停。
- `Recycling` 吃垃圾期间，括号以 1 秒周期反复纵向开合，叠加最高半径 22% 的双轴抖动和 ±12° 抖转；抖动频率独立于开合周期，压合时更强，张开时减弱。回收结束或取消后立即停止抖动，恢复普通抓握显示并重置计时；只影响内圈，使用游戏时间同步暂停。
- 相机展开时三段内圈连成无缝整圆（每段 120°），固定使用移动时的正常尺寸；RB 对焦进度使其平滑放大至 1.2 倍。实际 `PhotoCaptured` 事件触发后 0.07 秒压缩至正常尺寸的 60%，0.18 秒复原；关闭相机或禁用标记时清理动画，沿用游戏时间暂停。
- `RobotMarkerView.ScanRing.cs` 由现有 LB 扫描手势驱动：按下先缩小，长按逐渐压至 55%；长按释放或双击扫描触发时，0.08 秒弹至 130%，0.2 秒复原。首次短击在双击窗口内保持缩小，超时平滑复原；输入锁取消不触发弹大。仅普通移动模式、站立且机械臂收起/相机关闭时显示，切换高优先级状态即清空，禁止回放旧动画。
