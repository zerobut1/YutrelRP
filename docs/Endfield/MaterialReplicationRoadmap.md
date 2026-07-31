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
Assets/YutrelRP/Shader/Endfield/
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
- Cull：关闭
- Blend：关闭
- MSAA：1x
- 不执行 Alpha Clip
- 输出两个颜色目标

主要输出：

- Color 0：`R11G11B10_FLOAT`，最终 HDR 场景颜色
- Color 1：`R10G10B10A2_UNORM`，Packed Normal/辅助法线数据

因此该材质虽然名为 `Cloth1Alpha`，但不是透明混合材质，而是 Opaque Cutout：

1. Base 阶段先执行 Alpha Clip 并写深度。
2. 前向阶段使用 `ZTest Equal`，只着色此前留下的有效像素。

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

Packed Mask 通道根据 Shader 运算可基本确定为：

| 通道 | 含义 |
|---|---|
| R | Metallic |
| G | Specular Level |
| B | Material AO |
| A | Smoothness，使用时转换为 `Roughness = 1 - A` |

其他已观察资源：

- 1024×32 的 32³ Packed Color LUT
- 两张 256×1 Ramp/LUT
- BC6 Cubemap
- 方向光阴影贴图
- R8G8 屏幕空间遮蔽/Contact Shadow 数据
- 多张 3D 体积光照纹理
- 全局雨水法线/噪声候选纹理
- 蓝噪声候选纹理

这些资源的精确语义需要在对应实现阶段继续通过 Shader 反汇编、资源使用和 Pixel Debug 验证。

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
private static readonly ShaderTagId[] shaderTagIds =
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

建议输出：

```hlsl
sceneColor = 0;
GBufferA   = 0; // ShadingModelID = 0
GBufferB   = EncodeNormal(normalWS);
GBufferC   = 0;
```

不能只写 Depth 和 Normal 而保留其他 MRT 的旧内容，否则 Endfield 表面可能继承后方物体的 GBuffer 数据。

当前 YutrelRP 的延迟光照只处理 `ShadingModelID == 1`，因此 `GBuffer_A.a = 0` 可以作为最初的 ForwardOnly 标记，使延迟光照在这些像素上 discard。

### 3.2 新增 EndfieldForwardPass

`EndfieldForwardPass` 是需要新增的独立 RasterPass，插入到 Environment Lighting/DDGI 之后、Skybox 之前。

固定状态以 EID 6346 为参考：

```text
ZTest Equal
ZWrite On
Cull Off
Blend Off
```

RenderGraph 资源：

- `scene_color`：ReadWrite，输出最终前向颜色
- `scene_depth`：Read 或 ReadWrite；严格复刻时对应 ZWrite On
- `GBuffer_B`：可选 ReadWrite，输出最终材质法线
- ShadowMask/SSAO/环境光/DDGI 资源：按当前阶段声明只读依赖

前向阶段需要自行完成方向光、阴影和环境光，不能依赖此前的 Deferred Lighting，因为 Base 阶段已将该像素标记为 ForwardOnly。

### 3.3 ShadowCaster

Endfield Shader 必须提供 `ShadowCaster` Pass：

- 使用与 `EndfieldBase` 一致的 Base UV 和 Alpha Clip
- Cull Off
- 使用 YutrelRP 现有 ShadowPass 和 Shadow Pancaking 约定

否则主画面轮廓和阴影轮廓会不一致。

## 4. 法线缓冲的职责

`EndfieldBase` 写入的法线不是供 `EndfieldForward` 自身计算 BRDF 使用。前向 Shader 会再次从 Normal Map 构建自己的 shading normal。

Base 阶段法线主要服务于前向光照之前的屏幕空间处理：

- ShadowMask 的表面朝向与阴影偏移
- SSAO 的采样方向和几何边界判断
- Contact Shadow
- 将来的 SSR、Decal、描边和法线边缘检测
- Debug View

EID 3049 会采样 BC5 Normal Map，因此写入的是材质 shading normal，而非单纯的顶点几何法线。

EID 6346 又写出一次法线，可能是包含湿润、雨滴等扰动后的最终法线，供前向阶段之后的屏幕空间效果使用。

当前 YutrelRP 尚无 SSR 等后续效果时：

- Base 阶段法线是必要的。
- Forward 阶段再次写法线可以延后，但最终应保留这一能力。

## 5. Shader 文件组织

初始建议：

```text
Assets/YutrelRP/Shader/Endfield/
  EndfieldCharacter.shader
  EndfieldMaterialInput.hlsl
  EndfieldSurface.hlsl
  EndfieldBasePass.hlsl
  EndfieldForwardPass.hlsl
  EndfieldLighting.hlsl
  EndfieldShadowCasterPass.hlsl
```

湿润功能开始增长后再增加：

```text
  EndfieldWetness.hlsl
  EndfieldRain.hlsl
  EndfieldColorGrading.hlsl
```

运行时 Pass 建议放在：

```text
Assets/YutrelRP/Runtime/RenderPass/EndfieldForwardPass.cs
```

基础阶段优先复用 YutrelRP 已有的变换、BRDF、灯光、阴影和 IBL 公共代码，避免在没有对照基线前同时重写材质模型和渲染接入。

## 6. 分阶段实现路线

### 阶段 1：渲染结构闭环

目标：证明 Base + Forward 双绘制结构正确。

实现：

- `BasePass` 支持 `EndfieldBase`
- Alpha Clip、Depth、Normal、ForwardOnly 标记
- `EndfieldForwardPass`
- 先输出固定颜色或简单 BaseColor
- ShadowCaster

验收：

- 模型不会被 Deferred Lighting 重复照亮
- 遮挡关系正确
- Alpha Cutout 轮廓在主画面和阴影中一致
- GBuffer Normal Debug 正确

### 阶段 2：基础材质与光照

实现：

- BaseColor
- Tangent Space Normal
- Packed Mask 解码
- 方向光
- ShadowMask
- Cubemap IBL 与 DFG
- Material AO/SSAO
- Pre-Exposure

这一阶段先使用 YutrelRP Standard BRDF，暂不实现雨水和复杂风格化逻辑。

验收：

- 金属/非金属区域正确
- 粗糙度和高光形状合理
- 方向光、阴影、环境光与 YutrelRP 曝光系统一致
- 能分别 Debug BaseColor、Normal、Metallic、Roughness、AO

### 阶段 3：基础风格化匹配

实现顺序：

1. 32³ Packed Color LUT
2. Diffuse/Specular Ramp
3. 高光整形
4. Rim/Fresnel 风格化
5. 必要的颜色增益和亮度保护

先将干燥状态匹配到可接受程度，再进入湿润效果。

### 阶段 4：环境光与屏幕空间数据

按优先级实现：

1. Cubemap IBL
2. Directional ShadowMask
3. SSAO/Contact Shadow
4. YutrelRP DDGI 接入
5. 捕获中的 3D Probe/Volume Lighting，仅在确有必要时复刻

当 DDGI 开启时，当前 YutrelRP 不执行普通 EnvironmentLightingPass，因此 EndfieldForward 必须明确选择自己的环境间接光来源。

### 阶段 5：湿润与雨水

由简单到复杂：

1. 全局 Wetness 参数
2. BaseColor 压暗/增饱和
3. Roughness 降低与 Specular 增强
4. 基于材质 Mask 的湿润覆盖
5. 三平面雨水 Normal
6. 流水方向与时间动画
7. 程序化雨滴/水珠
8. 蓝噪声控制的闪烁高光

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
- EID 6346 第二输出的完整 Normal/Flag 编码
- 两张 256×1 Ramp 的精确职责
- 32³ LUT 在最终颜色流程中的准确位置
- R8G8 屏幕空间纹理的双通道语义
- 3D 光照纹理与 Cubemap/Probe 的组合方式
- 全局 Wetness 参数及材质局部 Mask 的来源
- 雨滴、流水和闪烁高光的独立开关与参数范围
- 原游戏 Stencil 分类在 YutrelRP 中是否需要等价实现
- 最终法线输出被哪些后续 Pass 消费

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

- 修改 `Assets/YutrelRP/Shader` 下的 HLSL/HLSLI 后必须运行：

```powershell
python tools\agent_harness.py shader-format
python tools\agent_harness.py compile
```

