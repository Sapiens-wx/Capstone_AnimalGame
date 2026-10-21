# 通用交互标记

将同目录的 `InteractionMarker.prefab` 放在可交互物品下面，原点对齐交互中心。装配台已经通过嵌套 Prefab 接入；其他物品也直接复用同一个 Prefab。

- `InteractionMarker` 只处理显示和旋转，不检测按键、不决定交互距离、不打开菜单。
- `Diameter`、`Tint`、内外环转速和排序可在 Inspector 中独立覆盖。默认外环顺时针 30°/秒，内环逆时针 45°/秒，使用游戏时间。
- 物品不可交互或自己的菜单打开时，业务脚本调用 `SetSuppressed(true)`，恢复时调用 `SetSuppressed(false)`；父物体隐藏时也会一起隐藏。
- 材质读取游戏现有的圆形视野参数，逐像素裁剪。主视野无效或 HUD 关闭时隐藏；没有该 HUD 的独立测试场景不会显示标记。
- 两环是共享网格，位于 XY 平面，默认直径 1；没有碰撞体，不改变物品原有的交互逻辑。

共享资源由菜单 `Animal Game / Interaction / Create Shared Marker Assets` 创建。菜单不会自动给场景中的其他物品添加标记。
