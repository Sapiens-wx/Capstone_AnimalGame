如果结构变化，需要更新AGENTS.md，但是确保内容精炼，不要加过多细节

# 动物 AI 实现约定

- 以下脚本路径均相对于 `Scripts/Animals/`。
- `Behaviours/Species/`：物种专属的 `XxxBehaviour`，只实现该物种的差异逻辑。
- `Behaviours/Common/`：通用 Behaviour、可复用 Action 和 `XxxActionDefinition`；鸟类通用逻辑也放在这里。
- `Behaviours/Actions/`：独立的专用 Action 实现（如打斗、坡地逃跑）。
- `Behaviours/`：共享状态机、日常调度与行为基类；`Core/`：个体运行数据、移动、感知、生命及统一模拟；`Configuration/`：配置类型；`World/`：场景表现与声音。
- 新物种必须继承适用的通用 AI：鸟类优先继承鸟类通用 Behaviour（当前为树栖鸟类的 `TreeBirdBehaviour`）；其他动物直接继承 `GenericAnimalBehaviour`。不要直接继承 `AnimalBehaviourSet` 重写整套流程；可复用的类别逻辑提取到通用 Behaviour。
- 动作执行类继承 `AnimalAction`，负责执行与生命周期；动作配置类继承 `AnimalActionDefinition`（树栖鸟类用 `TreeBirdActionDefinition`），通过 `CreateRuntime()` 创建独立动作实例。
- ScriptableObject 只保存共享配置；目标、计时器等运行状态保存在个体或动作实例中，不能写入共享资产。
- 物种配置资产放在 `Data/Animals/Behaviour/`，动作配置资产放在 `Data/Animals/Behaviour/SpeciesActions/`，通过 `AnimalSpeciesConfig.DailyBehaviours` 引用；权重、时长、冷却沿用现有日常调度。
- 复用 `AnimalAgent`、`AnimalSimulation` 及现有移动、感知、地形查询接口，不为新物种另建独立 Update/Tick 调度。
- 同时要在Animal Inspector的DebugUI中加上这个动物以及它相应的行为。

# AnimalInfo 结构

- `AnimalInfoLibrary` 是动物数据的统一入口；每个 `AnimalInfoEntry` 包含 `AnimalSpecies` 和 `photo`、`behaviour`、`info` 三个引用。
- 三个引用分别指向 `AnimalPhotoLibrary`（照片及显示名称）、`AnimalSpeciesConfig`（行为配置）、`AnimalInfo`（图鉴文字和插图）；它们不重复序列化物种枚举。
- 资产位于 `Assets/Data/Animals/`：父目录放 `AnimalInfoLibrary.asset`，`Photo/`、`Behaviour/`、`Info/` 分别存放三类资产。
- 游戏启动时由 List 建立 Dictionary，通过 `AnimalInfoLibrary.Active.BySpecies[species]` 获取条目；新增动物需在此注册完整引用。
- `AnimalInfoLibrary.TryChoosePhoto()` 委托普通辅助类 `AnimalPhotoSelector` 处理选图、裁剪和近期去重；`AnimalPhotoSubject` 的物种身份仍独立配置。

# AnimalUI 结构（Photos / Catalog）

- 界面代码位于 `AnimalPhoto/UI/`，其中 `Photos/` 和 `Catalog/` 分别组织照片界面与图鉴界面代码。
- `AnimalArchiveUI` 统一管理输入、页面切换及打开界面时的游戏暂停；仅保留上一个界面，B 返回一次后再次按 B 关闭；页面包含 Photos、照片全屏查看、Catalog 和物种详情。
- `Photos/PhotoCollection` 管理照片筛选、排序和选择；`PhotosView` 渲染照片，`PhotosSidebarView` 管理无滚动条的 collection 列表及越界滚动动画；Photos 页的手柄侧栏导航仅由 LT/RT 切换 collection，左摇杆和 D-pad 导航 Photo Index，右摇杆也可选择照片。
- Catalog 与物种详情共用六格可见侧栏；`CatalogSidebarView` 跳过未识别物种并动画滚动，摇杆上下或 LT/RT 选择，Main Catalog 清除选择并显示空白页；`CatalogView` 填充详情，底部识别条仅为静态占位。
- UXML、USS、Prefab 和 PanelSettings 位于 `Assets/Prefabs/Resources/UI/AnimalCatalog/`；自定义 VisualElement 负责网格、标题线、学名边框及照片尺寸等表现。
- 已保存照片来自 `SaveSystem.Current.photos`；`AnimalPhotoRecord` 保存原图标识、效果与裁剪参数，界面监听 `SaveSystem.Changed` 刷新；`PhotoDeleteHold` 管理长按 X 1.5 秒删除，`ArchiveHoldCircleElement` 显示环形进度，删除通过 `SaveSystem.DeletePhoto` 持久化。
