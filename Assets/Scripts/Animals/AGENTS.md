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
- 拍照立即自动保存；结果页 X 打开 Catalog、Y 打开 Photos，相册覆盖结果页，关闭后回到结果页。
- `AnimalArchiveUI` 统一管理输入、页面切换及打开界面时的游戏暂停；仅保留上一个界面，B 返回一次后再次按 B 关闭；页面包含 Photos、照片全屏查看、Catalog 和物种详情。
- `Photos/PhotoCollection` 管理照片筛选、排序和选择；`PhotosView` 渲染照片，`PhotosSidebarView` 复用 `CatalogSidebarView` 的六格列表、黄色选框与滚动动画；All Collection 置顶，其余按英文名称 A–Z 排序。Photos 顶部共用 Catalog 翻页栏样式，LT/RT 显示相邻集合并首尾循环；左摇杆和 D-pad 导航 Photo Index，右摇杆也可选择照片。
- 主 Catalog 使用 `MainCatalogView`（位于 `CatalogView.cs`）显示物种进度树及四列、两行可见网格，左摇杆 / D-pad 导航、A 进入详情；越界目标行移至顶部。物种详情保留 `CatalogSidebarView` 六格侧栏及 LT/RT 切换，RB 返回主 Catalog、Y 查看该物种照片；`CatalogView` 填充详情。拍到物种即解锁一级并在图鉴可见；网格预览由 `AnimalInfo.CatalogPreview` 配置，三级圆标和百分比读取 `SpeciesProgress`。
- UXML、USS、Prefab 和 PanelSettings 位于 `Assets/Prefabs/Resources/UI/AnimalCatalog/`；自定义 VisualElement 负责网格、标题线、学名边框及照片尺寸等表现。
- Photos 和 Catalog 共用外框、标签栏、侧栏、内容边框及底栏尺寸；公共布局直接定义在共享样式中，不按页面切换尺寸。
- 两页的 A/B/X/Y 和方向箭头为黑底白字、黄色圆环（42 像素直径、4 像素描边），LT/RT/LB/RB 保留白色矩形边框和黄色文字；`ArchiveButtonFeedback` 复用结算页的挤压回弹和涟漪，0.05 秒后执行操作、动画独立持续 0.2 秒，鼠标/键盘/手柄走同一入口，关闭时取消待执行动作。照片操作下划线与按键圆环底部对齐；长按删除按下仅缩放，删除成功后才播放涟漪。
- Photos 列表与大图页共用右侧信息区和翻页栏定位；左侧区域宽度等于页面内容高度，分别显示 Photo Index 或正方形大图，列表页不重复显示小预览图。
- 大图页切换照片时，`PhotosView` 保留上一张照片源图，0.32 秒内左右滑出/滑入；滑出照片随进度向移动方向的反侧后倾至 32°，左右镜像。`photo-frame` 裁切越界内容，动画使用非缩放时间，结束或关闭时释放旧图。
- 大图页与照片结算页共用 `PhotoIdleTilt`，7 秒循环转动；结算页两轴幅度为 4.5° / 3°，大图页倍率 1.5（6.75° / 4.5°）。右摇杆超过死区时手动控制，松开后平滑恢复自动转动，换图或重新进入时重置相位。
- Catalog 网格与物种侧栏只排列已解锁动物，不补空格，按英文显示名称 A–Z 排序；物种详情 LT/RT 在首尾循环。
- Catalog 物种页与 Photos 侧栏列表上下各有 LT Previous Species / RT Next Species，复用同一切换逻辑；LT 顶边位于原列表顶边，列表下移 42 像素，保持六格条目高度。
- Main Catalog 与 Photo Index 共用 `ArchiveColumnWave`：卡片按列以 4.8 秒周期、±4 像素浮动，各列错开相位；只改变视觉位移，离开页面或关闭时停止调度。
- 标签选中黄线位于内容背景上方，中心叠加与标签边框同色同宽的横线，避免跨越标签底边时被内容背景遮挡。
- Species 页的 `CatalogRulerElement` 按屏幕高度与统一的美洲野牛参考比例（1445:965）绘制所有物种标尺，不依赖是否配置插图；读取 `AnimalInfo.ShoulderHeight/TotalLength/BodyMass`。
- Species 页详情使用普通容器；学名、Physical Characteristics 和 Distribution 顶部对齐，LT/RT 条贴齐页面右上角，其左端距动物名称 Label 右缘为屏幕宽度的 3%。
- Description 自然排在 Classification 与 Physical Characteristics 两栏下方；Distribution 等区块使用独立右栏。
- Conservation Status 下方的 `CatalogConservationMeter` 绘制五档白色刻度；二级解锁后，按 `AnimalInfo.ConservationStatus` 显示黄色指针，从 Least Concern 到 Critically Endangered。
- 已保存照片来自 `SaveSystem.Current.photos`；`AnimalPhotoRecord` 保存原图标识、效果与裁剪参数，界面监听 `SaveSystem.Changed` 刷新；`PhotoDeleteHold` 管理长按 X 1.5 秒删除，`ArchiveHoldCircleElement` 显示环形进度，删除通过 `SaveSystem.DeletePhoto` 持久化。

- `SaveSystem` 统一读写 `Application.persistentDataPath/animal-save.bin`，缺失时创建空存档；`GameSave` 是普通数据类，二进制格式包含签名、版本、照片及物种进度，Editor 和独立 Player 使用同一逻辑。
- `StorePhoto` 拍照保存时立即增加认知度（每张照片 1/5 点），首次大于 0、达到 30/100 时升级。结算快照 `UnlockAdvanced` 仅决定 Catalog 后黄色星号；星号随文字打印，使用 Cargo 的 Orbitron-Medium 47 号字体，复用 InventoryController 的透明度闪烁（50%–100%，共用 MainUI 的周期配置），不缩放。Y/X/B 按下立即触发独立涟漪和按键快速缩小回弹，0.05 秒后执行对应页面操作，与解锁度无关。涟漪半径上限 108、快速淡出，位于照片上层、文字下层。存档 v3 迁移 v1/v2 时仅补计未上传照片，不重复计分。

# Earthline 通信

- `AnimalPhoto/Communication/` 管理联系人、对话、方向键焦点与 4 列 × 3 行选照片窗口；L 开关，A 确认，Y 分享/全选，X 发送，B 返回。
- Prefab/UXML/USS 位于 `Assets/Prefabs/Resources/UI/Communication/`；`Assets/Data/Communication/Contacts.asset` 配置联系人枚举、职业和 Sprite 头像。
- `GameSave.conversations` 保存各联系人的文字/照片消息；照片引用相册索引，删除相册照片时同步移除和调整消息索引。
- `SaveSystem.SharePhotos` 持久化消息、全局 sent 标记和已发送数量；上传不再增加认知度或解锁等级。
