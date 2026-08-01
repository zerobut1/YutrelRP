# `shoudun-1.rdc` 已确认事实索引

## 1. 文档目的

本文集中记录目前已经从 `shoudun-1.rdc` 中确认的事件、资源和 Shader 语义，避免后续复刻角色材质时重复调查同一内容。

- 捕获文件：`D:\renderdoc\终末地\shoudun-1.rdc`
- rdc-cli Session：`shoudun`
- API：Vulkan
- 捕获平台：Linux x86-64
- 捕获规模：1513 个 Draw Call，45 个 Dispatch
- 当前重点材质：汤汤 `Cloth1Alpha`
- 详细 Shader 分析：[EID6346Cloth1AlphaShaderAnalysis.md](EID6346Cloth1AlphaShaderAnalysis.md)

捕获缺少可读的原始 GPU Marker，因此本文中的 Pass 名称是根据附件、固定管线状态和 Shader 行为给出的功能名称，不一定等于原引擎内部正式名称。

置信度约定：

- **已确认**：可由资源绑定、生产/消费关系或反汇编运算直接证明。
- **高概率**：功能已经明确，但不知道原引擎正式命名。
- **待确认**：目前只有局部证据，不应据此设计固定接口。

## 2. 已调查 EID 索引

| EID | 功能分类 | 已确认内容 |
|---:|---|---|
| 1997 | 次级 Shadow Atlas 清理 | 清理 D16 Resource 70566。 |
| 2009–2196 | ShadowCaster 批次 | 向 Resource 70566 写入动态物体/角色阴影深度；其中包含 `Cloth1Alpha`。 |
| 2167 | `Cloth1Alpha` ShadowCaster | 写入 Resource 70566；Cull Off、Depth Write、Depth Bias；采样 BaseColor Alpha，Cutoff 约 `0.177`。 |
| 3049 | `Cloth1Alpha` Base/GBuffer | 与 EID 6346 绘制同一网格；执行 Alpha Clip，写深度、法线和分类/MRT 数据，不执行完整材质光照。 |
| 3182 | 疑似角色描边 | 观察到 Front Cull 风格状态；尚未完成 Shader 和输出验证，暂不视为已确认描边 Pass。 |
| 6217–6228 | 主光阴影可见性生成 | 一个全屏 Render Pass，生成 Resource 129707。 |
| 6223 | 主 CSM 可见性写入 | 全屏三角形；输出结构为 `float2(csmVisibility, 1)`。 |
| 6227 | CSM + 次级阴影可见性写入 | 全屏三角形；输出结构为 `float2(csmVisibility, secondaryShadowVisibility)`。G 来自 Resource 70566 的约 16-tap PCF。 |
| 6230 | 后续前向材质 Render Pass 开始 | 后续多个角色 Draw Call 读取 Resource 129707；尚未整理该 Pass 内所有材质。 |
| 6346 | `Cloth1Alpha` 前向光照 | 完整角色材质光照；`ZTest Equal`、Blend Off、Cull Off，不重复 Alpha Clip。 |
| 7408 | Motion/History 消费 | 读取 Resource 129801，逆变换运动编码，并结合历史类别、深度等构造历史不连续/拒绝信息。 |

### 2.1 EID 3049、6346 的关系

两者使用相同网格数据：

```text
Index Buffer: Resource 62599
Byte Offset: 14174912
Index Count: 13728
Triangle Count: 4576
```

实际管线结构为：

```text
EID 3049
  BaseColor Alpha Clip
  -> Depth / Normal / Classification

EID 6346
  ZTest Equal
  -> 完整前向光照
```

`Cloth1Alpha` 是 Opaque Cutout，不是透明混合材质。

## 3. ShadowMask 与阴影资源

### 3.1 Resource 129707：主光阴影可见性

| 属性 | 值 |
|---|---|
| 类型 | Texture2D / Color Attachment |
| 格式 | `R8G8_UNORM` |
| 尺寸 | 3840 × 2160 |
| 生产事件 | EID 6223、6227 |
| 主要消费 | EID 6236–6557 的多个前向材质 Draw Call，包括 EID 6346 |

通道语义：

| 通道 | 已确认语义 | 数值约定 |
|---|---|---|
| R | 主方向光 CSM Visibility | `1` 为受光，`0` 为被主阴影遮挡 |
| G | 次级 Shadow Atlas Visibility | `1` 为可见，较小值表示被角色/动态物体阴影遮挡 |

G 通道的准确结论：

- 它不是 SSAO。
- 它也不是直接沿场景深度 Ray March 的屏幕空间 Contact Shadow。
- EID 6227 会重建世界位置，再投影到 D16 Resource 70566，执行约 16 次带随机旋转的 PCF 采样。
- Resource 70566 中包含角色 ShadowCaster，包括 EID 2167 的衣服阴影。
- 因此可将 G 功能性称为 **次级角色/动态阴影可见性**；它能够补充角色接触和自阴影细节，但原引擎正式名称未知。

EID 6346 中两通道并非简单相乘：

```hlsl
float main_shadow = shadow_mask.r;
float secondary_visibility = shadow_mask.g;

float ao_visibility =
    packed_material_ao * secondary_visibility;
```

- R 在直接漫反射后段选择受光分支和阴影分支。
- G 与 Material AO、Diffuse Ramp Alpha 组合，作为更细粒度的可见性门控。

### 3.2 Resource 70566：次级 Shadow Atlas

| 属性 | 值 |
|---|---|
| 类型 | Texture2D / Depth Attachment |
| 格式 | `D16` |
| 尺寸 | 4096 × 1024 |
| 清理 | EID 1997 |
| 写入 | EID 2009–2196 的 ShadowCaster Draw Call |
| 消费 | EID 6227 等阴影可见性生成 Pass |

该资源直接证明 ShadowMask.G 来源于第二套 Shadow Map，而不是环境光遮蔽纹理。

### 3.3 其它相关输入

| Resource | 格式/尺寸 | 当前结论 |
|---:|---|---|
| 129701 | 3840×2160 `R32_FLOAT` | EID 6227 用于重建屏幕像素的世界位置。 |
| 129781、129784 | 3840×2160 `R10G10B10A2_UNORM` | EID 6227 的 GBuffer 输入；包含八面体编码法线及分类信息，完整通道名待确认。 |
| 40919、40922 | 4096² `D16` | EID 6227 采样的主阴影资源；两者在 CSM 路径中的具体分工待进一步命名。 |
| 40928 | 6144×4096 `D16` | EID 6346 绑定的 Shadow Atlas；主要用于 Shader 支持的附加/局部阴影路径，主光可见性已经来自 129707。 |

## 4. EID 6346 固定管线与输出

| 项目 | 值 |
|---|---|
| API Call | Vulkan `vkCmdDrawIndexed` |
| Graphics Pipeline | Resource 78735 |
| Depth Test | `Equal` |
| Depth Write | 开启 |
| Cull | Off |
| Blend | 两个 RT 均关闭 |
| MSAA | 1x |
| Alpha Clip | 无；由 EID 3049 完成 |

输出资源：

| Resource | 格式 | 已确认用途 |
|---:|---|---|
| 129804 | `R11G11B10_FLOAT` | 完成曝光尺度和体积雾合成后的 HDR Scene Color |
| 129801 | `R10G10B10A2_UNORM` | Motion/History 数据，不是 Normal Buffer |
| 129796 | `D32S8` | 与 Base/GBuffer 阶段深度执行 Equal 测试 |

Resource 129801 的已确认编码：

```text
XY = 当前帧与上一帧位置计算出的非线性 Motion Vector
Z  = 本 Shader 固定写 1，后续参与分类位组合
A  = History/Surface Class；湿润覆盖在约 0.1 处选择 0.4 或 0.7
```

EID 7408 会读取并逆变换 XY，因此不能将 Resource 129801 当作 Packed Normal。

## 5. EID 6346 材质纹理

以下捕获资源已与本地 PNG 精确匹配，或只存在预期的 GPU 压缩误差：

| 用途 | Resource | Set/Binding | 捕获格式 | 本地文件 |
|---|---:|---|---|---|
| BaseColor | 131668 | 1 / 10 | 2048² `BC7_SRGB` | `T_actor_tangtang_cloth_01_D.png` |
| Tangent Normal | 131151 | 1 / 9 | 2048² `BC5_UNORM` | `T_actor_tangtang_cloth_01_N.png` |
| Packed Material Mask | 131169 | 1 / 7 | 2048² `BC7_UNORM` | `T_actor_tangtang_cloth_01_P.png` |
| 32³ Material Color LUT | 89545 | 1 / 6 | 1024×32 `BC7_SRGB` | `T_actor_common_cloth_lut_01_D.png` |
| Specular Ramp | 121267 | 1 / 5 | 256×1 `RGBA8` | `T_actor_tangtang_cloth_04_RS.png` |
| Diffuse Ramp | 111886 | 1 / 8 | 256×1 `RGBA8` | `T_actor_common_cloth_02_RD.png` |

Packed Material Mask 通道：

```text
R = Metallic
G = Specular Level
B = Material AO
A = Smoothness
```

基础 PBR 构造：

```hlsl
diffuse_color = albedo * (1.0f - 0.96f * metallic);
f0 = lerp(0.04f * specular_level, albedo, metallic);
perceptual_roughness = 1.0f - smoothness;
```

## 6. Diffuse Ramp 已确认用法

Resource 111886 在 EID 6346 中以 LOD 0 采样两次：

1. 主采样坐标来自修正后的 `dot(N, L)`，再从 `[-1, 1]` 映射到 `[0, 1]`。
2. 辅助采样坐标来自法线与摄像机朝向轴的点积，同样映射到 `[0, 1]`。

已确认语义：

- 主采样 RGB 用于直接漫反射调色。
- RGB 调色后会执行亮度保持，缩放上限约 `1.5`。
- 两次采样的 Alpha 与 Material AO、ShadowMask.G 组合，用于明暗分支混合和门控。
- ShadowMask.R 不会直接乘入 Ramp UV，而是在后段混合主受光分支与阴影分支。
- Ramp Alpha 不是简单的最终亮度乘数，不能直接实现为 `ramp.rgb * ramp.a`。

反汇编中可见的风格化常量包括：

```text
暗部基础亮度：0.65
漫反射饱和度修正：1.2
Ramp RGB 调色后的亮度补偿上限：1.5
```

## 7. Specular Ramp 已确认用法

Resource 121267 在 EID 6346 中固定以 LOD 0 采样一次：

```hlsl
float alpha = max(perceptual_roughness * perceptual_roughness, 0.0078f);
float a2 = alpha * alpha;
float denominator = NoH * NoH * (a2 - 1.0f) + 1.0f;
float D = a2 / (denominator * denominator);

float ramp_u = saturate(D / min(rcp(a2), 65504.0f));
float ramp_v = perceptual_roughness * (1.0f - metallic);
float3 ramp = specular_ramp.SampleLevel(sampler, float2(ramp_u, ramp_v), 0.0f).rgb;
```

- U 是 GGX NDF 相对其理论峰值的归一化结果，不是 `N·L`。
- V 与粗糙度、金属度有关；当前 Ramp 只有一行，因此不改变采样结果。
- Shader 只读取 RGB，Alpha 不参与计算。
- 直接高光使用 `D * V * (F0 * ramp.rgb)`，Ramp 取代标准 Schlick Fresnel 的颜色塑形。
- YutrelRP 使用 `_ENDFIELD_DIFFUSE_RAMP` 与 `_ENDFIELD_SPECULAR_RAMP` 两个本地 Keyword；关闭后分别恢复 Lambert 和标准 GGX 高光。

## 8. 主光与角色方向覆盖

Shader 支持将真实主光方向与一个角色/美术覆盖方向混合：

```hlsl
float3 shading_direction = lerp(
    -main_light_direction_raw,
    character_override_direction,
    override_weight);
```

EID 6346 的常量：

```text
主光 GPU 原始方向：(-0.445486, -0.728969,  0.519757)
实际朝向光源方向：( 0.445486,  0.728969, -0.519757)
角色覆盖方向：    (-0.433013,  0.500000,  0.750000)
覆盖权重：0.0
```

因此 EID 6346 实际完全使用场景主方向光，角色覆盖方向没有生效。覆盖方向没有独立颜色、强度或阴影，不是第二盏平行光。

单帧只能确定它以世界空间形式传入 Shader，不能确定 CPU 是否根据角色、摄像机或太阳方向动态生成。确认更新规则需要多帧受控对比。

## 9. SSAO 结论

EID 6346 没有绑定或采样独立的全屏 SSAO Texture。

当前 Draw Call 中与遮蔽相关的输入是：

```text
ShadowMask.R = 主光 CSM
ShadowMask.G = 次级 Shadow Atlas Visibility
PackedMap.B  = Material AO
3D 环境体积 = 环境光照及体积可见性数据
```

3D 环境体积可见性和 Material AO 都不等于 SSAO。最终画面是否在 EID 6346 之后由其它全屏 Pass 合成 SSAO，目前尚未追踪，不能由本 Draw Call 得出结论。

## 10. 环境、天气和雾资源

| 用途 | Resource | 格式/尺寸 | 置信度 |
|---|---:|---|---|
| 环境体积 A0/A1/A2 | 45033 / 45039 / 45045 | 128×64×128 `R11G11B10_FLOAT` | 高概率为三级环境光数据，通道名待定 |
| 环境体积 B0/B1/B2 | 45036 / 45042 / 45048 | 128×192×128 `RGBA8` | 高概率为三级方向/可见性数据，通道名待定 |
| Specular IBL | 43023 | 128 Cubemap `BC6_UFLOAT` | 已确认 |
| 雨滴法线/Mask | 41471 | 1024² `BC7_UNORM` | 已确认参与基础湿润层 |
| 垂直流动法线 | 131140 | 256² `BC7_UNORM` | 已确认参与基础湿润层 |
| 细噪声法线 | 1038 | 1024² `BC7_UNORM` | 已确认资源，EID 6346 对应分支关闭 |
| 彩色 Voronoi 噪声 | 38572 | 512² `BC3_SRGB` | 已确认资源，EID 6346 对应分支关闭 |
| 体积雾 Froxel | 128921 | 320×270×128 `RGBA16_FLOAT` | 已确认 |

EID 6346 的光照来源包括：

- 主方向光；
- Shader 支持的 Clustered 局部光；代表像素未观察到有效局部光增量；
- 三组级联 3D 环境光体积；
- BC6H Cubemap Specular IBL；
- 最终体积雾合成。

## 11. 当前仍待确认

- 原引擎对 Resource 70566 和 ShadowMask.G 的正式命名。
- 40919、40922、40928 三张 Shadow Atlas 的正式职责划分。
- EID 3182 是否确实为描边，以及描边颜色/宽度来源。
- 三组环境体积每个通道的正式语义。
- 整个 EID 6346 覆盖区域中局部光的实际影响范围。
- 角色覆盖光方向的 CPU 更新规则。
- EID 6346 之后是否存在影响角色最终颜色的独立 SSAO 合成 Pass。
- 捕获曝光标量与原引擎 Pre-Exposure 的完整契约。

## 12. 常用复查命令

```powershell
rdc --session shoudun status
rdc --session shoudun draw 6346 --json
rdc --session shoudun descriptors 6346 --stage ps --json
rdc --session shoudun usage 129707 --json
rdc --session shoudun usage 70566 --json
rdc --session shoudun events --range 6208:6230 --json
rdc --session shoudun shader 6346 ps --reflect --constants --json
```

分析其它截帧时不能假设 EID 和 Resource ID 保持一致，应结合网格三角形数、Index Buffer、纹理尺寸、格式和 Shader 状态重新识别。
