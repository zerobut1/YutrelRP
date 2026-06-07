# DDGI Probe Blending 实现说明

本文记录 `DDGI ProbeRayData -> ProbeIrradiance / ProbeDistance atlas` 第一版实现的数值语义。它是后续 screen-space gather 的输入边界说明，不代表最终 RTXGI 质量实现。

## Pass 顺序

`DDGIProbeTracePass` 成功生成本帧 `ProbeRayData` 后，会立即记录 `DDGIProbeBlendPass`：

```text
DDGI Probe Trace  ->  DDGI Probe Blend  ->  DDGI atlas debug view / future gather
```

如果 DDGI disabled、active Volume 无效、Frame Debugger 导致 trace 跳过、`ProbeRayData` 缺失、persistent atlas 缺失或 compute shader 缺失，blending 会跳过并记录诊断，不改变非 DDGI 渲染。

## Atlas 布局

`ProbeIrradiance` 与 `ProbeDistance` 继续使用现有 layout：

```text
tileTexelSize = interiorTexels + 2
atlasWidth    = probeCount.x * tileTexelSize
atlasHeight   = probeCount.z * tileTexelSize
slices        = probeCount.y
probeX        = atlasX / tileTexelSize
probeZ        = atlasY / tileTexelSize
probeY        = slice
```

每个 tile 的 1 texel border 使用 `DDGIAtlasWrappedInteriorTexel` 映射到 octahedral 对边/对角 interior texel 后重新计算当前帧值。映射方向与 RTXGI border copy 约定一致：

```text
corner border -> opposite corner interior
top/bottom row border -> mirrored x, nearest interior row
left/right column border -> nearest interior column, mirrored y
```

gather 采样 UV 使用 tile center 加 `octantCoordinates * interiorTexels * 0.5`，让 bilinear footprint 可以跨入 border texel。这样 seam 方向不再依赖黑色或过期 border 数据。

## Irradiance 语义

`ProbeIrradiance` 当前存储的是 probe ray radiance accumulation。自 history irradiance feedback 阶段起，hit ray radiance 包含 direct diffuse 与上一轮 `ProbeIrradiance/ProbeDistance` gather 反射回命中材质后的 diffuse feedback；miss ray 仍写入 environment / fallback skylight radiance。它仍不是包含 relocation、classification、完整材质模型或最终 RTXGI encoding 的质量实现。

每个 atlas texel 先通过 `DDGIAtlasInteriorUV` 和 `DDGIOctahedralDecode` 得到 octahedral 方向，然后遍历当前 probe 的全部 ray：

```text
weight = pow(saturate(dot(atlasDirection, rayDirection)), 24) + 0.0001
```

ray contribution 通过 `DDGIProbeRayDataLoadRadiance` 从 `ProbeRayData` 解码：

```text
miss           -> environment / fallback skylight radiance
front-face hit -> ray-facing normal 的 directional direct diffuse radiance + history feedback radiance
back-face hit  -> ray-facing normal 的 directional direct diffuse radiance + history feedback radiance，state 保留在 ProbeRayData signed distance
```

因此 irradiance atlas 能随当前帧方向光颜色、强度、方向和 skylight miss 输入变化。后续 gather 可以把它作为真实 radiance 数据流基线，但仍不能把它视为最终物理完整的 DDGI 结果。

当前能量约定：

```text
ProbeRayData radiance  = first-version diffuse radiance sample；hit 时已包含 trace material baseColor / PI、NoL、directional visibility；miss 时是 environment radiance
ProbeRayData radiance += history ProbeIrradiance gather * saturate(trace baseColor) / PI * coverage * visibility；feedback 使用固定 1.0 gain，并在写入前 clamp 到有限非负范围
ProbeIrradiance.rgb    = 对 ProbeRayData radiance 做方向滤波后的 radiance-like 值，不包含 screen-space AO 或 final pre-exposure
EnvironmentLighting    = sample ProbeIrradiance 后乘 surface.diffuse_color、AO、DDGI diffuse intensity、coverage，并只在最终写 scene_color 时应用 pre-exposure
```

history feedback 在 probe trace 阶段采样上一帧/上一轮 persistent atlas；本帧 trace 完成后，再由 blend pass 通过 `ProbeHysteresis` 写回同一 persistent atlas。atlas alpha 为 0 的初始化状态不会作为有效历史权重参与 blend，初始 history 为黑色，因此反馈会从 direct/miss 输入逐帧积累。反馈采样沿用 `DDGISampleTrilinearGather` 的 volume coverage、distance visibility、normal/view bias 与 atlas layout 语义，避免绕过已有 gather 约束。

当前 trace 对 front/back hit 都按双面几何处理并使用 ray-facing normal 着色。这样可以避免导入资源 winding 或室内可见面被 RTAS 判定为 backface 时，把实际可见亮墙写成黑色。front/back 状态仍通过 `ProbeRayData` signed distance 保留给 distance/debug。

## Distance 语义

`ProbeDistance` 当前通过 `DDGIProbeRayDataLoadDistance` 从 `ProbeRayData` 解析 signed distance/state，并存储归一化 distance moments 与 hit ratio：

```text
R = weighted mean(distance / probeMaxRayDistance)
G = weighted mean((distance / probeMaxRayDistance)^2)
B = weighted hit ratio
A = 1 when initialized by blending
```

distance blend 的方向权重使用 `YutrelDDGIVolume.DistanceExponent`。miss 使用 `distance01 = 1`、`hit ratio = 0`，因此不会被当作近距离遮挡。front-face hit 使用 `hit ratio = 1`。back-face hit 使用 `hit ratio = 0.15`，用于在 debug/future visibility 中保留“命中了但命中类型较差”的区别，同时避免背面命中造成大面积过度遮蔽。

该数据适合作为下一阶段基础 visibility/gather 的输入原型；最终漏光控制、bias、relocation/classification 语义仍需要后续阶段补全。

## Temporal 与 Reset

blending 使用 active `YutrelDDGIVolume.ProbeHysteresis`：

```text
newAtlas = lerp(currentFrameValue, historyValue, probeHysteresis)
```

atlas 初始清空或重建后 alpha 为 0，第一帧 blending 会忽略历史并直接写入当前帧值。persistent atlas identity 由 volume key、probe count、irradiance interior texels、distance interior texels 和 atlas semantic version 决定；这些参数或语义版本变化会释放并重建 atlas，避免跨不兼容 layout / border / distance convention 混合历史。
