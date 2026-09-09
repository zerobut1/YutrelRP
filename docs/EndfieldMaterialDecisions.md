# Endfield 材质的 YutrelRP 输入决策

更新：2026-09-08。复刻 Endfield 的材质算法，场景光照输入采用 YutrelRP。
本文描述当前适配约定；历史捕获报告中的原游戏输入不代表 Unity 侧应恢复的设置。

## 平行光与曝光（已实现）

使用第 0 盏方向光的方向、颜色与 illuminance（lux）。Lighting Reference Scale
是固定参考曝光 **EV100=14、Exposure Compensation=0** 下，100000 lux 对应的
Endfield Core 主光强度，默认值为 `1.6243868`。参考曝光是固定校准点，不跟随相机或未来默认设置变化。

```text
S     = lightingReferenceScale
P     = 当前相机 _PreExposure = 2^-(FixedEV100 - ExposureCompensation) / 1.2
P_ref = 2^-14 / 1.2
_EndfieldYutrelInputScale = S / (100000 * P_ref)
mainLight.radiance = light.color * (lux / 100000) * S * (P / P_ref)
```

EV 与补偿仍由管线原有范围限制。C# 设置固定输入标尺，Shader 恰好乘一次当前
`_PreExposure`；不再用当前曝光作分母抵消它。`chromaticity` 仍仅为光色。

| 条件（白光 100000 lux） | 进入 Core 的 radiance（每通道） |
|---|---:|
| EV100=14，补偿=0 | S |
| EV100=13，补偿=0 | 2S |
| EV100=15，补偿=0 | S/2 |
| EV100=14，补偿=+1 | 2S |
| EV100=14，补偿=0，标尺加倍 | 2S |
| 无方向光 | 0 |

只保证参考曝光下与旧映射一致，其他曝光下有意改变亮度。光照输入经过 Core 的非线性
材质计算和后处理，最终屏幕像素不保证等比变化。不要在输出上再乘一次相机曝光，
也不要用修改场景灯光或 Volume 值来抵消此修正。

Endfield Settings 的 **Scene Pre-Exposure** 是独立输出系数，默认 1：
`output = float4(result.color * endfieldScenePreExposure, result.alpha)`。
它影响最终 RGB，不能代替主光输入曝光；Alpha 不参与亮度缩放。

实现入口：`ResolvedEndfieldSettings.GetYutrelInputScale`、`EndfieldShaderGlobals`、
`ForwardPassBindings`，以及 Sandbox 的 `Assets/Endfield/Shaders/YutrelRP/YutrelInputs.hlsl`。
不透明和透明受光入口共用主光装配，因此此修正同时作用于两者。

## 其他明确决策

| 项目 | 当前约定与状态 |
|---|---|
| 环境漫反射 | 明确使用 Endfield Volume 的固定 Ambient Color / Intensity，直接进入 Core 的 hueTint / scale；不接 SH、DDGI 或捕获 Ambient Volume。不乘 Lighting Reference Scale 或当前相机曝光。Intensity=0 仍可能保留 Core 的基础暗部项。 |
| 环境镜面反射 | 已接入 YutrelRP 环境立方图。环境参考亮度为 20000，但先按平行光的 100000-lux 校准换算到 Endfield Core 输入域，因此参考曝光下 Core 强度为 `0.2 * LightingReferenceScale`，再随 `P/P_ref` 变化。仅 Cloth 669/679/684、Ear 674、透明衣服 832 启用；其他材质保持 0。保留 Endfield split-sum BRDF，不叠加 YutrelRP 原生 DFG。 |
| Local light | 明确暂不接入局部光 / Cluster；多方向光也未接入。 |
| 阴影 | 不透明使用 YutrelRP 屏幕 ShadowMask；透明头发 810、透明衣服 832 和绒毛 827 用自身世界坐标与几何法线查询 YutrelRP CSM；半透明描边 815 使用壳的源世界位置与烘焙外扩法线查询 CSM。映射为 mainLightVisibility=1、contactShadowVisibility=可见度，强度仅在管线应用一次。无主光/无有效阴影时全受光。 |
| 阴影差异 | 保留 YutrelRP 的级联布局、偏移与过滤，不复刻原游戏 Poisson、atlas/embedded/detail 系统。当前未接 RT 或透明透光投影。不透明描边在未外扩源像素查询 ShadowMask；半透明描边已接表面 CSM。 |
| 假阴影 | 791/796/799 保持已有 stencil 分类与颜色叠加，不当作普通受光材质另加曝光或阴影。 |
| 雾效 | 当前明确不需要，不接捕获雾或新增管线雾效。 |
| 湿润 | Core 有算法，当前 Unity 适配使用干燥路径；湿润输入接入另行处理。 |
| 透明材质 ShadowCaster | 2026-09-09 明确暂不支持透明材质独立投影，包括绒毛 827（捕获投影 310）与透明衣服 832（捕获投影 315）。透明头发/描边不增加重复投影，继续由现有不透明头发投影；透明表面接收 CSM 阴影保持已有实现。 |

## 材质算法一致性约束（2026-09-09）

除上表明确暂缓的功能与场景光照输入适配外，材质内部算法必须与当前 RDC Shader 一致。
不得用 NdotV、常数、经验曲线或简化采样替换原有分支，也不得因本帧权重为零而省略能力。
平台适配只转换坐标、深度编码、资源格式与绑定；算法和材质变体保留在 Sandbox/Core。
普通不透明头发使用 scene-depth sheen；透明头发原 Shader 本身使用 NdotV sheen，二者必须区分。
模型输入另有明确约定：保留 rest position/normal，排除捕获中的表情形变；这不仅适用于眉毛 708，也适用于其它涉及表情形变的网格。此约定不允许简化材质着色算法。

透明阶段与资源约定见 [Transparency.md](Transparency.md)。捕获两路阴影证据见
[TransparentSurfaceShadows.md](D:/Project/Unity/YutrelSandbox/Renderdoc/Endfield/2026-08-29-tangtang/Docs/TransparentSurfaceShadows.md)。

## 主光曝光修正验证

本次仅修改主光标尺的 C# 计算、关联调用和注释/文档，未修改 Core/Capture 算法或材质状态。
`python tools/agent_harness.py compile` 与 Sandbox 的
`dotnet build YutrelSandbox.slnx --no-restore` 均通过，0 警告、0 错误；
上述参考曝光、EV ±1、补偿 +1 和标尺加倍的公式数值核对通过。
未修改包内 HLSL 或捕获算法，因此未扩大 Shader/捕获回归。
遵守不启动 Unity 的约定，运行时还需在固定场景中分别调整
Lighting Reference Scale、Fixed EV100 与 Exposure Compensation，核对上述输入倍率；
检查不透明与透明主光同时响应、固定环境漫反射输入不变。编译通过不代表像素精确复刻通过。

## 环境镜面编译修正（2026-09-08）

新增环境采样曾错误地将 `float3` 传给要求完整 RGBA 的 `DecodeHDREnvironment`，
并缺少其 `EntityLighting.hlsl` 引用；现已显式 include 并传入 `float4` 采样结果。
无有效环境时使用 RenderGraph 导入的 `CoreUtils.blackCubeTexture`，不再把二维白图
绑定到 Cubemap 槽。无资源或 SpecularMultiplier=0 时在采样前返回 0。

离线检查使用当前安装的 Unity HDR 解码函数，并验证环境 Cubemap 仅出现在
ClothOpaque、SimpleOpaque、ClothTransparent 的优化后片元资源中。
224 个 Shader 阶段编译/SPIR-V 校验和 16 个捕获 Forward VS 输入检查通过；
YutrelRP harness compile 通过，0 警告、0 错误。
Sandbox 的 `--no-restore` 构建仍缺少 `Temp/obj/*/project.assets.json`；
该缓存问题独立于 Shader 编译错误，此处不将此前曝光修正的构建结果当作本次结果。
未启动 Unity，运行时重编译和画面仍待验收。未修改 TangTang 场景资源。

## 描边接入（2026-09-09）

描边保留固定环境漫反射和既有主光曝光标尺，不新增环境镜面、局部光或雾。
不透明描边使用源像素 ShadowMask 与原生 GBuffer_B XYZ 法线；捕获的 oct 编码不直接套用。
头发描边 736 使用 Base 结束后的 R32_SFloat 只读深度快照抑制内侧 sheen，
815 保留捕获的 NdotV 曲线。深度快照由 RasterPass 完成，无 UnsafePass 或附件读写反馈。

所有描边调度留在 Sandbox：饰品/衣服用同 Renderer 的 Base＋Forward；头发分为 485 Base
（队列 2016）和 736 Forward（2012），共享 Mesh 和生成参数。复用 `YutrelForwardOnlyBase` /
`YutrelForwardOnly`，队列表达 Base 的饰品→衣服1/2→头发与 Forward 的身体→脸→头发→饰品→衣服1/2。
BasePass、ForwardOnlyPass 没有描边专用标签、列表或分支，也未加入 RenderFeature。
身体、脸和透明描边没有独立的描边 Base，直接在 Forward 写深度。

管线只提供通用相机法线、可选 Base 深度快照及可用性标记，见
[CameraSurfaceTextures.md](CameraSurfaceTextures.md)。`copyDepthForForward` 默认 false，Sandbox 显式开启；
无快照时绑定远深度并置标记为 0。694/736 的精确材质路径要求有效深度快照，
Sandbox 不再以缺少深度为由关闭或替代原 sheen 算法。
运行时实现和验收记录见
[OutlineImplementation.md](D:/Project/Unity/YutrelSandbox/Renderdoc/Endfield/2026-08-29-tangtang/Docs/OutlineImplementation.md)。
