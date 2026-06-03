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

每个 tile 的 1 texel border 使用 `DDGIAtlasWrappedInteriorTexel` 映射到 octahedral 对边/对角 interior texel 后重新计算当前帧值。第一版不做独立 border copy pass，但 border texel 保证被写入定义值。

## Irradiance 语义

`ProbeIrradiance` 当前存储的是 first-version direct diffuse radiance accumulation，不是包含 visibility、material albedo、shadow 或 multi-bounce 的最终 DDGI 质量实现。

每个 atlas texel 先通过 `DDGIAtlasInteriorUV` 和 `DDGIOctahedralDecode` 得到 octahedral 方向，然后遍历当前 probe 的全部 ray：

```text
weight = pow(saturate(dot(atlasDirection, rayDirection)), 24) + 0.0001
```

ray contribution 直接来自 `ProbeRayData.rgb`：

```text
miss           -> black radiance
front-face hit -> no-shadow directional direct diffuse radiance
back-face hit  -> black radiance, state 保留在 ProbeRayData.a
```

因此 irradiance atlas 能随当前帧方向光颜色、强度和方向变化；关闭方向光后会向黑色收敛。后续 gather 可以把它作为真实 direct radiance 数据流基线，但仍不能把它视为最终物理完整的 DDGI 结果。

## Distance 语义

`ProbeDistance` 当前从 `ProbeRayData.a` 解析 signed distance/state，并存储归一化 distance moments 与 hit ratio：

```text
R = weighted mean(distance / probeMaxRayDistance)
G = weighted mean((distance / probeMaxRayDistance)^2)
B = weighted hit ratio
A = 1 when initialized by blending
```

miss 使用 `distance01 = 1`、`hit ratio = 0`。front-face hit 使用 `hit ratio = 1`。back-face hit 使用 `hit ratio = 0.5`，用于在 debug/future visibility 中保留“命中了但命中类型较差”的区别。

该数据适合作为下一阶段基础 visibility/gather 的输入原型；最终漏光控制、bias、relocation/classification 语义仍需要后续阶段补全。

## Temporal 与 Reset

blending 使用 active `YutrelDDGIVolume.ProbeHysteresis`：

```text
newAtlas = lerp(currentFrameValue, historyValue, probeHysteresis)
```

atlas 初始清空或重建后 alpha 为 0，第一帧 blending 会忽略历史并直接写入当前帧值。persistent atlas identity 仍由 volume key、probe count、irradiance interior texels、distance interior texels 决定；这些参数变化会释放并重建 atlas，避免跨不兼容 layout 混合历史。
