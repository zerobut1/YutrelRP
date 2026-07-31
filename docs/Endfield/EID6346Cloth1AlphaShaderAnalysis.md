# 《终末地》EID 6346：Cloth1Alpha 前向 Shader 分析

## 1. 分析目标

本文分析以下 RenderDoc 事件，作为后续在 YutrelRP 中复刻 `Endfield/Character` Shader 的依据：

- 捕获：`D:\renderdoc\终末地\shoudun-1.rdc`
- Drawcall：EID 6346
- 当前场景材质：`Assets/LocalAssets/Characters/TangTang/Materials/Cloth1Alpha.mat`
- 分析工具：rdc-cli + RenderDoc 1.45

本文只描述捕获中可确认的行为，不包含 Shader 实现。反编译得到的临时变量没有原始名称，因此无法完全命名的全局资源会明确标注为“推断”。

## 2. 结论摘要

EID 6346 是一个 **Opaque Cutout 材质的前向光照 Drawcall**，不是透明混合：

1. EID 3049 已对同一网格执行 Alpha Clip，并写入深度。
2. EID 6346 使用 `ZTest Equal`，只为前一阶段保留下来的像素执行完整光照。
3. EID 6346 本身没有 `discard/clip`，也没有 Alpha Blend。

本次 Drawcall 包含的主要功能如下：

| 功能 | 本次 Drawcall 状态 | 结论 |
|---|---:|---|
| BaseColor + 32³ LUT 调色 | 启用 | LUT 在光照前改变材质底色 |
| BC5 法线贴图 | 启用 | 切线空间法线转换到世界空间 |
| Metallic / Specular Level / AO / Smoothness | 启用 | 来自一张 RGBA Packed Mask |
| 主方向光及风格化 Ramp | 启用 | 不是单纯的 Lambert `N·L` |
| GGX 类直接镜面反射 | 启用 | 额外经过一张镜面 Ramp 调色 |
| 屏幕空间阴影/可见性 | 启用 | R、G 通道承担不同的可见性作用 |
| Clustered 局部光 | Shader 支持 | 当前抽样像素未观察到有效增量 |
| 环境漫反射 | 启用 | 来自三组级联 3D 光照体积 |
| 环境镜面反射 | 启用 | 来自 BC6H Cubemap，按粗糙度选 mip |
| 基础湿润/雨滴层 | 启用 | 全局强度为 `215/255`，但覆盖由逐像素雨滴 Mask 决定 |
| 第二层雨水噪声/闪点 | 禁用 | 对象天气参数 W 为 0，相关分支不影响本次结果 |
| 材质颜色校正/Rim 分支 | 禁用 | 材质开关为 0 |
| 体积雾/大气合成 | 启用 | 在最终颜色输出前合成 |
| 屏幕运动及历史分类输出 | 启用 | RT1 不是法线缓存 |

最重要的修正是：EID 6346 的第二个输出 `R10G10B10A2_UNORM` 是 **运动/历史相关缓冲**，不是 Packed Normal。现有总路线文档中将其视为辅助法线的描述属于早期判断，应以本文分析为准。

## 3. Drawcall 与固定管线状态

| 项目 | EID 6346 |
|---|---|
| API | Vulkan `vkCmdDrawIndexed` |
| Graphics Pipeline | Resource 78735 |
| Index Buffer | Resource 62599，Byte Offset 14174912，`uint16` |
| Index Count | 13728 |
| Triangle Count | 4576 |
| Instance Count | 1 |
| 分辨率 | 3840 × 2160 |
| Raster | Solid、No Cull、Front CCW |
| Depth Test | 开启，Compare `Equal` |
| Depth Write | 开启 |
| Stencil | 关闭 |
| Blend | 两个 RT 均关闭，RGBA 全通道写入 |
| MSAA | 1x |
| Alpha-to-Coverage | 关闭 |

Render Target：

| 输出 | Resource | 格式 | 用途 |
|---|---:|---|---|
| RT0 | 129804 | `R11G11B10_FLOAT` | 雾合成后的 HDR 场景颜色 |
| RT1 | 129801 | `R10G10B10A2_UNORM` | 非线性编码的运动量 + 表面/历史分类 |
| Depth | 129796 | `D32S8` | 与 Base 阶段生成的深度做 Equal 测试 |

EID 3049 与 EID 6346 使用相同的 Index Buffer、Offset 和三角形数量，可以确认它们绘制的是同一块网格。该结构相当于“先建立 Cutout 深度/分类，再执行昂贵的前向材质光照”。

## 4. 顶点阶段

顶点 Shader 的主要职责为：

- 解码压缩存储的法线和切线；
- 读取骨骼索引与权重，执行最多四骨骼蒙皮；
- 计算当前帧与上一帧的蒙皮/变换结果；
- 输出 UV、世界坐标、世界法线、世界切线及切线符号；
- 输出当前帧和上一帧的裁剪空间辅助坐标，供像素 Shader 构造 RT1；
- 应用当前帧投影抖动；
- 输出对象/实例索引，以读取对象级材质及天气数据。

可见顶点输入包括：

| 数据 | 捕获格式 | 作用 |
|---|---|---|
| Position | `R32G32B32_FLOAT` | 模型空间位置 |
| Packed Normal | 位打包数据 | 解码为顶点法线 |
| UV | `R32G32_FLOAT` | 材质纹理坐标 |
| Packed Tangent | `R8G8B8A8_SNORM` | 切线和 Handedness |
| Bone Weights | `R16G16B16A16_UNORM` | 蒙皮权重 |
| Bone Indices | `R8G8B8A8_UINT` | 蒙皮骨骼索引 |

YutrelRP 复刻时不必复制原游戏的顶点数据压缩方式，但必须保留正确的 TBN，以及在需要复刻 RT1/TAA 行为时提供当前帧和上一帧位置。对于蒙皮角色，上一帧只使用上一帧 Object Matrix 并不充分，还需要考虑上一帧骨骼姿态。

## 5. 材质资源及本地资产对应

### 5.1 已精确匹配的材质纹理

通过导出捕获资源并与本地 PNG 做像素比较，以下资源已精确或在 GPU 压缩误差内匹配。比较时需要做垂直翻转，这是 RenderDoc 导出与源图片坐标方向的差异，不表示 Unity 材质需要额外翻转 UV。

| Shader 作用 | 捕获资源 | Set/Binding | 捕获格式 | 本地资源 |
|---|---:|---|---|---|
| BaseColor | 131668 | 1 / 10 | 2048² `BC7_SRGB` | `T_actor_tangtang_cloth_01_D.png` |
| Tangent Normal | 131151 | 1 / 9 | 2048² `BC5_UNORM` | `T_actor_tangtang_cloth_01_N.png` |
| Packed Material Mask | 131169 | 1 / 7 | 2048² `BC7_UNORM` | `T_actor_tangtang_cloth_01_P.png` |
| 32³ Color LUT | 89545 | 1 / 6 | 1024×32 `BC7_SRGB` | `T_actor_common_cloth_lut_01_D.png` |
| Specular Ramp | 121267 | 1 / 5 | 256×1 `RGBA8` | `T_actor_tangtang_cloth_04_RS.png` |
| Direct Diffuse/Shadow Ramp | 111886 | 1 / 8 | 256×1 `RGBA8` | `T_actor_common_cloth_02_RD.png` |

像素平均绝对误差：BaseColor `0.00044`、Normal RG `0.00088`、Packed Mask `0.00111`；LUT 和两张 Ramp 的导出结果与本地 PNG 完全一致。这同时验证了资源语义判断不是仅凭画面外观猜测。

当前 `Cloth1Alpha.mat` 只绑定了 BaseColor 和 Normal；Packed Mask、LUT、两张 Ramp 尚未接入当前 Shader。

### 5.2 Packed Mask 通道

像素 Shader 对 Resource 131169 的通道解释可以确定为：

| 通道 | 含义 | 运算 |
|---|---|---|
| R | Metallic | 金属度 |
| G | Specular Level | 非金属 F0 的缩放量，基础值约为 `0.04 × G` |
| B | Material AO | 对直接光与环境光的若干项执行遮蔽/门控 |
| A | Smoothness | Perceptual Roughness = `1 - A` |

这是后续复刻首先需要补齐的输入。仅使用 BaseColor 和 Normal 无法还原布料在金属配件、暗部遮蔽及高光宽度上的差异。

### 5.3 全局光照及天气资源

| Shader 作用 | 捕获资源 | Binding | 格式/尺寸 | 判断置信度 |
|---|---:|---:|---|---|
| 屏幕阴影/可见性 | 129707 | Set 0 / 18 | 3840×2160 `R8G8_UNORM` | 高 |
| Shadow Atlas | 40928 | Set 0 / 20 | 6144×4096 `D16` | 高 |
| 环境体积 A0 | 45033 | Set 0 / 28 | 128×64×128 `R11G11B10_FLOAT` | 中 |
| 环境体积 B0 | 45036 | Set 0 / 25 | 128×192×128 `RGBA8` | 中 |
| 环境体积 A1 | 45039 | Set 0 / 27 | 128×64×128 `R11G11B10_FLOAT` | 中 |
| 环境体积 B1 | 45042 | Set 0 / 24 | 128×192×128 `RGBA8` | 中 |
| 环境体积 A2 | 45045 | Set 0 / 26 | 128×64×128 `R11G11B10_FLOAT` | 中 |
| 环境体积 B2 | 45048 | Set 0 / 23 | 128×192×128 `RGBA8` | 中 |
| Specular IBL Cubemap | 43023 | Set 0 / 36 | 128 Cubemap `BC6_UFLOAT` | 高 |
| 雨滴法线/Mask | 41471 | Set 0 / 35 | 1024² `BC7_UNORM` | 高 |
| 垂直流动法线 | 131140 | Set 0 / 32 | 256² `BC7_UNORM` | 高 |
| 细噪声法线 | 1038 | Set 3 / 1 | 1024² `BC7_UNORM` | 高，当前分支关闭 |
| 彩色 Voronoi 噪声 | 38572 | Set 3 / 0 | 512² `BC3_SRGB` | 高，当前分支关闭 |
| 体积雾 Froxel | 128921 | Set 0 / 29 | 320×270×128 `RGBA16_FLOAT` | 高 |

三组环境体积以级联形式按世界位置采样。它们共同提供环境漫反射、主导方向或可见性一类数据，但仅凭无符号的 SPIR-V 不能可靠拆出每个通道的正式名称，因此不应机械复制其纹理布局。

四张全局雨水纹理没有在 `TangTang/Textures` 中找到像素匹配资源。后续接入湿润效果时需要从全局资源中定位原图，或提供功能等价的替代纹理。

## 6. 像素 Shader 总体数据流

EID 6346 的主流程可以概括为：

```text
BaseColor（硬件解码到 Linear）
  -> 转回 sRGB 坐标并采样 32³ LUT
  -> 读取 Packed Mask 与切线空间法线
  -> 根据对象天气参数生成湿润覆盖、雨滴法线和流动法线
  -> 修改 BaseColor、Normal、Roughness
  -> 主方向光 + 风格化 Diffuse Ramp + GGX Specular Ramp
  -> Clustered 局部光（条件执行）
  -> 3D 环境体积漫反射 + Cubemap 镜面 IBL
  -> 全局曝光/场景尺度
  -> 体积雾/大气合成
  -> RT0 HDR Color

当前帧位置 + 上一帧位置 + 湿润分类
  -> RT1 Motion/History Data
```

## 7. BaseColor 与 32³ LUT

BaseColor 纹理为 sRGB 资源，采样后由硬件解码为 Linear。Shader 随后执行一次近似精确的 Linear → sRGB 变换，将颜色作为 3D LUT 坐标。

LUT 本质是一个 `32 × 32 × 32` RGB 立方体，平铺为 `1024 × 32` 二维纹理：

- R、G 决定单个 Slice 内的位置；
- B 选择相邻的两个 Slice；
- Shader 手动采样两次并在 B 方向插值；
- LUT 自身是 sRGB 纹理，采样结果再次由硬件解码为 Linear。

因此它不是最终画面的后处理 LUT，而是 **材质级 Albedo 重映射**，发生在 PBR 光照和湿润处理之前。

以 UV `(0.46246, 0.70356)` 为例：

- BaseColor Linear：`(0.14893, 0.00562, 0.00970)`；
- LUT 输出 Linear：`(0.08625, 0.00137, 0.00698)`。

LUT 对颜色变化很明显，复刻时不能把它简化成可选的轻微调色项。

## 8. 法线与双面处理

BC5 纹理提供切线空间 XY：

1. XY 从 `[0, 1]` 重映射到 `[-1, 1]`；
2. 应用材质 Normal Scale；
3. 通过 `sqrt(saturate(1 - x² - y²))` 重建 Z；
4. 使用顶点阶段输出的 TBN 转换到世界空间；
5. 结合 `FrontFacing` 和材质双面标志处理背面方向。

固定管线为 No Cull，因此双面法线修正是材质外观的一部分。虽然 `Cloth1Alpha` 当前可见区域不一定大量展示背面，复刻时仍应避免把 Cull 固定成 Back 后忽略该路径。

## 9. 湿润与雨水处理

### 9.1 本次实际启用的基础湿润层

对象/实例数据中包含四个 8-bit 天气控制量，本次像素调试得到：

```text
(0.8431373, 0, 0, 0) = (215/255, 0, 0, 0)
```

第一分量驱动基础湿润层。`0.843` 是全局/对象湿润强度，不是最终逐像素湿润 Mask；最终覆盖还会由世界空间雨滴分布和材质条件决定。

基础湿润层执行了以下处理：

- 对 1024² 雨滴纹理做世界空间三平面采样；
- 生成两种世界空间尺度约为 `20` 和 `34.35` 的解析雨滴图案；
- 使用时间参数驱动雨滴移动；
- 多次采样 256² 垂直流动法线，形成向下流动的水痕；
- 把原材质法线混合到雨滴/流动法线；
- 按湿润覆盖改变 Albedo；
- 将湿润区域的 Roughness 向 `min(DryRoughness, 0.05)` 收敛；
- 改变 RT1 的 A 通道表面/历史分类。

湿润强度很高时，仍然会同时存在干燥像素和完全湿润像素。抽样结果：

| 像素 | 最终湿润覆盖 | 最终 Roughness | 说明 |
|---|---:|---:|---|
| (1911, 92) | 0 | 约 0.4706 | 位于局部干燥区，保留材质粗糙度 |
| (2160, 96) | 约 0.5748 | 约 0.20 | 部分湿润 |
| (1750, 126) | 1 | 0.05 | 完全湿润 |
| (1617, 287) | 1 | 0.05 | 完全湿润 |
| (1939, 418) | 1 | 0.05 | 完全湿润 |

这说明原 Shader 的“湿润”不是一个全材质统一降低粗糙度的滑杆，而是一个具有空间分布、流动法线和独立覆盖的天气层。

### 9.2 本次关闭的第二雨水/闪点层

天气参数第四分量 W 为 0，因此另一个较昂贵的分支没有影响本次输出。静态 Shader 代码表明该分支会：

- 三平面采样 1024² 细噪声法线；
- 三平面采样 512² Voronoi/随机噪声；
- 进一步扰动法线并改变颜色；
- 压低或重映射粗糙度、金属度；
- 产生尖锐的雨水闪点/高光。

该分支属于 Shader 的通用能力，但不是复刻 EID 6346 当前画面所必需的第一阶段内容。

## 10. 直接光照

### 10.1 BRDF 基础

Shader 使用 Metallic Workflow：

```text
DiffuseColor = Albedo × (1 - 0.96 × Metallic)
F0 = lerp(0.04 × SpecularLevel, Albedo, Metallic)
PerceptualRoughness = 1 - Smoothness
```

镜面部分使用 GGX 类 NDF 和可见性项，并包含环境 BRDF/多重散射能量补偿的多项式近似。它不是简单的 Blinn-Phong 高光。

### 10.2 主方向光与风格化 Ramp

主方向光读取全局方向、颜色及屏幕可见性纹理 Resource 129707：

- R：主要阴影衰减；
- G：第二层接触/可见性/遮蔽门控；
- B 未观察到对本材质有独立主要语义。

Resource 129707 的生产 Shader 表明 R 写入主阴影结果，G 在另一路径写入第二种接触/可见性信息。现有证据不足以把 G 严格命名为 SSAO，因此复刻文档只称其为 Secondary Visibility。

直接漫反射不是只计算 `saturate(N·L)`：

- Shader 对法线与光方向的点积做重映射；
- 采样 `T_actor_common_cloth_02_RD.png`；
- Ramp RGB 改变直接光颜色，Alpha 参与明暗过渡/权重；
- Shader 在不同阶段对该 Ramp 进行不止一次采样。

镜面响应先由 GGX 计算，再使用 `T_actor_tangtang_cloth_04_RS.png` 进行风格化调色或形状控制。这两张 Ramp 是《终末地》衣料观感区别于纯标准 PBR 的关键输入。

### 10.3 Clustered 局部光

Shader 包含 Cluster/Tile Light List 遍历：

- 从 SSBO 位掩码中取出当前 Tile/Froxel 的灯光；
- 支持 Point/Spot 等局部光；
- 计算距离和角度衰减；
- 条件采样 Cookie；
- 对 Shadow Atlas 执行约 9 次 Comparison Sample 的 PCF；
- 将局部光也带入材质 BRDF。

在已调试的代表像素中，进入和离开 Clustered 累加段的颜色没有变化，说明该像素没有有效局部光贡献。不能据此断言整次 Drawcall 的所有像素都没有局部光；复刻颜色的第一阶段可以先完成主方向光，再按 YutrelRP 现有局部光系统接入。

## 11. 环境光照

### 11.1 环境漫反射

Shader 按世界位置从三组级联 3D 体积中取样，并结合世界法线、AO 和可见性生成环境漫反射。三组体积覆盖不同空间尺度，边界处会做级联选择或混合。

这些体积很可能同时编码 Irradiance、主导方向、遮蔽或 Probe 可见性，但通道正式语义尚未完全确定。YutrelRP 后续复刻应优先对齐“结果功能”：使用项目自己的环境光、Probe 或 DDGI 输出，不需要照搬原游戏的 3D 纹理排列。

### 11.2 环境镜面反射

Specular IBL 使用 BC6H Cubemap Resource 43023：

- 以世界空间反射向量采样；
- mip 近似使用 `5 + 1.2 × log2(Roughness)`；
- 再应用预积分 BRDF 近似、能量补偿及遮蔽；
- 湿润层降低 Roughness 后，会显著提高反射清晰度和强度。

因此湿润效果的视觉主体不仅来自雨滴法线，也来自它对 Specular IBL 的放大。

## 12. 曝光与雾

直接光、局部光和环境光合并后，Shader 使用全局标量 `0.26752895` 做场景曝光/亮度尺度换算，表现为除以该值，即约乘 `3.738`。该值可能属于原引擎 Pre-Exposure 契约，不能直接硬编码到材质。

随后 Shader 采样 320×270×128 的 RGBA16F Froxel 体积纹理，按透射率执行近似如下的合成：

```text
FinalColor = FogScattering + LitColor × FogTransmittance
```

代表像素 `(1911, 92)`：

- 光照累加：`(0.068206, 0.003274, 0.006588)`；
- 曝光尺度后：`(0.254949, 0.012239, 0.024626)`；
- 雾合成后：`(0.256229, 0.017091, 0.029828)`。

如果 YutrelRP 已在独立阶段统一合成雾，则不应在角色 Shader 内重复复刻该步骤，否则角色会接受两次雾。

## 13. RT1：运动与历史分类

### 13.1 写入方式

像素 Shader 使用顶点阶段传入的当前帧和上一帧位置构造屏幕位移。按反编译变量表达，核心过程为：

```text
motion = current.xy / current.z - previous.xy / previous.z
motion.y = -motion.y
motion *= 0.5

encoded.xy = 0.5 + 0.5 × sign(motion) × abs(motion)^(1/4)
encoded.z = 1
encoded.w = wetCoverage > 0.1 ? 0.7 : 0.4
```

由于 RT1 的 A 只有 2 bit，`0.4` 和 `0.7` 实际分别量化到约 `1/3` 和 `2/3`。因此 A 不是连续湿润强度，而是离散表面分类。

代表像素 `(1911, 92)` 的实际输出为：

```text
(0.4660758, 0.4744720, 1, 0.4)
```

### 13.2 后续消费验证

EID 7408 的全屏 Pass 会读取 Resource 129801，并执行近似逆变换：

```text
motion = sign(encoded.xy - 0.5) × (2 × encoded.xy - 1)^4
```

它还会读取上一帧同类缓冲，对比：

- 当前与上一帧运动量；
- 当前与上一帧 A 通道类别；
- 深度差异；
- 其它压缩分类位。

这些差异被组合成历史不连续/拒绝信息。因此可以高置信度确定：

- XY 是 Motion Vector 风格的非线性编码；
- A 是参与历史稳定性判断的表面类别，本 Shader 用湿润覆盖选择类别；
- B 在本 Shader 中固定写 1，并在后续 Pass 中参与其它分类位的合并；
- RT1 绝不能接到 YutrelRP 的 Normal GBuffer。

如果当前阶段只追求静态截图的颜色一致，RT1 可以晚于光照实现；如果目标包含 TAA、动态蒙皮和雨水高光的历史稳定性，则必须按 YutrelRP 自己的 Motion Vector/Reactive Mask 契约接入。

## 14. 本次关闭或不属于前向 Shader 的功能

| 功能 | 状态 | 说明 |
|---|---:|---|
| Alpha Clip | 前向阶段关闭 | EID 3049 已建立 Cutout 深度；ShadowCaster EID 2167 Cutoff 约为 `0.177` |
| Alpha Blend | 关闭 | RT0、RT1 均不混合 |
| 第二雨水噪声/闪点 | 关闭 | 对象天气 W = 0 |
| 材质颜色校正 | 关闭 | 对应材质开关为 0，Brightness/Saturation/Contrast 默认 1 |
| 材质 Rim 分支 | 关闭 | 同一可选分支未启用 |
| 全局附加 Rim | 无贡献 | 捕获中的全局 Rim 颜色为 0 |

`Cloth1Alpha` 名字中的 Alpha 表示 Cutout，不表示透明。当前材质文件里的 `_EndfieldAlphaCutoff = 0.177` 与 ShadowCaster 捕获一致；该阈值应由 Base/Depth/Shadow 路径使用，而不是在 EID 6346 等价的前向 Pass 中重复 Clip。

## 15. 对当前 YutrelRP 复刻的建议顺序

### 阶段 A：补齐材质数据

1. 接入 `T_actor_tangtang_cloth_01_P.png`；
2. 按 R/G/B/A 解码 Metallic、Specular Level、AO、Smoothness；
3. 接入 `T_actor_common_cloth_lut_01_D.png` 的 32³ 平铺 LUT；
4. 保证 BC5 Normal、TBN、双面法线正确。

这是当前 `Cloth1Alpha.mat` 与捕获之间最直接的数据缺口。

### 阶段 B：建立主要光照外观

1. 使用 Metallic Workflow + GGX 类 BRDF；
2. 接入 YutrelRP 主方向光和主阴影；
3. 使用 `T_actor_common_cloth_02_RD.png` 塑造直接漫反射/阴影过渡；
4. 使用 `T_actor_tangtang_cloth_04_RS.png` 塑造镜面响应；
5. 正确应用材质 AO 和屏幕可见性。

应先对齐 Ramp 采样前的标量输入和颜色空间，再调 Ramp 强度；否则容易通过错误参数偶然拟合单一截图。

### 阶段 C：环境光

1. 使用 YutrelRP 当前环境漫反射或后续 DDGI 输出替代原游戏三组 3D 体积；
2. 接入 Specular IBL 和粗糙度 mip；
3. 加入环境 BRDF/多重散射能量补偿；
4. 使用 YutrelRP 的曝光契约，不硬编码捕获中的 `0.26752895`。

### 阶段 D：基础湿润层

1. 增加对象级 Wetness 强度；
2. 生成独立的世界空间雨滴覆盖，而不是统一降低粗糙度；
3. 接入三平面雨滴、流动法线和时间动画；
4. 同时修改 Normal、Roughness、Albedo；
5. 全湿区域 Roughness 目标约为 `0.05`。

全局雨水纹理目前没有本地精确匹配资源，需要先解决资源来源或等价替代。

### 阶段 E：管线集成

1. 保持 Base/Depth 阶段 Alpha Clip、Forward 阶段 `ZTest Equal` 的结构；
2. Fog 若由管线统一处理，角色材质不重复执行；
3. 静态颜色完成后，再接入蒙皮 Motion Vector 与湿润 Reactive/History 分类；
4. 最后再实现当前未启用的第二雨水闪点和可选 Rim/颜色校正。

## 16. 建议的调试输出

复刻时建议按以下顺序提供临时 Debug View，以避免只比较最终颜色：

1. LUT 前/后的 Linear Albedo；
2. Metallic、Specular Level、AO、Roughness 四通道；
3. Dry World Normal 与 Wet World Normal；
4. Wet Coverage 和最终 Roughness；
5. 主光 Ramp 输入与输出；
6. 直接漫反射、直接镜面反射；
7. 环境漫反射、Specular IBL；
8. 曝光前颜色、雾前颜色、最终颜色；
9. Motion Vector 解码结果和 History Class。

优先验证截帧中已调试的干燥点 `(1911, 92)`、部分湿润点 `(2160, 96)` 和完全湿润点 `(1750, 126)`，可以快速区分基础 BRDF 偏差与湿润层偏差。

## 17. 尚未完全确定的内容

- 三组 3D 环境体积每个通道的正式语义；
- 屏幕可见性纹理 G 通道在原引擎中的正式名称；
- RT1 A/B 各离散类别在原引擎中的正式枚举名；
- 当前 Drawcall 全部像素中的 Clustered 局部光覆盖范围；
- 四张全局雨水纹理在资源工程中的原始文件名；
- 捕获中的曝光标量与原引擎自动曝光/Pre-Exposure 的准确契约。

这些未知项不会阻塞基础材质、主光、环境光和基础湿润效果的复刻，但在实现对应模块时应通过额外 EID、相邻帧或资源生产 Pass 继续验证，避免把推断写成固定接口。
