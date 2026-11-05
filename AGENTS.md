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