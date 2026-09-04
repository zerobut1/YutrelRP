# Unity 6.6 升级检查

检查日期：2026-09-04。目标工程：YutrelRP。

## 结论与修改

主工程已升级到 Unity `6000.6.0f1`（`f7f8ed4d1e24`），实际加载 SRP Core `17.6.0`。现有 C# 渲染路径未发现阻塞升级的 API 编译错误。

- 包 `package.json` 的最低 Unity 版本调整为 `6000.6`，Core 依赖调整为 `17.6.0`，同步 `packages-lock.json` 的 embedded 包依赖记录。
- 相机警告去重使用 `HashSet<EntityId>` 保存完整标识，避免把哈希值当成相机身份。RenderGraph 的 `executionId` 已直接使用 `camera.GetEntityId()`，无需迁移。
- 按项目要求移除 ShadowMask 的 Shader 调试符号指令，不保留旧版本条件分支。
- 更新包 README、CHANGELOG 和本机 AGENTS 中的编辑器路径。

Unity 6.6 已弃用若干整数对象标识 API，并更新了 Shader 调试指令。核对依据为 [Unity 6000.6.0f1 发布说明](https://unity.com/releases/editor/whats-new/6000.6.0f1)。

Sandbox 的升级由用户另行同步；本包当前不再以 Unity 6.5 为兼容目标。

## 已检查的设置与依赖

| 项目 | 当前状态 | 结论 |
| --- | --- | --- |
| 图形 API | Windows 手动指定 Direct3D 12 优先，Vulkan 后备 | 保留；已有 Unity 6.6 日志确认运行于 D3D12 |
| 色彩空间 | Linear | 保留 |
| 默认管线 | Graphics 指向 YutrelRP Asset；各 Quality 档未覆盖管线 | 保留 |
| 全局设置 | Graphics 的 Global Settings 映射仍指向 YutrelRPGlobalSettings | 保留 |
| DDGI | 使用现有加速结构和 ComputePass 接口 | C# 编译通过；画面与 GPU 行为仍需运行时验证 |
| Collections / Burst | 当前包解析结果为 Collections 6.6.0 / Burst 2.0.0 | Unity 6.6 内置包；无需为数字与依赖最低版本不同而手工改锁文件 |
| Terrain 模块 | 主工程仍显式依赖 terrain / terrainphysics | Core 移除自身 Terrain 依赖不影响本工程的声明 |

检查了当前使用的 RenderGraph、RTHandle、光追、Volume 和编辑器接口。未发现项目代码使用已弃用的 `DEVELOPMENT_BUILD` / `UNITY_64` 条件，也未使用改动了泛型约束的 `ConstantBuffer<T>`，无需针对这些变更改写代码。

本地 Graphics 参考仓库的 Core 仍为 `17.5.0`；本次 API 核对以 YutrelRP `Library/PackageCache` 实际安装的 `17.6.0` 为准。

## Player 调试设置

Unity 6.6 的 **Managed Code Variant** 与 **Development Build** 分开控制。默认 Release 会剔除 SRP 的部分诊断和性能采样内容。需要在 Player 中检查 RenderGraph 或保留诊断数据时，在对应平台的 Player Settings 中选择 **Checked**；需要源码单步调试时选择 **Debug**；只需要性能采样时可选 **Instrumented**。没有自动修改工程默认值。参见 [Unity 6.6 升级指南](https://docs.unity3d.com/6000.6/Documentation/Manual/UpgradeGuideUnity66.html)。

Unity 6.6 还提供 Build Profile 的 Graphics override / Shader Build Settings 中的 D3D12 FXC、DXC 选择。当前没有必须切换编译器的证据；如以后切换，应重新验证光栅 Shader、光追 Shader 与截帧结果。参见 [Unity 6.6 发布说明中的 Shader 编译器设置](https://unity.com/releases/editor/whats-new/6000.6.0f1)。

已有 `Logs/Editor-prev.log` 记录了相机场景没有启用 `YutrelEnvironmentLight`、因此跳过 SkyboxPass 的警告。对应代码主动检查场景环境光，Shader 资源引用仍存在；这条日志本身不说明 Unity 6.6 API 不兼容。需要天空盒的场景应配置并启用该组件。

## 验证范围

- `tools/agent_harness.py compile --timeout 60`：通过，0 警告、0 错误；覆盖包 Runtime、Editor、Editor Tests 和宿主 Editor 代码。
- `dotnet build YutrelRP.slnx --no-restore`：通过，0 警告、0 错误。首次运行因宿主生成工程缺少 `project.assets.json` 失败，执行一次 `dotnet restore YutrelRP.slnx` 后通过。
- `git diff --check`：通过。
- 检索包及 Assets 下的 Shader / HLSL / compute / raytrace 文件，未残留 Shader 调试符号 pragma。

本次未启动 Unity；测试程序集仅编译，未运行 EditMode 测试。C# 编译不覆盖 Shader 编译、Player 构建和 GPU 画面一致性，不能据此声称 RenderDoc 像素复刻验证通过。
