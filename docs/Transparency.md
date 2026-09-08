# 通用透明阶段

2026-09-08。`YutrelDeferredRenderer` 在不透明 Forward、Skybox 之后记录
`TransparentPass`，结果随后进入统一后处理。没有新增管线开关或 LightMode。

## Shader 接口

- `RenderPipeline = YutrelPipeline`，`LightMode = YutrelForwardOnly`。
- 材质 renderQueue 位于 `RenderQueueRange.transparent`；列表使用 `CommonTransparent`。
- Scene Color 与 Scene Depth 均按 `AccessFlags.ReadWrite` 绑定，保留前面阶段的结果。
- Shader 自己指定 Blend、ZTest、ZWrite、Cull、Stencil。管线不设置 RenderStateBlock，
  因而也支持特殊的透明深度写入材质。
- 队列用于材质阶段依赖；同队列由 Unity 的透明排序处理。不同 SortingLayer/Order
  或交叉透明几何仍可能影响结果，这不是逐三角形排序或 OIT。

## 每相机资源

`ForwardPassBindings` 在记录时保存当前相机的资源和参数；不透明与透明阶段各自
记录一个参数准备 Compute Pass，再记录 Raster RendererList Pass。准备 Pass 使用
`AllowGlobalStateModification(true)`；绘制 Pass 显式声明所有读资源。
每次绑定光源计数、当前相机方向光 buffer、ShadowMask、SSAO、场景曝光及逆曝光，
不依赖前一个绘制阶段偶然遗留的全局量。准备 Pass 只设置参数，不派发计算 Shader。

Endfield 的环境光、输入尺度和曝光兼容参数集中在 `EndfieldShaderGlobals`，
通用 TransparentPass 不包含角色、EID 或 stencil 分类分支。

无方向光或 ShadowMask 无效时显式绑定白图；关闭阴影时沿用 ShadowMaskPass 的白图回退。
无方向光仍绑定当前相机创建的 buffer，并将计数设为 0。透明阶段始终绑定白色 SSAO，
同时将 `_EndfieldUseScreenSpaceAO` 设为 0；不透明阶段维持原来的 SSAO 开关。

透明受光 Shader 可以通过 `ShaderLibrary/ForwardLighting.hlsl` 查询自身表面的 CSM：

```hlsl
float visibility = EvaluateDirectionalShadowVisibility(lightIndex, positionWS, geometricNormalWS);
```

输入是世界坐标和归一化世界空间几何法线，返回 0～1（1 为全受光），已包含灯光阴影强度。
接口按自身位置计算视空间深度和级联，不读取 Scene Depth、GBuffer 或不透明 ShadowMask。
仅支持当前的一盏带阴影方向光；越界光索引、无阴影灯、无级联、超出距离/级联返回 1。
查询光色与方向但不读取 ShadowMask 时使用 `GetUnshadowedDirectionalLight`，调用方先检查光源数量。

透明材质需要声明以下片元变体；过滤由管线和主光设置统一决定，不是材质属性：

```hlsl
#pragma multi_compile_fragment _DIRECTIONAL_SHADOW_FILTER_NONE _DIRECTIONAL_SHADOW_FILTER_LOW _DIRECTIONAL_SHADOW_FILTER_MEDIUM _DIRECTIONAL_SHADOW_FILTER_HIGH
```

`DirectionalShadowBindings` 保存当前相机 atlas、矩阵/级联 Buffer、过滤、距离衰减和 texel size。
透明 preparation 设置全部全局参数与互斥过滤关键字，绘制 Pass 显式声明相同资源。
无阴影时绑定 defaultShadowTexture、当前 SetupLightPass 初始化的 Buffer，并设级联数 0；
不会读上一相机的矩阵。ShadowMask 使用同一参数快照和共享采样算法，过滤关键字使用 local
变体，避免上一相机透明阶段的 global 关键字覆盖它。

不透明表面继续使用屏幕 ShadowMask。透明阶段仍保留兼容用 ShadowMask 绑定，但当前
Endfield 透明头发/衣服不读取它。Unlit 不调用受光接口。此接口可供后续 RT 实现复用，当前只实现 CSM。

Unity 6000.6 的 `RenderGraphParameters.rendererListCulling` 已废弃，空列表专用裁剪
自 6000.5 起不再支持。实现交由 RenderGraph 正常调度/资源裁剪，不启用废弃开关或
同步查询 RendererList；空列表无 draw，但不能保证连同参数准备 Pass 一起消失。

## 透明 Unlit 入口

`Shaders/UnlitTransparent.shader`（菜单 `YutrelRP/Unlit Transparent`）提供纹理、HDR 颜色、
独立 RGB/Alpha 混合因子、深度测试/写入和 Cull 配置。
默认 RGB 混合 `SrcAlpha / OneMinusSrcAlpha`、Alpha `One / OneMinusSrcAlpha`，
深度 `LEqual / Off`、Cull Back。RGB 经过 `ApplyPreExposure` 写入 Scene Color，alpha 不曝光。
普通 Alpha 用默认值；加法示例将 RGB Destination Blend 改为 One，按需要选择 Source Blend。

这是一般透明 Shader 的入口示例，未增加 DefaultLit/OpenPBR 透明着色、折射、OIT 或描边。
透明 Shader 若需采样新全局纹理，必须扩展本阶段的显式资源依赖，不能偷读旧全局绑定。

## 验证边界

本轮 package runtime/editor/tests/host editor 的 `agent_harness.py compile` 通过。
Sandbox 离线 DXC/SPIR-V 检查包含 UnlitTransparent 的 VS/PS。
共享 CSM 修改已运行 shader-format；Sandbox 的 224 个 DXC/SPIR-V 阶段通过，
包含真实 ShadowMask/CSM 和四档过滤，以及 16 个捕获 Forward 输入签名。
优化后资源断言确认透明头发/衣服使用 atlas 且不读取 ShadowMask/SceneDepth/GBuffer，
不透明头发/衣服保留 ShadowMask。提取前后 CSM helpers 忽略空白后的代码一致。
没有启动 Unity。运行时还需检查透明排序/遮挡、天空盒与后处理位置、无光源/无阴影及多相机资源回退。
