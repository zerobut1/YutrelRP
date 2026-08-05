# OpenPBR DefaultLit — DeferredRenderer 实现方案

> 前置:可行性调研见 `docs/OpenPBR/OpenPBR_DefaultLit_Feasibility.md`。
> **对齐目标 = YutrelRender `src/core/surfaces/openpbr.cpp`(775 行)的 eval**,不是 Adobe 完整实现。两者已用 Adobe golden 数据 + 白炉验证对齐(YutrelRender `test/data/openpbr_adobe_8a20d6f9.h`)。
> 范围:BasePass + DirectionalLighting。IBL/DDGI 暂不做(见 §6)。

---

## 0. 对齐目标:YutrelRender 的 eval 公式全集(移植依据)

以下是从 `openpbr.cpp` 提取的**完整公式集**,deferred 侧实现必须数值一致。全部计算可在**世界空间**完成(v1 isotropic,见 §2.4,无需 TBN)。

### 0.1 材质参数 → 派生量(populate_closure,视图无关)

```
weighted_base_color = base_color * base_weight
diffuse_albedo      = weighted_base_color * (1 - metalness)
alpha               = max(roughness², 1e-6)              // roughness = specular_roughness(感知)
relative_ior        = specular_ior / eta_ambient          // deferred 外部介质 = 空气, eta_ambient = 1
f0                  = ((relative_ior-1)/(relative_ior+1))²
weighted_f0         = min(specular_weight * f0, 0.9999)
weighted_ior        = (relative_ior < 1) ? 1/external_weighted_ior : external_weighted_ior
                      // external_weighted_ior = (1+√weighted_f0)/(1−√weighted_f0)
metal_average       = MetalAverageFresnel(weighted_base_color, specular_color)   // F82 半球均值, RGB
metal_mms_scale     = metal_average² * (metalness * specular_weight)             // RGB
```

### 0.2 视图侧缓存(每像素一次,与灯光无关)

```
NoV        = max(dot(N, V), 0)
dielectric_view  = E_diel_3D(weighted_ior, alpha, NoV)            // LUT 3D + ior 外推
dielectric_avg   = E_diel_2D(weighted_ior, alpha)                 // LUT 2D + ior 外推
dielectric_view_compensation = dielectric_view / max(dielectric_avg, 1e-12)
metal_view       = E_metal_2D(alpha, NoV)                         // LUT 2D
metal_avg        = E_metal_1D(alpha)                              // LUT 1D
```

### 0.3 每灯光 eval(evaluate_impl;注意 f 已含 cosθ,勿再乘 NoL)

```
if NoV <= 0 or NoL <= 0: return 0
wh = normalize(V + L);  LoH = |dot(V, wh)|

// --- 高光 lobe(GGX + 综合 Fresnel)---
fresnel = specular_color * (1-metalness) * fresnel_dielectric(LoH, 1, weighted_ior)
        + metal_f82(weighted_base_color, specular_color, LoH) * (metalness * specular_weight)
specular_f_cos = fresnel * D_GGX(wh, α) * G1_GGX(NoV, α) * G1_GGX(NoL, α) / (4 * NoV)

// --- 金属多次散射 MMS lobe(α >= 0.0016 才生效,即 roughness >= 0.04)---
metal_light = E_metal_2D(alpha, NoL)
metal_factors = metal_view * metal_light / max(metal_avg, 1e-12)
metal_factors = min(metal_factors, 1 / NoL)
metal_f_cos   = metal_mms_scale * metal_factors * (1/π) * NoL

// --- 扩散 lobe(EON 能量守恒型,Oren-Nayar)---
dielectric_light = E_diel_3D(weighted_ior, alpha, NoL)
diffuse_factor   = dielectric_view_compensation * dielectric_light
diffuse_f_cos    = EON(diffuse_albedo, diffuse_roughness, NoV, NoL, dot(V,L)) * diffuse_factor * NoL

f_total = specular_f_cos + metal_f_cos + diffuse_f_cos
// 直接光: out = f_total * light.color * light.illuminance * light.occlusion(shadow)
```

### 0.4 子函数定义(与 YutrelRender 逐符号一致)

**fresnel_dielectric(真实非偏振 Fresnel,含 TIR):**
```
cos_i = clamp(cos, -1, 1);  entering = cos_i > 0
η_i = entering ? η_i : η_t;  η_t = entering ? η_t : η_i;  cos_i = |cos_i|
sinθ_i = sqrt(max(0, 1 - cos_i²));  sinθ_t = (η_i/η_t) * sinθ_i;  cosθ_t = sqrt(max(0, 1 - sinθ_t²))
r_par = (η_t cosθ_i - η_i cosθ_t) / (η_t cosθ_i + η_i cosθ_t)
r_perp = (η_i cosθ_i - η_t cosθ_t) / (η_i cosθ_i + η_t cosθ_t)
return (sinθ_t < 1) ? 0.5*(r_par² + r_perp²) : 1.0      // TIR → 1
```

**metal_f82(F82-tint Schlick):**
```
b = (f0 + (1-f0)(1-cosθmax)⁵) * (1 - f82_tint) / (cosθmax * (1-cosθmax)⁶),  cosθmax = 1/7
return saturate(f0 + ((1-f0) - b*cosθ*(1-cosθ)) * (1-cosθ)⁵)
```

**MetalAverageFresnel(F82 半球均值,闭式):**
```
b = 同上
return saturate(f0 + (1-f0)*(1/21) - b*(1/126))
```

**D_GGX(各向异性,isotropic 时 αx=αy=α):**
```
tan²θh = (1 - NoH²) / NoH²;  cos⁴θh = NoH⁴
e = tan²θh * (cos²φ/αx² + sin²φ/αy²)      // αx=αy → e = tan²θh / α²
D = 1 / (π αx αy cos⁴θh (1+e)²)
```

**G1_GGX(Smith 遮蔽,slope 公式):**
```
slope² = ((αx·wx)² + (αy·wy)²) / wz²        // isotropic: α² tan²θw
G1 = 2 / (1 + sqrt(1 + slope²))
```

**EON 扩散(Fujii Oren-Nayar,能量守恒型,含多散射;basis-free 形式):**
```
const A = 0.5 - 2/(3π);  B = 2/3 - 28/(15π)
const g1=0.0571085289, g2=0.491881867, g3=-0.332181442, g4=0.0714429953
mu_o = NoV;  mu_i = NoL;  s = dot(V,L) - mu_o*mu_i;  sovertF = s>0 ? s/max(mu_i,mu_o) : s
a      = 1/(1 + A*roughness)
f_ss   = rho * (1/π) * a * (1 + roughness*sovertF)
E(mu)  = (1 + roughness*G(mu)/π) / (1 + A*roughness),  G(mu)/π = (1-mu)(g1 + (1-mu)(g2 + (1-mu)(g3 + (1-mu)g4)))
e_o = E(mu_o);  e_i = E(mu_i);  e_avg = a * (1 + B*roughness)
rho_ms = rho² * e_avg / (1 - rho*(1 - e_avg))
f_ms   = rho_ms * (1/π) * max(1e-7, 1-e_o) * max(1e-7, 1-e_i) / max(1e-7, 1-e_avg)
return f_ss + f_ms
```

### 0.5 LUT 访问(与 YutrelRender/Adobe 相同的约定)

```
remap(idx) = clamp((idx + 0.5) / 32, 0.5/32, 31.5/32)     // texel-center 重映射
ior_idx(ior)  = ior < 1 ? 15 - (1/ior - 1)*(15/1.5) : 16 + (ior - 1)*(15/1.5)
alpha_idx(α)  = sqrt(α) * 31
cos_idx(cos)  = cos * 31
ior 外推(仅 opaque 两张表): ior>2.5 或 ior<0.4 时,
  f0max = f0(2.5)=0.1837, progress=(f0- f0max)/(1-f0max), value *= (1-progress)
```

**纹理采样映射(HLSL):**
```
E_diel_3D(ior, α, cos):  SAMPLE_TEXTURE3D(LUT, uvw = float3(remap(cos*31), remap(α_idx), remap(ior_idx))).r  [轴: x=cos, y=α, z=ior]
E_diel_2D(ior, α):       SAMPLE_TEXTURE2D(LUT, uv  = float2(remap(α_idx), remap(ior_idx))).r                    [x=α, y=ior]
E_metal_2D(α, cos):      SAMPLE_TEXTURE2D(LUT, uv  = float2(remap(α_idx), remap(cos*31))).r                    [x=α, y=cos]
E_metal_1D(α):           SAMPLE_TEXTURE2D(LUT, uv  = float2(remap(α_idx), 0.5)).r                               [32×1]
```
全部 Linear 过滤 + Clamp。**数据直接顺序拷贝**(无转置,见 §3.2)。

---

## 1. BasePass 设计

### 1.1 输入参数(材质属性)

OpenPBR 9 参数(与 `OpenPBRMaterialData` / 现有 `OpenPBR.shader` 属性一一对应)+ 2 个渲染器侧输入:

| 参数 | 来源 | 说明 |
|---|---|---|
| base_weight | 材质 | [0,1],折叠进 base_color |
| base_color | 材质/纹理 | sRGB → 线性 |
| base_metalness | 材质/纹理 | [0,1] |
| base_diffuse_roughness | 材质/纹理 | [0,1],EON 扩散粗糙度 |
| specular_weight | 材质/纹理 | ≥0 |
| specular_color | 材质/纹理 | [0,1]³ |
| specular_roughness | 材质/纹理 | [0,1],感知粗糙度 |
| specular_roughness_anisotropy | 材质/纹理 | **v1 忽略**(各向异性,见 §2.4) |
| specular_ior | 材质/纹理 | >0,默认 1.5 |
| material AO | 渲染器 | 沿用现有 |
| (emission) | 材质/纹理 | 走现有 scene_color,不改 |

### 1.2 BasePass 预计算(折叠,省 GBuffer 通道)

| 折叠项 | 公式 | 存入 | 节省 |
|---|---|---|---|
| base_weight × base_color | `weighted_base_color = base_color * base_weight` | A.rgb | 1 标量 |
| specular_weight × specular_ior | `weighted_f0 = min(specular_weight * f0(specular_ior), 0.9999)` | D.a(存 √weighted_f0) | 1 标量 |
| (metalness, specular_weight) | 原样存储 | C.g/C.b | ——(light pass 现算 1−m 与 m·w) |
| roughness | 原样存储(感知值,便于调试) | C.r | ——(light pass 算 α=roughness²) |

**明确不预计算/不存进 GBuffer 的:**
- 视图侧 LUT 缓存(`dielectric_view_compensation`、`metal_view`、`metal_avg`):每像素 4 次 LUT 采样,存需 2-3 通道,不划算 → light pass 每像素算一次(灯光无关,可摊薄)。
- `metal_average` / `metal_mms_scale`:闭式 ~10 ALU,light pass 现算。
- 各向异性 αx/αy 与 tangent: v1 不存。

### 1.3 GBuffer 布局(3 RT → 4 RT,全部 RGBA8)

| RT | 格式 | R | G | B | A |
|---|---|---|---|---|---|
| scene_color | RGBA16F | emissive(预曝光) | 同 | 同 | 0(沿用) |
| GBuffer_A | R8G8B8A8 | weighted_base_color.r | .g | .b | shading model ID |
| GBuffer_B | R8G8B8A8 | normal_WS.r(0.5N+0.5) | .g | .b | base_diffuse_roughness |
| GBuffer_C | R8G8B8A8 | specular_roughness | base_metalness | specular_weight | material_AO |
| GBuffer_D(**新**) | R8G8B8A8 | specular_color.r | .g | .b | √(weighted_f0) |

- 16 通道恰好装满;`√weighted_f0` 编码精度优于线性(√f0∈[0,1),解码 `weighted_ior = (1+√f0)/(1−√f0)`,免开方)。
- B 由 A2B10G10R10 改为 RGBA8(normal 精度 10→8 位,换取 diffuse_roughness 通道;如在意,后续可改八面体编码 RG+2 通道)。
- 新增 `SHADING_MODEL_OPENPBR`(ShadingModel.hlsl),Endfield 等现有模型不受影响。

### 1.4 简化决策

1. **v1 isotropic**:`specular_roughness_anisotropy` 暂不支持(OpenPBR.shader 保留属性,写入时若 >0 打警告)。各向异性需要 tangent 进 GBuffer,列为 v2。
2. **世界空间计算**:EON、GGX D/G1 在 isotropic 下都是 basis-free(只依赖 NoV/NoL/NoH/dot(V,L)),light pass 无需构造 TBN。
3. **无 dielectric MMS / coat / fuzz / transmission**:DefaultLit 范围外,权重为 0 时自然不贡献。

---

## 2. DirectionalLighting 设计

### 2.1 每像素一次(灯光无关,`GBuffer2OpenPBRSurface`)

```
解码: weighted_base_color, specular_color, normal, roughness, metalness, specular_weight,
      diffuse_roughness, √weighted_f0, AO, ID
position_WS  = ComputeWorldSpacePositionFromFullScreenUV(uv, depth)
V            = GetWorldSpaceViewDirectionForSurface(position_WS)
NoV          = max(dot(N, V), 0)
alpha        = max(roughness*roughness, 1e-6)
weighted_ior = (1 + sqrt_f0) / (1 - sqrt_f0)          // 暂不处理 ior<1 内反射(边缘情况,可加 ite)
dielectricness = 1 - metalness
darkened_metal = metalness * specular_weight
diffuse_albedo = weighted_base_color * dielectricness
metal_average  = MetalAverageFresnel(weighted_base_color, specular_color)   // RGB,闭式
metal_mms_scale = metal_average * metal_average * darkened_metal            // RGB
// LUT 视图侧缓存(4 次采样):
dielectric_view_compensation = E_diel_3D(ior,α,NoV) / max(E_diel_2D(ior,α), 1e-12)
metal_view = E_metal_2D(α, NoV);  metal_avg = E_metal_1D(α)
```

### 2.2 每灯光(`OpenPBREvaluateBRDF`,§0.3 公式)

```
NoL = dot(N, L); if (NoL <= 0) return 0;
wh  = normalize(V + L);  NoH = saturate(dot(N,wh));  LoH = saturate(dot(L,wh));
fresnel = specular_color*dielectricness*FresnelDielectric(LoH, 1, weighted_ior)
        + MetalF82(weighted_base_color, specular_color, LoH)*darkened_metal
spec    = fresnel * D_GGX(α,NoH) * G1(α,NoV) * G1(α,NoL) / (4*NoV)
// 金属 MMS:
metal_factors = metal_view * E_metal_2D(α,NoL) / max(metal_avg, 1e-12)
metal_factors = min(metal_factors, 1/max(NoL,1e-12))
metal_ms = (α >= 0.0016) ? metal_mms_scale * metal_factors * (1/π) * NoL : 0
// 扩散:
diffuse_factor = dielectric_view_compensation * E_diel_3D(ior,α,NoL)
diffuse = EON(diffuse_albedo, diffuse_roughness, NoV, NoL, saturate(dot(V,L))) * diffuse_factor * NoL
f = spec + metal_ms + diffuse
return f * light.color * light.illuminance * light.occlusion     // ⚠ 不再乘 NoL(已含)
```

> ⚠ 与现有 `StandardShading` 的关键差异:`OpenPBR f` 已含 cosθ(wi.z),光照 pass **不要**再乘 NoL,否则双倍衰减。

### 2.3 外部数据清单 + Unity 导入

| 数据 | 是否需要新增 | Unity 导入方式 |
|---|---|---|
| **4 张 LUT 纹理**(必需) | ✅ 新增 | 见下 |
| 光源方向/颜色/照度/阴影 | 已有 | `GetDirectionalLight(_LightIndex, uv)`(Light.hlsl) |
| 相机/预曝光/位置重建 | 已有 | 现有 pipeline |
| DFG LUT | **不需要**(直接光不用) | —— |
| tangent/bitangent | 不需要(v1 isotropic) | —— |

**LUT 导入(重点):**
1. **数据源**:YutrelRender `src/core/surfaces/openpbr_data/*.h`(uint16 数组,0..65535,Adobe commit 8a20d6f9,Apache-2.0)—— 与 Adobe 参考一致且已通过 golden 验证。4 个文件:
   - `openpbr_opaque_dielectric_energy_complement_data.h`(32768 个 ushort → Texture3D 32³)
   - `openpbr_opaque_dielectric_avg_energy_complement_data.h`(1024 → Texture2D 32²)
   - `openpbr_ideal_metal_energy_complement_data.h`(1024 → Texture2D 32²)
   - `openpbr_ideal_metal_avg_energy_complement_data.h`(32 → Texture2D 32×1)
2. **转换脚本**:一次性把 4 个 .h 的数值段转成 C# `static readonly ushort[]`(如 `Runtime/OpenPBR/OpenPBRLUTData.cs`),保留 Apache-2.0 版权头。
3. **运行时建纹理**:`Runtime/OpenPBR/OpenPBRLUTs.cs`,静态懒加载:
   ```
   tex3D = new Texture3D(32,32,32, TextureFormat.R16, false);  tex3D.SetPixelData(data);  tex3D.Apply(false,true);
   // filterMode = Bilinear, wrapMode = Clamp; 同样建 3 张 Texture2D(32², 32², 32×1)
   // Shader.SetGlobalTexture("_OpenPBR_OpaqueDielectricEnergy", tex3D) × 4
   ```
   **数据直接顺序拷贝,无需转置**(Unity 3D 纹理线性序 = z*1024+y*32+x,恰好 = Adobe 的 [ior][α][cos] 展平序;2D = y*32+x 恰好 = [x][y] 展平序)。全局绑定一次即可(光照 pass 共享)。
4. **HLSL 侧**(`Shaders/Utils/OpenPBR.hlsl`):
   ```
   TEXTURE3D(_OpenPBR_OpaqueDielectricEnergy);   SAMPLER(sampler_...);
   TEXTURE2D(_OpenPBR_OpaqueDielectricAverage); TEXTURE2D(_OpenPBR_IdealMetalEnergy); TEXTURE2D(_OpenPBR_IdealMetalAverage);
   // 采样映射严格按 §0.5
   ```

### 2.4 Shader 文件组织

- 新建 `Shaders/Utils/OpenPBR.hlsl`:常量 + remap/索引函数 + 4 LUT 声明/采样 + `FresnelDielectric` / `MetalF82` / `MetalAverageFresnel` / `D_GGX` / `G1_GGX` / `EON`(§0 全部子函数,与 YutrelRender 逐符号一致)。
- 新建 `Shaders/Utils/ShadingModelOpenPBR.hlsl`:`OpenPBRSurface` 结构 + `GBuffer2OpenPBRSurface`(§2.1)+ `OpenPBREvaluateBRDF`(§2.2)。
- 保留原 `ShadingModelStandard.hlsl`(Endfield/兼容路径)。

---

## 3. 改动文件清单

**HLSL**
| 文件 | 改动 |
|---|---|
| `Shaders/Utils/OpenPBR.hlsl`(新) | §0 全部函数 + LUT |
| `Shaders/Utils/ShadingModelOpenPBR.hlsl`(新) | 表面构建 + eval |
| `Shaders/Utils/GBuffer.hlsl` | 加 `_GBuffer_D`;GBufferData/EncodedGBuffer 扩展(解码新增字段) |
| `Shaders/Utils/ShadingModel.hlsl` | `SHADING_MODEL_OPENPBR`;`UsesDeferredLighting`/`HasSurfaceNormal` 加入 |
| `Shaders/DefaultLit.hlsl` | RTStruct 加 `SV_Target4` |
| `Shaders/OpenPBRDefaultLitSurface.hlsl` | 真实参数解析:纹理采样、weighted_f0 折叠、diffuse_roughness、specular_color/weight |
| `Shaders/DirectionalLightPass.hlsl` | `case SHADING_MODEL_OPENPBR:` → 新 eval |
| `Shaders/DebugViewPass.hlsl` | 新通道 debug 视图(可选) |

**C#**
| 文件 | 改动 |
|---|---|
| `Runtime/OpenPBR/OpenPBRLUTData.cs`(新) | 4 组 ushort 数据(从 YutrelRender 拷贝) |
| `Runtime/OpenPBR/OpenPBRLUTs.cs`(新) | 建纹理 + SetGlobalTexture |
| `Runtime/FrameData/RenderTargets.cs` | `GBuffer_D` ID + handle |
| `Runtime/RenderPass/SetupPass.cs` | 创建第 4 张 GBuffer;调用 LUT 初始化 |
| `Runtime/RenderPass/BasePass.cs` | `SetRenderAttachment(textures.GBuffer_D, 4)` |
| `Runtime/RenderPass/DirectionalLightPass.cs` | 绑定 `_GBuffer_D`(如需) |

---

## 4. 验证方案

1. **白炉**:WhiteFurnace 场景,roughness ∈ {0.05, 0.2, 0.5, 0.8} × metalness ∈ {0, 0.5, 1} × ior ∈ {1.3, 1.5, 2.2} 网格,deferred 输出 vs YutrelRender 输出(可利用其 `tools/openpbr_reference/validate_white_furnace.py` 思路)。
2. **CornellBox 对拍**:同场景同材质,直接光部分逐像素比较(允许 IBL 差异,先关环境光只留 directional)。
3. **固定角度数值抽检**:把 §0 公式镜像为 C#(或临时 compute shader dump),与 `openpbr_adobe_8a20d6f9.h` golden 数值逐项比对(误差 < 1e-4)。
4. **LUT 一致性**:Unity 纹理采样值 vs YutrelRender 同一索引采样值(可在 Editor 测试中读回 GPU 纹理抽样验证)。

---

## 5. 已知取舍

- GBuffer 带宽 +33%(3→4 RT);normal 精度 10→8 bit(v1)。
- light pass ALU 增加:EON(≈30 ALU)+ 真实 Fresnel(sqrt/acos 级)+ 2 次 LUT 采样/灯。
- 各向异性、coat/fuzz/transmission、IBL/DDGI 均未覆盖,均为后续增量。
- `specular_roughness_anisotropy > 0` 的材质当前被忽略(建议 v1 打警告)。

---

## 6. 后续(不在本次范围)

- IBL:DFG + 预滤波 cubemap 复用,`√weighted_f0` 已为 DFG 备好;语义对齐用 `E_glossy-diffuse` 类因子(见可行性报告 §3.0/§3.1)。
- DDGI 扩散项:EON × dielectric_view_compensation 近似。
- 各向异性:GBuffer 加 tangent;D/G1 换 §0.4 各向异性版。
- RT 路径:DefaultLitRayTracing 集成完整 OpenPBR(采样/MIS)——可直连 YutrelRender 同源实现。
