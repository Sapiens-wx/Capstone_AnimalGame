# Inventory

运行时格子、数量、首次获得类型与未查看标记由 `InventoryStore` 管理；`InventoryItem` 只保存物品定义。独立的 `InventoryController.cs` 管理格子、菜单、MOVE、输入和星号。它通过 `IInventoryHost` 请求打开/关闭背包，不依赖 MainUI 的内部字段。MainUI 负责 UI 绑定、Inspector 配置、整体动画与暂停，并转发原有公共接口。

## 配置

- 物品：`Assets/Data/Inventory/Items/`；奖励表：`Assets/Data/Inventory/InventoryRewards.asset`。两种 Scrap 每格上限为 10。
- 在 MainUI 的 **Inventory Rewards** 中配置奖励表；未指定时不发放奖励。表中可配置每种垃圾的固定奖励、随机抽取次数与权重。
- MainUI Inspector 中可调整星号完整闪烁周期、摇杆死区与连发时间。
- `EmptyInventoryItem.uxml` 包含格内 `ItemAStar`；右侧继续使用现有 `Label-AStar`。

## 接口

```csharp
var result = MainUI.Active.AddItem(itemDefinition, quantity);
// result.Added / result.Remaining：实际加入 / 溢出数量。
// 按需求，回收奖励的溢出部分直接忽略。
MainUI.Active.UseRequested += item => { /* 后续实际使用逻辑 */ };
MainUI.Active.InfoRequested += item => { /* 后续 Info UI */ };
MainUI.Active.DiscardRequested = slot => { /* 后续数量选择 UI */ };
```

不设置 `DiscardRequested` 时直接删除整格；设置后由调用方完成删除。`SetItem` 保留原接口，作为初始化/外部替换操作，不生成新物品提示。不要直接写 `MainUI.Items` 数组；使用 `SetItem`、`AddItem` 或 `InventoryStore` 的方法以保持数量与提示一致。

成功的 `WorldInteraction.Recycle` 调用才会通过 `GarbageInventoryRewards` 发奖，并防止同一垃圾对象重复发放；抓取、放开与销毁不会发奖。现有流程停用已处理对象，没有新增对象池复用规则。

## 已采用的补充规则

- MOVE 框可以经过占用格；占用格不显示预览，A/左键不能提交。取消后返回源格，背包保持打开。
- 星号仅在首次成功获得该类型时产生。后续同类型增加数量或新占格均不重新提示；默认配置物品算已获得过。
- 普通浏览/交互菜单中右键没有作用；MOVE 中右键取消。
- N/B 每次退一层；USE/INFO 调用占位接口后关闭菜单，不消耗物品。

## 验证

Unity 菜单：`Animal Game/Validation/Run Inventory Checks`。在 Edit Mode 检查库存、菜单、预览、鼠标事件与布局；在 Play Mode 运行时额外模拟手柄、键盘与鼠标输入。`InventoryRegressionChecks.RunRecycleChecks()` 在 Play Mode 验证实际回收调用链、重复发奖防护与取消/销毁行为。
