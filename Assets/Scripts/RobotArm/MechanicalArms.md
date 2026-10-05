# 机械臂配置与验证

## 操作

- 按住 L3 展开；键盘沿用 CapsLock。普通抓持时松开会先放下物体，再依次收回手、小臂、大臂；已经进入回收动画时，则先完整回收，再根据 L3 当前状态决定是否收臂。
- 按住手柄 A / 键盘 Space 持续尝试抓取并维持抓持。将小、中垃圾放到胸前接收区域后，松开的当帧就进入回收动画，无需停留或精确对齐。A 与 L3 同时松开也会优先按胸前位置判断回收；区域外松开立即放下，仅松开 L3、继续按住 A 仍是放下。
- 左摇杆 / IJKL 表示玩家局部 XY 方向；(0,1) 始终朝身体前方，(1,0) 始终朝身体右侧。摇杆阈值使用未经过死区重映射的原始幅度。
- 外圈操作时，输入相对身体前方的角度超过左右 70°，身体向对应方向持续转动；输入回到角度范围内或摇杆回中即停止请求转向。身体转动不会改变输入的局部角度，所以持续推向正右方会持续右转。机械臂目标角度仍限制在 ±70°，并随身体一起转动。
- 抓持小、中垃圾时，摇杆收回内圈会立即开始自动对准身体正前方入口，无需保持一段时间。停靠期间忽略内圈输入方向，也不因机械臂输入请求身体转向，无需精确回中。回收条件只看垃圾当前位置，外圈手动操作也能在胸前区域内松开 A 回收。继续按住 A 并推过退出阈值，可恢复外圈操作。大型垃圾拉拽不进入停靠，也不能直接回收。
- 回收动画一旦开始，摇杆回中、松开 L3、再次按 A 或改变机械臂摇杆方向都不会打断。机械臂输入保持占用，动画结束后才交还左摇杆；L3 仍按住时恢复机械臂操作，已松开时自动收臂。倒地、进入拍照模式、组件禁用或垃圾失效仍会中止并清理本次回收。
- 翻倒时可展开，目标角度限制在 ±70°，不能抓取，也不请求身体转向。拍照模式会放下物体并收回机械臂。

## 给物体添加交互

添加 `WorldInteraction` 组件，`Kind` 是位掩码，可以组合选择：

- `Collision`：参与身体和机械臂障碍检测。
- `Grabbable`：可被机械手抓取。`Required Hands` 为 1 或 2；双手物体只有同一物体同时被两只手命中才会建立抓持。

一个 GameObject 可以同时挂两个组件，分别提供碰撞和抓取范围。优先使用显式指定的 `Box`；否则使用指定或同对象上的 `SpriteRenderer.bounds`；没有 SpriteRenderer 时使用 `Local Center / Local Size`。BoxCollider2D 仅作为几何配置来源，不需要 Rigidbody2D，也不依赖 Unity 物理模拟。手部以实际末端点查询抓取范围。

已有 `HeightMapObstacleFootprint` 继承统一组件，继续使用原来的地图米制圆形范围，无需逐个迁移树木。查询算法位于 `WorldInteractionQuery.cs`；传入同一查询方法的 `WorldInteractionKind` 即可筛选碰撞或抓取。地图存在时查询坐标是地图米，否则使用世界 XY。范围不检查视觉深度 Z。

`WorldInteractionKind` 的值为 `None = 0`、`Collision = 1`、`Grabbable = 2`；组合 mask 匹配任意包含的类型。所有类型共用 `Scripts/World/WorldInteractionQuadTree.cs` 中的树，节点 `KindMask` 用于整棵子树剪枝，AABB 筛选后仍执行原有精确判定。跨子节点的物体保留在父节点，每个组件只存一次。容量为 12、最大深度为 10、最小子节点边长为 0.5 个查询坐标单位；根节点覆盖地图及物体范围，物体越界时重建。切换 Scene/map 上下文时重建共享树，避免混用坐标。

空间索引使用显式 dirty 通知，不逐帧检查移动，普通查询不遍历 `Active`。启用时入队，禁用及销毁即时移除；查询前只刷新去重集合中的对象。没有变化时不调用物体的 `GetShape()`。首次建树、Scene/map 上下文切换或全局失效需要全量读取；根节点越界重建使用已缓存的形状。

运行时通过 `WorldInteraction.WorldPosition / WorldRotation / LocalPosition / LocalRotation / LocalScale`、`SetWorldPositionAndRotation()`、`SetParent()`、`MoveToScene()` 修改变换，通过 `LocalCenter / LocalSize / SetLocalBounds()` 修改本地范围，通过 `SetKind()` 修改类型，通过 `BoxSource / SpriteSource` 更换几何来源。这些接口会自动标记 dirty；变换接口同时通知同一物体及所有子物体的交互组件。`HeightMapObstacleFootprint.RadiusMeters / BlocksTraversal` 也提供通知索引的 setter。

直接修改 Collider 的 offset/size、SpriteRenderer 的 sprite/size/drawMode/bounds，或自定义 `GetShape/Kind` 所依赖的字段后，必须调用每个受影响组件的 `MarkSpatialDirty()`。直接移动父物体或整个层级后，调用 `WorldInteraction.MarkHierarchySpatialDirty(root)`。几何来源若在外部层级，需另外通知引用它的交互组件。多次通知会合并成一次刷新，且 AABB 未变化时仍刷新精确形状。代码中的 `SPATIAL INDEX` 注释标出了需要遵守这个约定的字段。

地图生成及释放会使索引失效；外部修改地图 Transform、渲染器范围或坐标换算后，应调用 `WorldInteractionQuery.InvalidateSpatialIndex()`。Inspector 修改交互字段通过 `OnValidate` 入队，编辑器 Transform/几何来源的 Undo 属性修改和 Undo/Redo 通过事件使索引失效，不使用逐帧轮询。其他脚本、物理模拟或动画若绕过封装接口，仍须在修改后、查询前主动通知；`OnDidApplyAnimationProperties` 只覆盖 Unity 发送该回调的对象。

`Recyclable` 控制能否回收，`Size` 选择 Small / Medium 的停靠位置。`On Grabbed / On Released / On Recycled` 可连接玩法事件。默认回收完成后停用整个物体，可继承组件覆盖 `Recycle` 来接入库存或对象池。一次只搬运一个物体；多个候选按接触点距离、实例 ID 排序选取。

## 机械臂参数

`RobotArmController` 的固定几何参数在创建机械臂时缓存：大臂、小臂长度以 `Arm Length` 为参考；安装点、双手总间距和臂段碰撞宽度以身体直径为参考。游玩期间不会根据目标距离伸缩，只有展开/收回动画会改变可见长度。双手实际目标仅沿机器人局部 X 轴分别偏移半个间距。

停靠默认阈值与 `RobotMarker.prefab` 为进入 ≤0.40 立即对准，退出 >0.50；0.40–0.50 保持原有内/外圈状态，防止边界抖动。`Dock Enter Delay` 和停靠计时已删除。方向死区 `Left Stick Dead Zone` 为 0.08；≥0.95 使用最大目标距离。`Aim Smoothing Time` 保留项目当前 0.37 秒，抓持减速仍会影响机械臂实际移动速度，但没有额外的回收等待时间。

Small / Medium 的停靠位置是机器人局部坐标，以 `Arm Length` 为长度参考。回收接收区域以该位置为中心，是一个椭圆：`Recycle Zone Half Width Of Body Diameter` 默认为 0.35，`Recycle Zone Half Depth Of Body Diameter` 默认为 0.30，均以身体直径为单位。项目直径为 0.72，对应横向 ±0.252、前后 ±0.216 的范围。用已抓持垃圾的中心判定，无需停靠状态、摇杆回中或对准到某个精确点；完全伸在远处、侧面或身后的垃圾仍会放下。

停靠时目标固定为入口位置，并补偿抓持偏移。若偏心抓持导致目标超出臂长，握持位置会缓慢向可达范围调整，仅调整必要的距离；外圈操作保留原来的握持偏移。松开 A 时先检查这一帧开始时的垃圾位置，再处理 L3 释放、摇杆转向和机械臂移动，避免原本在区域边缘的有效释放被移出范围。回收动画直接从实际释放位置开始，不会先跳到停靠中心。翻倒或拍照模式仍优先放下垃圾。

胸前接收范围只用于内部位置判断，游戏中不显示黄色、绿色提示圈；相关显示开关、颜色参数和接收范围 Gizmo 已移除。

`State` 对外区分 Retracted、Extending、OuterOperating、Docking、Retracting、Recycling；另提供 `IsBlocked`、`IsRecycleReady`、`HeldObject` 和实际左右手位置。保留翻身辅助使用的原有公开属性，`CurrentTargetLocal` 仍表示输入在身体局部坐标下的方向及原始幅度。

选中运行中的机器人可查看机械臂碰撞框。展开受阻时停在实际进度，移开障碍会继续展开；正常展开后遇障碍不会撤销抓取资格。机械臂避开自身及其当前持物的碰撞组件。机械臂和身体移动分小步检测，已有重叠允许退离或切向移动。

机械手各自带有 `RobotHandAnimation`，公开的 `PlayGrab / PlayRelease / PlayRecycle` 为动画资源预留。回收暂用可调时长的物体移进入口动画，完成时调用物体的 `Recycle`，每次只完成一次。回收期间的 `IsArmModeActive` 和移动控制器输入占用保持为真；完成时按实际 L3 输入更新状态，并解除抓持减速。回收完成回调若禁用或销毁机器人，不会再次恢复输入占用或显示机械臂。

## 回归检查

Unity 菜单：`Animal Game > Validation > Run Mechanical Arm Checks`。

检查在独立 Preview Scene 中创建临时对象并清理，不修改已打开场景。覆盖 IK 可达范围、统一查询、双手抓取、实际 prefab 参数下的原始摇杆漂移、固定入口、无计时停靠、滞回边界、偏心抓持调整、胸前范围内立即释放回收、A 与 L3 同时释放、动画中松开 L3 后完整回收、完成后恢复操作或收臂、强制中止、范围外放下、提示圈移除、部署阻挡、身体运动碰撞和翻倒限制。真实手柄手感和最终美术动画仍应在 Play Mode 中验收。
