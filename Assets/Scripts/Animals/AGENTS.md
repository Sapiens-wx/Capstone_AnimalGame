# 动物 AI 实现约定

- 以下脚本路径均相对于 `Scripts/Animals/`。
- `Behaviours/Species/`：物种专属的 `XxxBehaviour`，只实现该物种的差异逻辑。
- `Behaviours/Common/`：通用 Behaviour、可复用 Action 和 `XxxActionDefinition`；鸟类通用逻辑也放在这里。
- `Behaviours/Actions/`：独立的专用 Action 实现（如打斗、坡地逃跑）。
- `Behaviours/`：共享状态机、日常调度与行为基类；`Core/`：个体运行数据、移动、感知、生命及统一模拟；`Configuration/`：配置类型；`World/`：场景表现与声音。
- 新物种必须继承适用的通用 AI：鸟类优先继承鸟类通用 Behaviour（当前为树栖鸟类的 `TreeBirdBehaviour`）；其他动物直接继承 `GenericAnimalBehaviour`。不要直接继承 `AnimalBehaviourSet` 重写整套流程；可复用的类别逻辑提取到通用 Behaviour。
- 动作执行类继承 `AnimalAction`，负责执行与生命周期；动作配置类继承 `AnimalActionDefinition`（树栖鸟类用 `TreeBirdActionDefinition`），通过 `CreateRuntime()` 创建独立动作实例。
- ScriptableObject 只保存共享配置；目标、计时器等运行状态保存在个体或动作实例中，不能写入共享资产。
- 物种配置资产放在 `Data/Animals/`，动作配置资产放在 `Data/Animals/SpeciesActions/`，通过 `AnimalSpeciesConfig.DailyBehaviours` 引用；权重、时长、冷却沿用现有日常调度。
- 复用 `AnimalAgent`、`AnimalSimulation` 及现有移动、感知、地形查询接口，不为新物种另建独立 Update/Tick 调度。
- 同时要在Animal Inspector的DebugUI中加上这个动物以及它相应的行为。
