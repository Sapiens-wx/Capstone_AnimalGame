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

## 玩家标记

- `RobotMarkerView.InnerRing.cs` 管理独立的三段内圈：静止呼吸、按带方向的移动速度旋转、侧翻时偶发 0.4 秒内左右左右各 12° 的抖转并回到原角度；外圈固定。源 SVG 和渲染脚本位于 `output/imagegen/`，导入贴图位于 `Assets/Arts/Robot/`。
- 内圈通过 `Shaders/RobotInnerRing.shader` 绘制等长三段圆弧；按绝对移动速度平滑从静止的 68% 尺寸 / 每段 50° 过渡到移动的 82% / 每段 90°，静止呼吸幅度 ±6%；正反向按各自额定速度归一化。
- `Tumbling` 翻滚阶段内圈保留 100% 基础尺寸 / 每段 100°，以 900°/秒快转，按 0.28 秒周期做 ±12% 缩放；进入落地收尾 `FinalRocking` / `Fallen` 后切回小角度间歇抖转。
