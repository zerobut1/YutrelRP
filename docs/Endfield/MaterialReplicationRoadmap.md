# 《终末地》角色材质复刻路线

## 1. 目标与范围

本文记录在 YutrelRP 中复刻《明日方舟：终末地》角色材质的总体路线，供后续 Agent 延续分析与实现。

当前首要目标是汤汤模型的 `Cloth1Alpha` 材质。参考捕获：

- RenderDoc 捕获：`D:\renderdoc\终末地\shoudun-1.rdc`
- 主要前向 Drawcall：EID 6346
- Base/GBuffer 阶段 Drawcall：EID 3049
- ShadowCaster 参考 Drawcall：EID 2167
- 疑似描边 Drawcall：EID 3182

Shader 代码统一放在：

```text
Packages/com.yutrel.render-pipelines.yutrel/Shaders/Endfield/
```

目标不是一次性翻译捕获中的完整 Shader，而是先建立正确的渲染结构，再按优先级逐项复刻基础材质、风格化光照、环境光和湿润/雨水效果。

## 2. 已确认的捕获事实

### 2.1 EID 3049：Base/GBuffer 阶段

EID 3049 与 EID 6346 使用相同的索引缓冲：

- Index Buffer：Resource 62599
- Byte Offset：14174912
- 三角形数：4576

这说明两次 Drawcall 绘制的是同一块网格。

EID 3049 的状态：

- Depth Test：反向 Z 的 `GreaterEqual`
- Depth Write：开启
- Cull：关闭
- Blend：关闭
- Stencil：开启
- 采样 BaseColor 与 BC5 Normal Map
- 使用 BaseColor Alpha 执行 `Kill/clip`
- 写入 Scene Depth 和多个 MRT

它不是完整材质光照，而是主 Base/GBuffer 阶段中的深度、法线和分类数据写入。

### 2.2 EID 6346：前向光照阶段

EID 6346 的状态：

- Depth Test：`Equal`
- Depth Write：开启
- Stencil Test：关闭
- Cull：关闭
- Blend：关闭
- MSAA：1x
- Sample Mask：`0xFFFFFFFF`
- Alpha-to-Coverage：关闭
- Depth Bounds：关闭
- Rasterizer Discard：关闭
- 不执行 Alpha Clip
- 输出两个颜色目标

主要输出：

- Color 0：`R11G11B10_FLOAT`，最终 HDR 场景颜色
- Color 1：`R10G10B10A2_UNORM`，非线性 Motion Vector 与表面/历史分类数据，不是法线缓存

因此该材质虽然名为 `Cloth1Alpha`，但不是透明混合材质，而是 Opaque Cutout：

1. Base 阶段先执行 Alpha Clip 并写深度。
2. 前向阶段使用 `ZTest Equal`，只着色此前留下的有效像素。

EID 3049 的像素着色器明确包含 `Kill()`，而 EID 6346 的像素着色器不存在 `Kill`、`discard`、`TerminateInvocation`、`Demote`、`FragDepth` 或 `SampleMask` 输出。EID 6346 的顶点着色器也不输出 `ClipDistance` 或 `CullDistance`。因此 Alpha Cutout 只由 Base 阶段建立的深度掩码负责，前向阶段不重复执行 Alpha Clip。

### 2.3 EID 2167：ShadowCaster

EID 2167 使用同一索引缓冲，主要状态为：

- Depth Write：开启
- Cull：关闭
- 开启 Depth Bias
- 采样 BaseColor Alpha
- Alpha Cutoff 约为 `0.177`

EID 3049 也执行同类 Alpha Clip，但仍需单独核对其实际阈值是否完全相同。

### 2.4 已识别的材质输入

EID 6346 使用三张主要 2048×2048 材质纹理：

- BaseColor：BC7 sRGB
- Normal：BC5 UNorm
- Packed Mask：BC7 UNorm

Packed Mask 通道已经由 Shader 运算和本地纹理匹配确认：

| 通道 | 含义 |
|---|---|
| R | Metallic |
| G | Specular Level |
| B | Material AO |
| A | Smoothness，使用时转换为 `Roughness = 1 - A` |

其他已确认资源：

- 1024×32 的 32³ Packed Color LUT；
- 256×1 Diffuse Ramp 与 256×1 Specular Ramp；
- BC6H Cubemap Specular IBL；
- R8G8 屏幕可见性：R 为主方向光 CSM，G 为次级 Shadow Atlas 可见性；
- 三组级联 3D 环境光照体积；
- 本帧启用的雨滴法线/Mask 与垂直流动法线；
- 本帧关闭的细噪声法线与彩色 Voronoi 噪声。

目前仍未确认的是三组环境体积各通道、RT1 离散类别及若干全局资源的原引擎正式命名，而不是上述功能语义。

## 3. YutrelRP 中的目标渲染结构

不新增独立的 `EndfieldPrepass` RenderGraph Pass。Endfield 材质的第一次绘制直接合并进现有 `BasePass`。

目标顺序：

```text
ShadowPass
→ BasePass
    ├─ DefaultLit/GBuffer
    └─ EndfieldBase：Depth + Normal + ForwardOnly 标记
→ ShadowMask
→ Directional Lighting
→ SSAO
→ Environment Lighting 或 DDGI
→ EndfieldForward
→ Skybox
→ Tone Mapping
```

### 3.1 扩展 BasePass

`BasePass` 的 RendererList 同时接受：

```csharp
private static readonly ShaderTagId[] shader_tag_ids =
{
    new("GBuffer"),
    new("EndfieldBase")
};
```

优先使用同一个 RendererList，使普通不透明物体和 Endfield 物体继续共享 `CommonOpaque` 排序。

`EndfieldBase` 的职责：

- Base Alpha Clip
- 写入 Scene Depth
- 采样 Normal Map，写入 `GBuffer_B`
- 将其余 GBuffer 写成不会触发延迟光照的中性值

当前输出：

```hlsl
GBufferData gbuffer      = (GBufferData)0;
gbuffer.normal_WS        = normal_WS;
gbuffer.shading_model_id = SHADING_MODEL_ENDFIELD;
EncodedGBuffer encoded   = EncodeGBuffer(gbuffer);
```

不能只写 Depth 和 Normal 而保留其他 MRT 的旧内容，否则 Endfield 表面可能继承后方物体的 GBuffer 数据。

当前 ShadingModel ID 契约定义在 `Packages/com.yutrel.render-pipelines.yutrel/Shaders/Utils/ShadingModel.hlsl`：

```hlsl
#define SHADING_MODEL_NONE      0
#define SHADING_MODEL_STANDARD  1
#define SHADING_MODEL_ENDFIELD  2
```

`GBuffer_A.a` 是 8-bit UNorm，因此 ID 使用 `id / 255.0` 编码并通过 `round(encoded * 255.0)` 解码。Endfield 实际写入 `GBuffer_A.a = 2 / 255`，而不是复用背景的 ID 0。

Directional、Environment 和 DDGI 全屏光照只处理 `SHADING_MODEL_STANDARD`，遇到 Endfield ID 时 discard。SSAO 不属于材质光照，它将 Standard 和 Endfield 都视为具有有效深度、法线的表面。

### 3.2 EndfieldForwardPass

`EndfieldForwardPass` 是独立 RasterPass，插入到 Environment Lighting/DDGI 之后、Skybox 之前。

固定状态以 EID 6346 为参考：

```text
ZTest Equal
ZWrite On
Cull Off
Blend Off
```

RenderGraph 资源：

- `scene_color`：ReadWrite，输出最终前向颜色
- `scene_depth`：ReadWrite，对应 ZTest Equal 和 ZWrite On

当前版本已经读取方向光、ShadowMask.R 和 DFG LUT，并完成以下材质及直接光照逻辑：

- BaseColor 与材质颜色；
- BC5 切线空间法线和双面法线修正；
- Packed Mask：Metallic、Specular Level、Material AO、Smoothness；
- 32³ Packed Color LUT；
- Diffuse Ramp 的双采样、Alpha 门控、饱和度与亮度保护；
- GGX 类直接镜面反射与 Specular Ramp。

当前输出仍只有方向光直接漫反射和直接镜面反射。湿润层、环境间接光等剩余差异见 3.4 节。

Forward Fragment 不执行 Alpha Clip。透明区域没有在 Base 阶段写入当前表面的深度，因此会在 `ZTest Equal` 时被拒绝。Base 与 Forward 必须保持完全一致的顶点位置计算。

前向阶段需要自行完成方向光、阴影和环境光，不能依赖此前的 Deferred Lighting，因为 Base 阶段已将该像素标记为 ForwardOnly。

### 3.3 ShadowCaster

Endfield Shader 已提供 `ShadowCaster` Pass：

- 使用与 `EndfieldBase` 一致的 Base UV 和 Alpha Clip
- Cull Off
- 使用 YutrelRP 现有 ShadowPass 和 Shadow Pancaking 约定
- 当前 Base 与 ShadowCaster 共用 `_EndfieldAlphaCutoff`，默认值为 `0.177`

现有 Shadow RendererList 会自动收集该 Pass，不需要新增 RenderGraph Pass 或修改 `ShadowPass.cs`。

### 3.4 当前实现状态

截至 2026-08-01，已完成：

- 公共 ShadingModel ID 编解码契约
- `BasePass` 接入 `EndfieldBase`
- BaseColor Alpha Clip、Depth、切线空间 Normal Map 和 Endfield ID 写入
- 双面法线、负缩放切线符号和 GPU Instancing
- `ShadowCaster` 及 Shadow Pancaking
- Endfield 法线接入 ShadowMask、SSAO 和 GBuffer World Normal Debug
- AO Debug 对 Endfield 直接显示 Screen Space AO，不读取未写入的 Material AO
- `EndfieldForwardPass` 插入 Environment/DDGI 与 Skybox 之间
- Packed Mask、32³ 材质 LUT、Diffuse Ramp 与 Specular Ramp
- 主方向光直接漫反射、直接镜面反射和 ShadowMask.R
- YutrelRP SH/DDGI 环境漫反射、Cubemap Specular IBL、环境 DFG、能量补偿与 Specular AO
- `YutrelRP/Endfield/CharacterPBR` 使用同一环境光实现，作为无 LUT/Ramp 的标准 PBR 对照组

当前实现与 EID 6346 的差异清单：

| 差异项 | EID 6346 状态 | 当前实现 | 当前范围 |
|---|---|---|---|
| 主方向光方向约定 | 实际启用；使用朝向光源方向 | 使用 YutrelRP 方向光数据，仍需按捕获约定校正和验证方向 | **关注** |
| 基础湿润/雨滴层 | 实际启用；对象强度 `215/255`，逐像素覆盖 | 未实现 | **关注** |
| 环境漫反射 | 实际启用；三级联 3D 光照体积 | Forward 已接入 YutrelRP DDGI，并在 DDGI 不可用时使用 SH | 已接入；数据布局不机械复刻 |
| 环境镜面反射与环境 BRDF | 实际启用；BC6H Cubemap、粗糙度 mip、DFG、能量补偿和遮蔽 | 已复用 YutrelRP Cubemap、DFG、能量补偿和 Specular AO | 已接入 |
| ShadowMask.G 次级可见性 | 实际启用 | ShadowMask 为单通道，Shader 中固定为 `1` | 暂不关心 |
| 光照尺度与 Pre-Exposure | 实际启用 | 直接光使用材质级参考照度归一化；物理单位的环境光单独应用 YutrelRP Pre-Exposure | 暂不统一两条光照尺度 |
| 体积雾/大气合成 | 实际启用 | 未实现 | 暂不关心 |
| Motion/History RT1 | 实际启用 | 未创建第二颜色附件，也未输出当前/上一帧运动与分类 | 暂不关心 |
| Clustered 局部光 | Shader 支持；代表像素无有效增量 | 未实现 Point/Spot、Cookie 和局部阴影 | 暂不关心 |
| 角色美术方向覆盖 | Shader 支持；本帧权重为 0 | 未实现 | 不属于 EID 6346 必需项 |
| 第二层雨水噪声/闪点 | 本帧关闭，天气参数 W 为 0 | 未实现 | 不属于 EID 6346 必需项 |
| 材质颜色校正与 Rim | 本帧关闭；全局 Rim 颜色也为 0 | 未实现 | 不属于 EID 6346 必需项 |

当前静态颜色复刻范围下一步为主光方向修正和基础湿润层。

## 4. 法线缓冲的职责

`EndfieldBase` 写入的法线不是供 `EndfieldForward` 自身计算 BRDF 使用。前向 Shader 会再次从 Normal Map 构建自己的 shading normal。

Base 阶段法线主要服务于前向光照之前的屏幕空间处理：

- ShadowMask 的表面朝向与阴影偏移
- SSAO 的采样方向和几何边界判断
- Contact Shadow
- 将来的 SSR、Decal、描边和法线边缘检测
- Debug View

EID 3049 会采样 BC5 Normal Map，因此写入的是材质 shading normal，而非单纯的顶点几何法线。

EID 6346 的 RT1 不是最终法线，而是非线性编码的 Motion Vector 与表面/历史分类。湿润覆盖会改变其 A 通道分类，但不会把湿润后的最终法线写入该 RT。

因此当前 YutrelRP 只需要继续由 Base 阶段提供屏幕空间法线。Forward 是否增加 Motion/History 输出应由 YutrelRP 后续 TAA、Reactive Mask 和动态蒙皮契约决定。

## 5. Shader 文件组织

当前已经建立：

```text
Packages/com.yutrel.render-pipelines.yutrel/Shaders/Endfield/
  EndfieldCharacter.shader
  EndfieldCharacterPBR.shader
  EndfieldCharacterInput.hlsl
  EndfieldCharacterSurface.hlsl
  EndfieldCharacterEnvironment.hlsl
  EndfieldCharacterBasePass.hlsl
  EndfieldCharacterForwardPass.hlsl
  EndfieldCharacterPBRForwardPass.hlsl
  EndfieldCharacterShadowCasterPass.hlsl
```

公共环境光和 DDGI 逐表面采样分别位于 `Shader/EnvironmentLighting.hlsl` 与
`Shader/DDGI/DDGILighting.hlsl`。全屏延迟光照和 Endfield Forward 共用这些实现。

Shader 名称为 `YutrelRP/Endfield/Character`，材质属性使用 `_Endfield` 前缀，HLSL 类型和函数使用 `EndfieldCharacter` 前缀。当前公开材质输入为：

- `_EndfieldBaseMap`
- `_EndfieldBaseColor`
- `_EndfieldNormalMap`
- `_EndfieldNormalScale`
- `_EndfieldPackedMap`
- `_EndfieldColorLUT`
- `_EndfieldDiffuseRamp`
- `_EndfieldDiffuseRampOffset`
- `_EndfieldSpecularRamp`
- `_EndfieldAlphaCutoff`
- `_EndfieldDirectIntensity`
- `_EndfieldReferenceIlluminance`

实现光照和湿润功能时按需增加：

```text
  EndfieldCharacterLighting.hlsl
  EndfieldWetness.hlsl
  EndfieldRain.hlsl
  EndfieldColorGrading.hlsl
```

运行时 Pass 已建立在：

```text
Packages/com.yutrel.render-pipelines.yutrel/Runtime/RenderPass/EndfieldForwardPass.cs
```

基础阶段优先复用 YutrelRP 已有的变换、BRDF、灯光、阴影和 IBL 公共代码，避免在没有对照基线前同时重写材质模型和渲染接入。

## 6. 分阶段实现路线

### 阶段 1：渲染结构闭环

目标：证明 Base + Forward 双绘制结构正确。

实现：

- [x] `BasePass` 支持 `EndfieldBase`
- [x] Alpha Clip、Depth、Normal、Endfield ShadingModel ID
- [x] ShadowCaster
- [x] SSAO、ShadowMask 和 World Normal Debug 使用 Endfield Base 数据
- [x] `EndfieldForwardPass`
- [x] 前向阶段直接输出 BaseColor

验收：

- 模型不会被 Deferred Lighting 重复照亮
- 遮挡关系正确
- Alpha Cutout 轮廓在主画面和阴影中一致
- GBuffer Normal Debug 正确

阶段 1 的实现已闭环。

### 阶段 2：基础材质与光照

实现：

- [x] BaseColor
- [x] Tangent Space Normal
- [x] Packed Mask 解码
- [x] 方向光直接光照
- [x] ShadowMask.R
- [x] Cubemap IBL 与环境 DFG
- [x] 环境光中的 Material AO
- [ ] 统一 Pre-Exposure（环境光已预曝光；直接光仍使用参考照度归一化）

这一阶段先使用 YutrelRP Standard BRDF，暂不实现雨水和复杂风格化逻辑。

验收：

- 金属/非金属区域正确
- 粗糙度和高光形状合理
- 方向光、阴影、环境光与 YutrelRP 曝光系统一致
- 能分别 Debug BaseColor、Normal、Metallic、Roughness、AO

### 阶段 3：基础风格化匹配

实现顺序：

1. [x] 32³ Packed Color LUT
2. [x] Diffuse Ramp
3. [x] Specular Ramp 与高光整形
4. [ ] 主方向光方向约定校正
5. Rim/颜色校正：EID 6346 对应分支关闭，暂不实现

先将干燥状态匹配到可接受程度，再进入湿润效果。

### 阶段 4：环境光与屏幕空间数据

按优先级实现：

1. [x] YutrelRP 环境漫反射：SH 或 DDGI
2. [x] Cubemap Specular IBL
3. [x] 环境 DFG、多重散射能量补偿和 Specular AO
4. ShadowMask.G、SSAO/Contact Shadow 当前不在复刻范围
5. 捕获中的 3D Probe/Volume Lighting 只作为功能参考，不机械复制纹理布局

EndfieldForward 的环境光选择规则：有效 DDGI 只替换漫反射来源；DDGI 关闭或资源无效时回退 SH；
Cubemap 镜面反射始终保留。DDGI 体积外按当前 YutrelRP Volume Weight 衰减至零，不混入 SH。
Diffuse/Specular Ramp 只处理方向光，环境光不经过 Ramp。捕获中的 Irradiance Volume Clipmap 只作为功能参考，
没有复制其 A/B 纹理布局。

### 阶段 5：湿润与雨水

EID 6346 实际启用的基础层：

1. 对象级 Wetness 强度
2. 世界空间、逐像素雨滴覆盖
3. BaseColor 修改
4. Roughness 向 `min(DryRoughness, 0.05)` 收敛
5. 三平面雨滴 Normal
6. 垂直流水法线与时间动画

第二层细噪声、Voronoi 和闪点高光在 EID 6346 中关闭，不属于当前范围。

湿润模块必须可单独关闭，并提供分层 Debug 输出，避免同时调试过多变量。

### 阶段 6：描边和附加角色效果

EID 3182 疑似使用 Front Cull 的描边绘制，应作为独立功能处理，不要混入 EID 6346 的基础前向光照。

其他附加功能也应独立验证：

- 描边
- 头发特殊高光
- 皮肤/布料差异
- 透明或半透明部件
- 动态表情和特殊遮罩

### 阶段 7：性能与工程化

- 控制 Shader Variant 数量
- 统一纹理通道和导入设置
- 将全局雨水资源与材质资源分离
- 避免所有角色无条件执行高成本雨滴逻辑
- 为捕获对照保留 Debug Keyword/Debug View
- 使用 GPU Capture 对比纹理采样数、分支和输出状态

## 7. 推荐的调试方式

每个阶段都应能独立观察：

- BaseColor
- World Normal
- Packed Mask 各通道
- Direct Diffuse
- Direct Specular
- IBL Diffuse/Specular
- ShadowMask
- SSAO/Contact Shadow
- Wetness Mask
- Rain Normal
- 最终 Pre-Exposed Color

使用固定相机、固定环境光和固定曝光进行对比，避免一边修改 Shader，一边改变展示场景光照。

## 8. 尚未解决的问题

- EID 3049 五个 MRT 的精确语义和各通道编码
- EID 3049 的实际 Alpha Cutoff 是否与 EID 2167 完全一致
- RT1 A/B 离散类别的原引擎正式枚举名
- ShadowMask.G 与次级 Shadow Atlas 的原引擎正式命名
- 三组 3D 光照体积每个通道的正式语义，以及与 Cubemap 的组合细节
- 全局雨水纹理的原始资源名与来源
- 雨滴、流水和闪烁高光的独立开关与参数范围
- 角色覆盖光方向的 CPU 更新规则
- 捕获曝光标量与原引擎 Pre-Exposure 的完整契约
- EID 6346 全部像素中的 Clustered 局部光覆盖范围
- 原游戏 Stencil 分类在 YutrelRP 中是否需要等价实现

在实现相关功能前，应先针对对应问题补充 RenderDoc 证据，不要仅凭纹理外观命名。

## 9. RenderDoc 分析交接

使用 rdc-cli 前先阅读：

```text
D:\Project\rdc-cli\src\rdc\_skills\SKILL.md
```

基本流程：

```powershell
rdc doctor
rdc --session endfield open "D:\renderdoc\终末地\shoudun-1.rdc"
rdc --session endfield draw 3049 --json
rdc --session endfield pipeline 3049 --json
rdc --session endfield bindings 3049 --json
rdc --session endfield shader 3049 ps --reflect --json
rdc --session endfield draw 6346 --json
rdc --session endfield descriptors 6346 --json
rdc --session endfield close
```

分析新捕获时不要假设 EID 保持不变，应通过三角形数量、Index Buffer、Byte Offset、纹理资源和 Shader 状态重新确认 Drawcall 身份。

## 10. 项目约束

- Unity 版本：6000.5
- 使用 YutrelRP 自定义延迟渲染管线
- RenderGraph 只能使用 RasterPass 或 ComputePass
- 不允许为该功能引入 UnsafePass
- 修改 C#、asmdef、Editor 或 Package 后至少运行：

```powershell
python tools\agent_harness.py compile
```

- 修改 `Packages/com.yutrel.render-pipelines.yutrel/Shaders` 下的 HLSL/HLSLI 后必须运行：

```powershell
python tools\agent_harness.py shader-format
python tools\agent_harness.py compile
```
