# Forward 相机表面纹理

Deferred Renderer 为不透明与透明 Forward 提供 Base 完成后的表面数据。
资源协议不区分材质类型；材质决定采样位置、解码和资源不可用时的行为。

| 全局输入 | 内容 | 可用范围 |
|---|---|---|
| `_CameraNormalsTexture` | GBuffer_B 原生世界法线，XYZ UNorm，解码 `xyz * 2 - 1` | 两个 Forward 阶段 |
| `_CameraDepthTexture` | Base 结束时的 device depth，R32_SFloat | 复制成功时有效 |
| `_CameraDepthTextureAvailable` | 1 表示有效快照，0 表示未生成 | 每个相机、两个阶段都重新绑定 |

`YutrelDeferredRendererSettings.copyDepthForForward` 默认 false。需要采样深度的项目
在自己的 DeferredRenderer 资产上开启；关闭时不记录复制 Pass，也不分配深度快照。
法线直接复用 Base 的 GBuffer_B，不额外复制。清屏区域的法线不代表有效表面。

开启后，在 Base 后记录通用 `DepthCopyPass`。它使用 RasterPass 和
`Hidden/YutrelRP/Depth Copy`，将每个深度 texel 以 `Load` 写入 R32_SFloat，
不做线性化、滤波、Y 翻转或 stencil 复制。当前目标为管线的非 MSAA、二维深度附件。
深度值沿用当前平台的 reversed-Z 约定，材质按相机深度参数自行转换。

不透明和透明 Forward 显式声明并使用同一快照。快照不包含 Forward 或透明阶段
后来写入的深度，因此不会与正在写入的深度附件形成采样反馈。
法线同样保持 Base 内容，不代表 Forward 后的可见表面。

关闭配置或复制 Shader 不可用时，绑定远深度占位纹理（reversed-Z 为黑，否则为白），
并将 `_CameraDepthTextureAvailable` 设为 0。占位图不是相机分辨率的深度图；
材质必须先检查标记，再使用像素坐标 Load。资源缺失沿用运行时资源工具的错误日志。

复制 Shader 通过 `YutrelRPRuntimeShaders.depth_copy` 引用；应保留 GlobalSettings
的序列化引用，供运行时创建材质和构建资源收集使用。

验证包括离线 C# 编译，以及从实际 Shader 抽取 VS/PS 后，使用安装的 SRP Core
Common 和平台 API 编译、执行 SPIR-V 校验。Unity RenderGraph 执行及画面仍需运行时验收。
