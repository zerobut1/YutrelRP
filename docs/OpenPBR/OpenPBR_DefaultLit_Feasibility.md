# YutrelRP DefaultLit 替换为 OpenPBR — 可行性调研

> 范围:仅 DefaultLit(OpenPBR 的基底层 base + specular,暂不含 transmission / subsurface / coat / fuzz / thin-film)。
> 参考实现:Adobe `openpbr-bsdf`(D:\Project\OpenPBR\openpbr-bsdf),OpenPBR 1.1 规范(D:\Project\OpenPBR\OpenPBR)。
> 日期:2025-06。基于 YutrelRP 当前代码(6000.5.6f1,com.yutrel.render-pipelines.yutrel)。

---

## 0. 结论速览

| 问题 | 结论 |
|---|---|
| DefaultLit 需要哪些外部资源? | 4/8 张预计算 LUT(全部 32 texel 级,总数据 <150KB,含 2 张 32³ 3D 表);直接光**不需要**现有 DFG LUT;IBL 可继续复用 DFG LUT(近似)。另有 1 个关键工程约束:参考实现**没有 HLSL 后端**,官方只支持 Slang/C++/GLSL/CUDA/MSL |
| 参数如何进 GBuffer? | 需要从 3 RT 扩到 **4 RT(16 通道)**,其中可折叠参数:specular_weight×specular_ior→有效 f0(1 标量),roughness×anisotropy→αx/αy(2 标量),base_weight×base_color→加权颜色 |
| 哪些可在 BasePass 预计算? | 所有**视图无关**量:加权 base color、有效 f0、αx/αy、diffuse roughness、金属平均 Fresnel(F82 均值)、MMS 的 scale 因子;视图侧能量补偿缓存 E(NoV) 严格说也可在 BasePass 算,但不建议存进 GBuffer(见 §3.3) |
| 主要风险 | ① HLSL 移植工作量(约 4 个头文件 + 2 张 3D LUT 数据);② 漫反射从 Lambert 换成 EON、介电 specular 换成真实 Fresnel,光照 pass ALU 增加;③ GBuffer 带宽 +33% |

---

## 1. 问题一:OpenPBR DefaultLit 需要哪些外部资源(LUT 等)?

### 1.1 参考实现的全部 LUT(8 张)

来源:`D:\Project\OpenPBR\openpbr-bsdf\openpbr_data_constants.h`、`impl\data\*`。全部为 32 texel 精度的预计算表:

| ID | 名称 | 维度 | 内容 | 用途 |
|---|---|---|---|---|
| 0 | IdealDielectricEnergyComplement | 3D 32³ | 标量 unorm | 理想介电(透射路径)多次散射能量补偿 |
| 1 | IdealDielectricAverageEnergyComplement | 2D 32² | 标量 unorm | 上表余弦加权均值 |
| 2 | IdealDielectricReflectionRatio | 2D 32² | 标量 unorm | 介电 MMS 反射/透射比 |
| 3 | OpaqueDielectricEnergyComplement | 3D 32³ | 标量 unorm | **扩散 lobe 视图侧+灯光侧能量补偿**;coat 的 IOR 反射系数也用 |
| 4 | OpaqueDielectricAverageEnergyComplement | 2D 32² | 标量 unorm | 扩散 lobe 补偿分母 |
| 5 | IdealMetalEnergyComplement | 2D 32² | 标量 unorm | 金属 MMS 能量补偿 |
| 6 | IdealMetalAverageEnergyComplement | 2D(实为 1D)32 | 标量 unorm | 金属 MMS 补偿分母 |
| 7 | LTC | 2D 32² | vec3(a_inv,b_inv,R) | fuzz(Disney sheen LTC 拟合,tizian/ltc-sheen,Apache-2.0) |

**DefaultLit(不透明基底层)实际需要:ID 3、4、5、6 共 4 张。**
- ID 0/1/2:仅当 `transmission_weight > 0`(或 subsurface 非零)时,介电 MMS lobe 才贡献;DefaultLit 权重为 0,可整体裁剪。
- ID 7(LTC):仅 fuzz lobe 用。
- thin-film 是解析公式,无 LUT。

数据量(按 uint16 存储):32³×2B=64KiB/张(3D),32²×2B=2KiB/张(2D)。全部 8 张 ≈ 134KiB,4 张 DefaultLit 版本 ≈ 132KiB —— 几乎可以忽略。

### 1.2 两种访问模式(openpbr_settings.h)

- **数组模式** `OPENPBR_USE_TEXTURE_LUTS=0`(默认):LUT 内嵌为 shader `const` 数组,自带线性/双线/三线插值函数(`openpbr_energy_array_access.h`)。自包含、无纹理绑定,但 shader 体积与编译时间增大(两张 3D 表即 64K 元素)。
- **纹理模式** `=1`:宿主创建 2D/3D 纹理(硬件过滤),需定义 `OPENPBR_SAMPLE_2D_TEXTURE(lut_id, uv)` / `OPENPBR_SAMPLE_3D_TEXTURE(lut_id, uvw)` 两个宏;ID 0-7 可直接作为 bindless 槽位偏移。Unity 下推荐此模式:2×R16_UNorm 3D(32³)+ 4×R16_UNorm 2D(32²)+ 1×R16G16B16A16 2D(LTC)。

**纹理模式 UV 约定(移植时极易踩坑):**
- 能量表:索引先经 `openpbr_remap_exact_index`(texel-center 重映射),且**轴交换** —— `SAMPLE_2D(lut_id, vec2(remap_y, remap_x))`、`SAMPLE_3D(lut_id, vec3(remap_z, remap_y, remap_x))`。
- LTC 表(仅 fuzz):`uv = (cos_theta, alpha) * (31/32) + (0.5/32)`,不交换轴。
- 坐标→索引映射(`openpbr_microfacet_multiple_scattering_data.h`):
  - ior:`1..2.5` 线性映射到表的上半区;`<1` 用倒数映射到下半区;超出 `[0.4, 2.5]` 用 F0 外推衰减。
  - alpha:`sqrt(alpha) * 31`(即对感知粗糙度线性)。
  - cos_theta:`cos_theta * 31`。

### 1.3 关键工程约束:语言后端

`openpbr_settings.h` 明确:**没有 `OPENPBR_LANGUAGE_TARGET_HLSL`;HLSL 管线请用 Slang**。YutrelRP 全部 shader 是 HLSL。三条路线:

1. **手工移植到 HLSL(推荐做 v1)**。代码是 GLSL 风格 + 极薄宏层(`vec2/3/4`、`mix`、`saturate` 等),机械翻译可行。DefaultLit 的 **eval-only** 路径只需 4 个头文件:
   - `openpbr_diffuse_lobe.h`(EON 扩散 + 能量补偿)
   - `openpbr_comprehensive_microfacet_lobe.h`(GGX VNDF + 综合反射系数)
   - `openpbr_microfacet_multiple_scattering_lobes.h`(金属 MMS + 介电 MMS,后者可裁掉)
   - `openpbr_aggregate_lobe.h` 的 `calculate_lobe_value`(求和即可;采样权重/MIS 只对路径追踪有意义)
   - 外加 `openpbr_lobe_utils.h`(Fresnel、F82、IOR 工具)、`openpbr_reflection_transmission_coefficient.h`(反射系数结构)
2. **Slang**:可直接 include 原始头文件,省移植。但 Unity 的 Slang 支持在 6000.5 上处于何种成熟度、能否直接参与现有 RenderGraph 光栅 pass,需先做 PoC 验证(本项目当前零 Slang 使用)。
3. **仅用 LUT 数据 + 现有 BRDF.hlsl 重写**:把 OpenPBR 的"结果"用近似公式(如 Schlick + DFG)代替,精度低,不建议。

### 1.4 与现有资源的关系

- 现有 `_DFG_LUT`(Filament multiscatter,`Runtime/Textures/DFG_LUT.exr`):OpenPBR 直接光**不用**它(能量补偿来自 §1.1 的 LUT);仅 IBL 环境光继续使用(§3.4 说明近似性)。
- 新增资源建议:`Runtime/Textures/OpenPBR_*` 4 张纹理,或运行时由 C# 数组生成(数据可从 Adobe 仓库一次性转换)。C# 侧可仿照 `YutrelRPRuntimeTextures.cs` 声明。

---

## 2. 问题二:OpenPBR 参数如何进 GBuffer?BasePass 可预计算什么?

### 2.1 DefaultLit 相关参数集(openpbr_resolved_inputs.h / 规范 parametrization)

| 参数 | 范围 | 默认 | 说明 |
|---|---|---|---|
| base_weight | [0,1] | 1 | 整体权重 |
| base_color | [0,1]³ | (0.8) | |
| base_metalness | [0,1] | 0 | |
| base_diffuse_roughness | [0,1] | 0 | EON 扩散粗糙度 |
| specular_weight | [0,∞) | 1 | 通过 IOR→F0 缩放实现 |
| specular_color | [0,1]³ | (1) | 介电 F0 乘子 **与** 金属 F82 边色 |
| specular_roughness | [0,1] | 0.3 | 感知粗糙度,α = roughness² |
| specular_roughness_anisotropy | [0,1] | 0 | |
| specular_ior | (0,∞) | 1.5 | 介电折射率 |
| emission_luminance / emission_color | | | 走现有 scene_color 通道 |

> 注:OpenPBR 的 metal 反射**不用 specular_color 外的 IOR**:金属 F0 = base_color×base_weight,F82 tint = specular_color;specular_weight 对金属表现为缩放金属占比(`darkened_metal = metalness × specular_weight`)。这与现行"metallic 插值 dielectric_f0/base_color"的 Standard 模型有本质差别。

### 2.2 数据流:哪些是视图无关的(BasePass 预计算),哪些必须在光照 pass

参考实现的 `openpbr_prepare_lobes()` 是"prepare"+"eval"结构。逐项拆解(依据 Adobe 实现源码):

**A. 视图无关、可在 BasePass 折叠(纯数学,无 LUT):**

| 预计算项 | 公式 | 折叠后 |
|---|---|---|
| 加权 base color | `base_color × base_weight` | RGB(金属 f0、扩散 albedo 输入) |
| 有效介电折射率/F0 | `η_eff = apply_specular_weight_to_ior(specular_ior, specular_weight)`;`f0 = ((η_eff−1)/(η_eff+1))²` | 1 标量(吸收 specular_weight + specular_ior) |
| 各向异性 α 对 | `α = roughness²`;`αx = α√(2/(1+(1−aniso)²))`;`αy = (1−aniso)αx` | 2 标量(吸收 roughness + anisotropy) |
| EON 扩散粗糙度 | `base_diffuse_roughness` | 1 标量 |
| 金属占比 | `metalness × specular_weight`(darkened_metal) | 折入金属 F 计算 |
| 金属平均 Fresnel | `metal_average_fresnel_with_f82_tint(base_color_w, specular_color)`(F82 半球均值,闭式) | 可折入 MMS scale,不必存 |
| 不透明介电占比 | `opaque_dielectric = (1−trans)(1−sss)(1−metalness)`;DefaultLit 下 = (1−metalness) | 折入 albedo/scale |

**B. 视图无关、但需查 LUT 2D/1D 表(每像素一次,光照 pass 做即可):**
- `E_avg_diel = OpaqueDielectricAverageEnergyComplement(η_eff, α)`(ID 4)
- `E_avg_metal = IdealMetalAverageEnergyComplement(α)`(ID 6)
- `IdealDielectricReflectionRatio`(ID 2,DefaultLit 不需要)

**C. 视图相关、但**灯光无关**(每像素一次,光照 pass 开头算):**
- `NoV`、`V`
- 视图侧补偿(对称公式的一半):
  - 扩散:`E_diel(η_eff, α, NoV)`(ID 3)
  - 金属 MMS:`E_metal(α, NoV)`(ID 5)
- 最终公式恰好**对 V/L 对称**:`diffuse 补偿 = E(NoV)·E(NoL)/E_avg`;金属 MMS 同理。这是 deferred 化最舒服的地方。

**D. 每灯光相关(光照 pass 循环内):**
- `NoL, LoH, H`;GGX `D(αx,αy,NoH)`、Smith `V(NoV,NoL,αx,αy)`(注意:OpenPBR 的 VNDF 是各向异性 GGX)
- Fresnel:介电 = **真实 Fresnel**(`fresnel(η_eff, LoH)`,非 Schlick)× specular_color;金属 = `metal_schlick_with_f82_tint(base_color_w, specular_color, LoH)`
- 灯光侧补偿:`E_diel(η_eff, α, NoL)`(ID 3)、`E_metal(α, NoL)`(ID 5)
- EON 扩散值(`f_EON(roughness, NoV, NoL, s-term)`)+ 上述补偿

**结论:BasePass 预计算 = A 类全部(折叠后存 GBuffer)+ 可选 C 类;光照 pass 每像素一次 B/C、每灯光 D。** 由于每个灯光的额外成本只有 1-2 次 LUT 采样 + GGX/Fresnel,按现有 deferred 架构(每像素先构 `StandardSurface` 再循环灯光)即可,无需把视图侧缓存写进 GBuffer。

### 2.3 GBuffer 通道预算与推荐布局

需要持久化的视图无关量:加权 base color(3)、specular_color(3)、normal(3)、有效 f0(1)、αx(1)、αy(1)、diffuse_roughness(1)、metallic(1)、material AO(1)、shading model ID(1)= **16 通道 = 恰好 4×RGBA**。

**推荐布局(新增 1 张 RT):**

| RT | 格式 | 通道 |
|---|---|---|
| GBuffer_A | R8G8B8A8 | RGB = base_color × base_weight;A = shading model ID(沿用) |
| GBuffer_B | A2B10G10R10 | RGB = normal WS(沿用);A = base_diffuse_roughness(新,2 位不够则改 RGBA8) |
| GBuffer_C | R8G8B8A8 | R = αx,G = αy,B = metallic,A = material AO(替换原 roughness/metallic/specular/AO) |
| GBuffer_D(新) | R8G8B8A8 或 RGBA16F | RGB = specular_color;A = 有效 f0(介电) |

> 精度提示:有效 f0 范围约 [0, 0.25](η∈[1,3]),8-bit 量化误差偏大;建议 D 用 RGBA16F,或对 A 通道做非线性编码(如存 √f0,在 η 空间近似均匀)。base_color/specular_color 均 ∈[0,1],8-bit 可接受(线性空间;如追求质量可对 base_color 用 sRGB 编码)。

**3 RT 塞不下的原因:** 即使 normal 用八面体编码省 1 通道,也需 15 通道 > 12(3 RT)。要么砍 anisotropy(αx=αy=α,省 1 通道),仍要 4 张。因此 **4 RT 是保真度的下限**,代价是 GBuffer 带宽 +33%。

**v1 可裁剪项(按需):**
- anisotropy:当前 GBuffer 不存 tangent,各向异性 specular 无法计算。v1 建议 isotropic(只存 roughness,光照 pass 重算 α),后续再加 tangent 通道。
- base_diffuse_roughness:默认 0 时 EON 退化为 Lambert 近似,可先不存(恒 0)。

### 2.4 ShadingModel 与新 BRDF 文件

- `ShadingModel.hlsl`:新增 `SHADING_MODEL_OPENPBR`;`ShadingModelUsesDeferredLighting` / `HasSurfaceNormal` 加入新 ID。
- 新文件 `ShadingModelOpenPBR.hlsl`(或改写 Standard):`GBuffer2OpenPBRSurface`(每像素一次:NoV、αx/αy、η_eff、视图侧补偿、EON 参数)+ `OpenPBREvaluateBRDF`(每灯光)。
- `BRDF.hlsl`:新增 EON 扩散(`f_EON` 含多散射项)、真实 Fresnel、F82 Schlick、各向异性 GGX(D+V)。可保留原函数给 Endfield 用。

### 2.5 需要改动的 YutrelRP 文件清单

**HLSL:**
1. `Shaders/Utils/GBuffer.hlsl` — 加 `_GBuffer_D`、扩展 `GBufferData`/`EncodedGBuffer`(Encode/Decode)
2. `Shaders/Utils/ShadingModel.hlsl` — 新 ID
3. `Shaders/Utils/ShadingModelStandard.hlsl` → 新增 `ShadingModelOpenPBR.hlsl`
4. `Shaders/Utils/BRDF.hlsl` — EON/Fresnel/F82/aniso-GGX
5. `Shaders/DefaultLit.hlsl` — `RTStruct` 加第 4 输出
6. `Shaders/OpenPBR.shader` / `OpenPBRDefaultLitSurface.hlsl` — 目前只是"映射到 Standard"的占位,需改为真实 OpenPBR 参数解析(含纹理采样、base_diffuse_roughness、specular_color、specular_weight→f0 折叠)
7. `Shaders/DirectionalLightPass.hlsl`、`EnvironmentLightingPass.hlsl`、`DDGI/DDGILightingPass.hlsl` — 改用 `GBuffer2OpenPBRSurface` + 新 eval
8. `Shaders/DebugViewPass.hlsl` — 解码新 GBuffer

**C#:**
9. `Runtime/FrameData/RenderTargets.cs` — 加 `GBuffer_D` ID/handle
10. `Runtime/RenderPass/SetupPass.cs` — 创建第 4 张 GBuffer
11. `Runtime/RenderPass/BasePass.cs` — 挂第 4 个 attachment
12. 新增 OpenPBR LUT 资源加载(仿 `YutrelRPRuntimeTextures.cs`)+ 若用纹理模式需在相关 pass 绑定纹理

**RT 路径(后续):** `DefaultLitRayTracing.hlsl` 走同一 surface contract;OpenPBR 完整采样/PDF/MIS 建议直接集成 Adobe 源码(Slang 或移植),与 YutrelRender 离线端对齐。

---

## 3. 其他发现与风险

### 3.0 OpenPBR 是否规定了环境光照(IBL)算法?

**没有。OpenPBR 是 BSDF/材质模型规范,不是渲染算法规范。** 依据规范原文(`index.html`):

- 一致性定义:"Complete conformance to the specification is defined as reproducing all the physical light transport effects of that ideal appearance … The choice of the final BSDF implementation and its associated trade-off is left entirely to the implementer" —— 规范只规定**目标外观**(分层结构的 BSDF + 体积),实现方式完全自由。
- 光照连接:"We generally leave it as an implementation detail for a renderer to determine how connections to light sources be made through the surface" —— 与光源的连接(含环境光)是渲染器实现细节;规范全文唯一给出"建议"形式的只有透明阴影透射率公式。
- 环境光在 OpenPBR 语境下是**隐式**的:ground truth = 对 BSDF 做物理光传输积分 `∫ f(ωi,ωo)·Li(ωi)·cosθi dωi`,环境贴图只是 Li 的一种形式;任何能量守恒的 IBL 近似都符合规范。
- 规范确实提供了 IBL 近似所需的**数学基础**:方向反照率 E(ωo) 与分层能量守恒关系(如 `E_layer = E_coat + (1−E_coat)·E_substrate`)、glossy-diffuse 的 E 值、Furnace 测试保证,以及 "reduction to a mixture of lobes"(混合独立 lobe + MIS)作为推荐的渲染集成方式。
- 参考实现(Adobe `openpbr-bsdf`)是纯 BSDF 库:只暴露 evaluate/sample/pdf + emission + volume,无任何 IBL 代码。OpenPBR-viewer 则是"example implementation … in a WebGL pathtracer and rasterizer",其渲染(含环境光)是自己的实现选择。

**对 YutrelRP 的含义**:IBL 算法完全自主决定(预滤波 cubemap + DFG、LTC、SH 均可)。唯一注意点是近似的 IBL 不应破坏与直接光一致的材质能量语义(如 diffuse 侧应使用含 specular 能量补偿的 E_glossy-diffuse,而非直接 albedo×irradiance),否则与 YutrelRender 离线端对拍时表现不一致。

### 3.1 直接光 vs 环境光(IBL)
- 直接光:OpenPBR 自带能量补偿(LUT),**不需要** DFG 假设。
- IBL:OpenPBR 无解析可分形式;工程近似 = 现有 `_DFG_LUT` + 预滤波 cubemap,用新 f0(介电 = 有效 f0×specular_color;金属 = F82 均色)替换旧的 `lerp(0.04×specular, base_color, metallic)`;能量补偿用 OpenPBR 表。精度需与离线端对拍验证。
- 环境光 diffuse 侧建议用 `E_avg` 类因子(而非现有 DFG 的 1−F_avg),与 OpenPBR 语义一致。

### 3.2 与现行 Standard 模型的差异(渲染表现会变)
- 漫反射:Lambert → EON(Fujii Oren-Nayar,含多散射,`base_diffuse_roughness` 驱动)。
- 介电高光:Schlick → 真实 Fresnel(带 `acos/sqrt`,ALU 增加),且用 `specular_color` 做乘子而非 f0 嵌入。
- 金属:颜色=F0 的 Schlick → F82-tint 模型,掠射行为不同。
- `specular`(旧 0.08×specular 编码)被 `specular_weight`+`specular_ior` 取代,语义不同。
- 能量守恒:多次散射能量补偿由 LUT 承担,不再用 DFG 能量补偿近似。

### 3.3 视图侧补偿要不要存 GBuffer?
存(E_diel(NoV)/E_avg、E_metal(NoV)/E_metal_avg)可省每像素 4 次 LUT 采样,但要占 2 通道,且把 GBuffer 锁定到"固定视图"语义(与 SSR/后处理复用相斥)。**不建议**;LUT 采样成本远低于带宽成本。

### 3.4 语言后端是最关键的落地风险
无 HLSL 后端 → 要么手工移植(v1 建议,eval-only 约 4 个头文件),要么上 Slang(需先验证 6000.5 支持度)。LUT 数据(数组或转纹理)必须一并迁移;注意纹理模式与数组模式的 UV/轴约定不同(§1.2)。

### 3.5 验证策略
- 单元/对比:用 Adobe 的 `minimal_cpp_example.cpp`(CPU 实现)生成若干固定参数/固定 V·L 的参考值,移植后的 HLSL 用同参数输出比对(白炉、Furnace 测试)。
- 场景:现有 CornellBox / WhiteFurnace 场景可直接复用。

---

## 4. 建议的实施顺序

1. 移植 LUT 数据(生成 4 张纹理)+ HLSL 核心公式(diffuse EON、comprehensive microfacet、金属 MMS)成独立 `OpenPBRBRDF.hlsl`,先用**正向**验证(单独 shader 输出 BRDF 值)。
2. GBuffer 扩 4 RT + 新 ShadingModel,`OpenPBR.shader` 写入真实参数。
3. 直接光 pass(Directional)切到 OpenPBR eval,对拍参考值。
4. IBL 改造 + DDGI 扩散项改造。
5. (可选)anisotropy/tangent、fuzz(LTC)、RT 路径完整 OpenPBR。
