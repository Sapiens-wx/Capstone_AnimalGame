# Animal AI 使用说明

## 雪羊与狼

- 手动摆放 `Prefabs/Animals/MountainGoat/MountainGoat_Placeholder.prefab`、`MountainGoat_Juvenile_Placeholder.prefab` 或 `Prefabs/Animals/Wolf/Wolf_Placeholder.prefab`。沿用现有占位图形，不自动生成动物，也不修改场景。同群个体在 AnimalAgent 中填写相同的非空 Group Id；幼羊请与成年羊同组摆放。
- 物种参数位于 `Data/Animals/Behaviour/MountainGoatConfig.asset`、`WolfConfig.asset`。雪羊固定 10 血，最大可走坡度/滑落阈值 80°；狼为 38°，捕猎伤害 3，食谱为麝鼠和雪羊（含幼羊）。雪羊冲撞伤害暂配为 2，可在配置调整；打架与激怒共用此伤害、范围、击退和一秒延迟结算。
- 日常动作引用 `Data/Animals/Behaviour/SpeciesActions/` 的动作资产，权重、持续 X 秒、冷却在物种配置的 Daily Behaviours 中调整。动作资产提供速度等参数；通过日常条目创建时，以条目的时长为准。灌木进食仍使用原 EatPlantAction，乔木/灌木目标分别使用 TreeHabitat、Bush 类型 AnimalFoodSource，草地接受 Grass/Grassland/GrassTest，岩石为 Rock。
- 成年雪羊支持灌木/草地进食、观察、平坦岩石休息、岩石踱步、成对冲撞。幼体复用同一物种配置及战斗逻辑，仅缩小视觉并排除成年打架，可在 MountainGoatBehaviour 上指定幼体 Sprite。双方预期互撞不会触发普通惊扰；旁观者正常受伤并惊扰，外界威胁或死亡会取消双方会话。成员进入逃跑等状态时不会被取消操作拉回日常。
- 雪羊逃跑优先避开近距离威胁，再选可通行上坡；无上坡后反向下坡，保持方向越过谷底再开始上坡，规划间隔默认 0.7–0.9 秒。参数在 MountainGoatBehaviour 调整。所有移动继续使用现有局部避障，受阻时有限重选，不包含全局寻路；一直保持逃离，直至通用移除条件生效。
- 狼复用原日常调度、群体捕猎、逃跑、尸体预约和进食冷却。嗅探一个短段后寻找食谱内尸体，使用 CorpseOnly 捕猎策略靠近并进食，途中不会改追活猎物。惊扰时朝威胁慢退，所有激怒入口均被能力检查拦截。
- 嚎叫通过现有 AnimalSoundEmitter 的 Howling 声波配置表现；本次未新增角色动画或音频素材，后续可根据当前动作名绑定趴下、嗅探、撒尿及打架动画。
- 验证菜单：`Animal Game / Validation / Run Goat And Wolf AI Checks`。批处理入口 `AnimalGame.Editor.AnimalSpeciesRegressionChecks.RunBatch` 同时运行现有动物、空间索引和垃圾兼容检查。Brain.CurrentActionName 可查看雪羊逃跑子阶段与打架阶段。

本次实现对应项目 `docs/animal_ai.md`，并采用对话中补充的规则：接触时登记攻击、冷却一秒，一秒后扣血；滑落后恢复行为且不造成摔落伤害。局部导航优化仍为后续工作，麝鼠找水算法保持原样。

## 配置与接入

- 日常与逃离速度按物种区分，当前值、动作倍率与现实参考见 下方的「动物移动速度基准」。日常为基础速度，具体动作可叠加慢走倍率；啄木鸟树间飞行另在 `FlyToTree.asset` 配置，松鸡日常短飞复用逃离飞速。

- 现有麝鼠和啄木鸟继续引用原 `AnimalSpeciesConfig` 和物种行为组件，配置资产中的检测间隔等数值保留。`AnimalAgent` 在运行时补齐新模块，无需重新制作场景。
- 每个场景自动创建一个 `AnimalSimulation`，统一调度动物、群体事件和延迟伤害，使用 `Time.deltaTime` 累计的缩放时间。不要再单独调用各动物的 Tick。
- 新物种使用 `AnimalAgent` 和物种配置；未指定物种行为组件时，自动使用 `GenericAnimalBehaviour`。共享配置只保存参数，各个体持有自己的运行数据、生命、目标与计时。
- 在 `Daily Behaviours` 中添加 `Hunt` 并配置 `Prey Species`，才能由日常调度选择捕猎。原有两种动物没有因此自动变成肉食动物。
- 同一场景内 `AnimalAgent.groupId` 相同且非空的动物组成群体；可混合物种，第一名成员担任领队，离场或死亡后随机换领队。成员完成当前日常任务后再处理跟随。
- 日常条目可以直接引用 `AnimalActionDefinition`。通过 Assets/Create/Animal Game/Animals/Basic Action 创建待机、漫步、植物进食或捕猎资产。自定义行为继承 `AnimalAction`，实现 Definition 的 `CreateRuntime()`；每次必须返回独立运行对象。
- `AnimalDailyBehaviourSettings` 支持基础权重、冷却、四季修正及四个六小时时段修正。季节/时段读取现有 MainUI 的世界日历；没有世界日历时使用固定冬季中午。默认系数均为一。
- 身体半径等距离使用逻辑地图米。`Activity Radius` 现在是附近目标的搜索距离，普通移动不再限制在出生点周围；特殊回树锚点和旧找水范围继续保留。

## 行为与生命周期

- 普通逃离不自动解除；每次根据已发现威胁（包括玩家）取最大空扇区的角平分线，同样大小的空隙随机选一个。进入/失去范围时更新威胁列表。
- 受击立即进入惊扰响应，不跳过惊扰。群体首个逃离/激怒事件在 0.5 秒后使其他未自行反应的成员强制二选一；按各自概率归一化，双零概率时随机。触发者死亡不取消事件。
- 麝鼠 `DiveEscapeAction`、啄木鸟 `ReturnToTreeEscapeAction` 适配旧物种流程。潜水/回树隐藏以及暂时禁用后重新启用保留个体身份，不重置血量或冷却。
- 活体距玩家超过当前相机屏幕宽度所对应世界距离的三倍时销毁。普通遮挡不影响此规则。尸体不受距离移除规则影响。
- 初始血量在配置范围内均匀随机，最小值严格大于零且不超过血量上限；不自动回血。死亡切换为普通可推动尸体，不可抓取、扫描或拍照。
- 尸体自然降解速度为初始降解值 / 120 缩放秒，进食额外连续减少降解值；降到零就移除。可配置尸体精灵，缺省使用原身体变暗的表现。

## 捕猎与战斗默认规则

以下为用户授权由实现决定的默认值，可通过物种配置调整。

- 搜索半径 20 米，优先最近的合格猎物或可进食尸体；新目标至少近约 20% 才换目标，以减少来回切换。追逐半径以捕猎者当前位置为中心，默认 30 米。
- 从本轮捕猎开始计时，默认最多 180 秒，中途换目标不重置。找不到目标则结束本轮；无进展时放弃，短暂排除失败目标。再次调度失败行为前至少等待一秒。
- 按食谱选择目标，不捕猎同组成员。群体成员跟随最近的有合格目标成员，各自满足距离和攻击条件；群体可绕过个体捕猎冷却，但不能绕过进食冷却。
- 接触时登记伤害，一秒后扣血；攻击冷却按“攻击者—目标”独立计算。脱离接触仍会结算已登记伤害，目标销毁/死亡后不再重复生效。多个攻击者分别结算。
- 激怒冲撞对范围内的动物和玩家登记伤害，可伤及同伴；攻击者停下，对方受击退。每次冲撞进行一次 60% 解除判定；未解除时一秒后再次冲锋。
- 击退距离由伤害线性映射并 clamp；被动位移受实体阻挡，但允许进入危险地形。动物在陡坡上继续滑落，结束后恢复原行为，不增加摔落伤害。玩家受击扣除现有 UI 的完整度并使用自身移动链处理位移。
- 到达尸体旁后才开始自己的 20 秒进食。最多五只同时进食；尸体已满则寻找其他尸体。完整一轮的食用量按捕猎者攻击伤害决定，过程连续扣除。
- 中断或离开距离后结束本轮；再次进食从零开始。根据实际吃到的份额计算最多五分钟冷却；捕猎和进食分别保存截止时间。尸体耗尽时停止进食，不重复结算或扣成负数。

## 身体、飞行与照片

- 五个动物预制体的 `AnimalTurnDeformation` 控制头先转、身体和尾部依次跟随的视觉形变。默认 Follow Time 为 0.12 秒、Maximum Bend 为 22°；取消组件 Enabled 可恢复整体转向。头尾锚点使用 Visual Root 本地坐标，选中动物可看到青色辅助线；仅填写身体与描边渲染器。
- 转向材质保留玩家视野裁剪，未知雪花使用原效果，动物不显示方向箭头。网格副本按源 Sprite 共享，原图片和导入设置不变；死亡与禁用时恢复原 Sprite，传送时清空转角历史。渲染边界跟随形变供扫描和拍照使用，碰撞仍使用原圆形身体。
- `AnimalBody` 和 `PlayerWorldBody` 都属于 `WorldInteraction`，进入同一四叉树。活体阻挡身体和机械臂，尸体增加 `Pushable`。查询排除自身，正常移动、传送、击退、翻滚和出生定位后显式更新索引。
- 新增 `WorldInteractionKind.Actor` 能力位供飞行查询：飞行忽略地面物体，保留动物/玩家/尸体间的身体分离。落点被角色占据时保持受阻，由原物种超时/回退逻辑处理，不穿过角色。
- 照片状态自动读取快照；逃离、惊扰、激怒、隐藏等顶层状态优先。日常内再映射 `Moving`、`Eating`、`Resting`。手工指定照片状态字段已移除，旧枚举值保留，`Dead` 追加在末尾。

## 验证

## 新增五种动物

- Prefab 位于 `Prefabs/Animals/` 下的 `AmericanBison`（美洲野牛）、`Pronghorn`（叉角羚）、`PrairieGrouse`（草原松鸡）、`AmericanMarmot`（美洲旱獭）、`NorthAmericanPorcupine`（北美豪猪）目录。前两者另有 `Juvenile` Prefab；沿用现有占位美术，不自动刷新动物。
- 手动摆放群体时，在 `AnimalAgent` 填写相同且非空的 Group Id，幼体与成年放在同组。幼体不能打架或激怒，只会逃跑。野牛、叉角羚与雪羊的日常打架均不扣血、不击退、不影响第三方。
- 配置位于 `Data/Animals/Behaviour/`，动作资产位于 `Data/Animals/Behaviour/SpeciesActions/`。时长、权重和冷却从物种的 Daily Behaviours 调整；新动物及其动作自动接入 Animal Inspector 的动物列表和 Daily 菜单。
- 旱獭初始化时在出生点实例化 `Prefabs/Environment/Vegetation/MarmotBurrow.prefab`；洞穴没有贴图或碰撞。日常活动围绕自己的洞穴，逃跑可借用其他洞穴；洞口 Alert Radius 内连续 5 秒无威胁后出洞，期间保留身份和血量。
- 松鸡继承 `GenericAnimalBehaviour`，日常短飞 3–6 米并选择可站立落点；逃跑持续飞行。飞行可拍照，地面近战不能命中。
- 豪猪使用健康或枯死的 `TreeHabitat`；步行到树旁后直接切换到树上状态，无爬树动画。树上不激怒，现有地面捕猎者停止捕猎；地面激怒时背对威胁并竖刺，被捕猎者实际伤害时反伤 3 点，威胁离开 Alert Radius 后恢复日常。
- 旱獭警报及豪猪进食、啃树皮分别对应 `AnimalSoundEmitter` 的 Alarm / Eating / Bark Eating Event。未提供新音频素材，事件引用可自行绑定；已有独立声波表现。豪猪竖刺使用现有指示器贴图组成的占位表现，可通过 Quill Visual 替换。
- 重建资源：`Animal Game / Animals / Build Specific Animal Prototypes`。此菜单会覆盖这五种动物的配置与 Prefab，正常打开编辑器不会自动重建。
- 专项检查：`Animal Game / Validation / Run Specific Animal AI Checks`。批处理入口 `SpecificAnimalRegressionChecks.RunBatch`；实际运行及暂停恢复检查入口 `SpecificAnimalRegressionChecks.RunPlayModeBatch`。

## 通用验证

Unity 菜单：Animal Game / Validation / Run Animal AI Checks。

覆盖个体数据隔离、尸体连续降解、身体注册和移动后的四叉树更新、自身排除、延迟伤害及独立冷却、隐藏身份与自动照片状态、群体传播与领队替换、进食容量与比例冷却、最大空扇区、行为取消、暂停、飞行与玩家碰撞、两物种适配、视野移除条件，并运行现有空间索引与垃圾推动兼容性检查。

## 动物移动速度基准

2026-11-14：按现实物种的移动能力区分日常与逃离速度。以下是游戏设计值，不是每个个体在所有地形上都能维持的实测速度；不新增加速、体力或按地形变速机制。

| 物种 | 日常基础速度（m/s） | 逃离速度（m/s） | 使用方式 |
| --- | ---: | ---: | --- |
| 叉角羚 | 1.8 | 23 | 日常步行，受惊后快速奔跑 |
| 狼 | 2.2 | 16 | 日常小跑；现有捕猎逻辑在追逐逃跑猎物时也使用逃离速度 |
| 美洲野牛 | 1.5 | 15 | 日常缓行，受惊时短距离疾跑 |
| 雪羊 | 1.1 | 6 | 日常谨慎移动，逃离仍沿用现有山地选路 |
| 美洲旱獭 / Groundhog | 0.55 | 4 | 日常觅食，受惊奔向洞穴 |
| 北美豪猪 | 0.3 | 0.9 | 保留缓慢移动、依赖防御而非高速逃离的特征 |
| 麝鼠 | 0.65 | 1.5 | 以游泳速度量级抽象；现有配置共用于地面和水中移动 |
| 大草原松鸡 | 0.75 | 12.5 | 日常地面活动；逃离速度用于飞行，现有日常短飞也复用此速度 |
| 北美黑啄木鸟 / Pileated Woodpecker | 9.5 | 12 | 树间飞行；FlyToTree 动作基础速度同步为 9.5，沿用 ±10% 波动；树上爬行保持原值 |

配置入口为 `Assets/Data/Animals/Behaviour/*Config.asset` 的 `dailyMoveSpeedMetersPerSecond` 与 `fleeSpeedMetersPerSecond`；啄木鸟的独立飞行动作为 `SpeciesActions/FlyToTree.asset`。

日常慢走、嗅探等动作继续使用自己的速度倍率（例如叉角羚漫步 ×0.45）；上表是基础值。成年和幼体继续共享各自物种配置。冲撞速度属于独立的战斗配置，不随本轮修改。

### 现实参考与抽象

- [NPS：叉角羚](https://www.nps.gov/articles/000/pronghorn.htm)：约 55 mph（24.6 m/s）的高速奔跑；23 m/s 落在本次指定的 20–25 m/s 区间。
- [NPS：狼](https://home.nps.gov/articles/life-of-a-wolf.htm)：常以约 5 mph（2.2 m/s）小跑，短距离可达更高速度；采用 16 m/s 作为游戏逃离速度。
- [Utah State Parks：野牛](https://stateparks.utah.gov/parks/antelope-island/park-mammals/)：最高约 35 mph（15.6 m/s）；游戏采用 15 m/s。
- [NPS：Mountain Goat Action Plan，Appendices](https://parkplanning.nps.gov/showFile.cfm?projectID=49246&sfid=324118)：山地短时速度约 10–15 mph（4.5–6.7 m/s）；游戏采用 6 m/s。
- [Allegheny County：Groundhogs](https://www.alleghenycounty.us/files/assets/county/v/1/parks/north-park/latodami/documents/resources/groundhogs.pdf)：短距离可跑约 10 mph（4.5 m/s）；游戏采用 4 m/s。
- [Pennsylvania Game Commission：豪猪](https://www.pa.gov/agencies/pgc/wildlife/discover-pa-wildlife/porcupine)：短距离最高约 2 mph（0.9 m/s）。
- [Connecticut DEEP：麝鼠](https://portal.ct.gov/deep/wildlife/fact-sheets/muskrat)：游速最高约 3 mph（1.3 m/s）；为逃离表现略提高到 1.5 m/s。
- [Texas A&M：prairie-chicken flight research](https://oaktrust.library.tamu.edu/bitstreams/0fb66f6c-f5b7-4cb8-b4c6-1b9b4a4d6cf5/download)：大草原松鸡飞速均值约 45–46 km/h（12.5–12.8 m/s）；采用 12.5 m/s。
- [Scientific Reports：Periodic and transient motions of large woodpeckers](https://www.nature.com/articles/s41598-017-13035-6)：北美黑啄木鸟参考平均飞速 9.55 m/s；日常取整为 9.5 m/s，受惊飞行采用游戏设计值 12 m/s。

除狼的小跑和啄木鸟的巡航外，日常速度主要是便于观察和拍摄的缓行设计值，不以物种短时最高速度等比例计算。

## 动物警觉范围基准

`Alert Radius Meters` 是开始进行玩家发现判定及感知附近捕猎者的半径，单位为逻辑地图米。进入范围后仍按概率检测，并非进入半径就立即逃跑。自然发现玩家及捕猎者的概率按 `1 - 距离 / 警觉半径` 线性衰减：边缘为 0，中心为原有近距离上限；基础概率、近距离倍率、移动速度加成、直视加成及检查间隔沿用各物种配置。受击、拍照反应及群体事件继续走原有直接触发规则。

| 物种 | 原半径（m） | 当前半径（m） |
| --- | ---: | ---: |
| 叉角羚 | 10 | 27 |
| 狼 | 14 | 25 |
| 美洲野牛 | 10 | 20 |
| 雪羊 | 14 | 22 |
| 北美黑啄木鸟 | 18 | 25 |
| 大草原松鸡 | 10 | 20 |
| 美洲旱獭 | 10 | 14 |
| 麝鼠 | 14 | 16 |
| 北美豪猪 | 10 | 12 |

这是按物种差异设定的游戏半径，不是实测感官极限，也不是现实野生动物安全距离。大范围给予开阔地警戒、追猎及鸟类提前反应；小型动物保留近距离观察空间。[WDFW 的雪羊资料](https://wdfw.wa.gov/species-habitats/species/oreamnos-americanus)也指出对人的警惕会受习惯化影响，因此不把警戒距离视为固定的生物学常数。

现有依赖同一半径的威胁记忆、旱獭出洞安全检查和隐藏动物安全返回距离会随配置一起扩大；活动搜索范围、追逐范围和检测概率保持原配置。
