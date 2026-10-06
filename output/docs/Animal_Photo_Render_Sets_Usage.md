# 动物照片相机与渲染 Set 使用说明

动物原图继续由 AnimalPhotoLibrary 管理；相机型号引用一个 PhotoRenderSet，照片在按下快门时保存这套 Set 的参数副本。调整共享 Set 会影响之后拍摄的照片，不会改变本次运行中已经保存的照片参数。现有照片结果界面和相册继续使用这份快照。

## 在游戏中使用

- 默认机器人预制体 `Assets/Prefabs/Resources/Robot/RobotMarker.prefab` 的 PhotoModeController 使用 Instant Camera，绑定 Instant Film Set。
- 在 PhotoModeController 的 **Current Camera** 字段选择其他型号。留空使用 Basic 后备处理。
- `Assets/Data/AnimalPhoto/Rendering` 内提供 Basic Camera、Instant Camera 以及各自的 Set。
- 相机型号是逻辑拍摄设备，与负责世界画面跟随的 Unity Camera 分开。

当前项目内的 InstantFilmSet 已调为增强版（版本 2）：完整色调强度、1.28 对比度、0.55 高光压缩、0.13 暖高光、0.022 颗粒、0.65 EV 暗角与 0.18 柔化。新建 Instant Film Set 的工厂参数仍较温和；要以增强版为起点，请复制现有 InstantFilmSet。

设备系统可以调用以下接口；切换只影响下一次按下快门：

```csharp
photoModeController.SetCamera(cameraDefinition);
```

不要在照片展示或相册查看时重新读取 Current Camera。`AnimalPhoto.CaptureSettings` 保存型号 ID、名称和不可变的 `PhotoRenderSnapshot`；快照包含 Set ID、版本、实际参数及固定 seed。

## 调整风格

打开 **Animal Game > Animals > Animal Photo Library**，选择物种照片库和一张原图。在渲染工作区选择相机或 Set，保持同一裁切和颗粒种子，对比原图与不同 Set。编辑区修改的是共享 Set 资产，同型号的所有动物照片会使用它。

先确认最终裁切内动物轮廓、羽毛和毛发清楚，再调整色调、颜色，最后微调颗粒和暗角。用麝鼠棕色毛发、啄木鸟红冠和黑白羽毛共同检查效果，避免只为一张照片调出适用范围很窄的滤镜。

| 参数组 | 作用与建议 |
| --- | --- |
| Strength | 全局混合强度，0 为原图，1 为完整效果；不添加白边。 |
| Tone | 曝光以 EV 为单位；对比度、中间调、黑位和高光压缩形成温和的相纸层次。黑位抬升应少量使用。 |
| Color | 饱和度、暖高光、冷阴影；Color Protection 减少对原有高饱和颜色的偏色，不是动物识别蒙版。 |
| Grain | 强度、尺寸、彩色颗粒比例。种子固定，查看同一照片时颗粒不会闪动；尺寸按照片长边 1024 的参考空间定义。 |
| Vignette | 宽缓的边缘曝光衰减，单位 EV。保持弱暗角，避免遮住动物。 |
| Optical Softness | 可选的额外柔化 Pass。当前增强版使用 0.18，减少数码锐利感并保留动物细节；新建 Set 的工厂参数关闭此项。 |

Basic Set 延续旧流程：照片条目的 Saturation 与 Basic Set 饱和度相乘，再限制到支持范围。Instant Film Set 使用自身的颜色参数，不叠加条目饱和度。所有原图仍应按彩色纹理导入，**sRGB Color Texture** 保持开启。

## 新建其他相机

1. 新的参数组合：在照片工作区复制现有 Set，重新命名并调整。窗口的复制操作会生成新的稳定 ID。
2. 新建 Camera Model，填写显示名称，在 Render Set 字段绑定所需 Set。多个型号可以有意共享同一个 Set。
3. 在机器人预制体或运行时切换 Current Camera 验证拍摄效果。

也可以通过 **Assets > Create > Animal Game > Photos** 创建 Camera Model、Basic 和 Instant Film Set。直接在 Project 中用 Ctrl+D 复制资产会保留序列化 ID；需要独立身份时使用工作区提供的复制功能。

## 扩展新的渲染方式

新的风格族可以继承 `PhotoRenderSet`，实现 `Capture(int seed)`，返回自己的 `PhotoRenderSnapshot`。快照必须复制全部可编辑数据，不能在 Render 时回读 ScriptableObject。含 AnimationCurve、数组或列表的未来配置需要深拷贝，不能只复制引用。

工作区的 Create Set 菜单自动发现可实例化的 PhotoRenderSet 子类，不需要为新风格修改菜单代码。

快照的 `Render(source, crop, legacySaturation, maximumSize)` 返回由调用者持有的 RenderTexture。不同快照可以使用不同 Shader、多 Pass、LUT 或其他渲染方法；现有照片选择和结果 UI 不需要添加风格枚举或分支。多 Pass 必须明确传递中间纹理，避免对同一纹理原地 Blit；结束时恢复 RenderTexture.active 和 GL.sRGBWrite，并释放临时资源。

当前 Basic 与 Instant Film Shader 放在 `Assets/Shaders/Resources`，用于运行时加载及构建收录。新增风格的 Shader 也需要可靠的构建引用。

## 边界

- 这是原图的颜色与质感处理，保留原有构图、动物形态及内容。它不会复现此前 AI 图片生成可能改变的毛发、光线或局部结构。
- 相册目前在内存中保存，退出运行后不持久化。快照防止参数资产修改影响旧照片，但替换源图片或升级 Shader 实现仍可能改变重新渲染的结果。跨版本永久保存需要后续存储最终图片或保留版本化源素材与渲染器。
- 默认参数是可调的起点，最终艺术强度应在游戏照片卡片实际显示尺寸下确认。
- 编辑器展示处理后的照片本身；游戏照片卡片仍会施加现有的倾斜与轻微明暗效果。

## 验证

在 Edit Mode 执行 **Animal Game > Photos > Validate Rendering**。验证工具使用临时测试对象，检查基础兼容、颗粒确定性、零强度原图、Alpha、比例、快门参数冻结和默认预制体绑定，同时导出真实照片对比及 Shader 检查结果。

报告位于 `output/photo-render-validation/report.json`，对比页位于同目录的 `index.html`。这些渲染检查不代替最终游戏内的美术验收，也不构成 GPU 性能基准。
