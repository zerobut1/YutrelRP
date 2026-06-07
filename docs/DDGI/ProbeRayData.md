# DDGI ProbeRayData 贴图说明

`DDGI ProbeRayData` 是 DDGI Probe Trace 阶段生成的逐 ray 数据贴图，用来保存每个 probe 发出的每条 ray 的 direct radiance 与命中状态。它是 `ProbeIrradiance` blending 的输入，同时仍可用于验证 probe trace 是否正确命中场景几何。

## 贴图尺寸

贴图类型为 `Texture2DArray`，格式由 `YutrelDDGIVolume.Probe Ray Data Format` 决定：

```text
F32x2 -> R32G32_SFloat
F32x4 -> R32G32B32A32_SFloat
```

默认使用 `F32x2`，与 RTXGI 的默认高性能路径一致；调试 Probe Trace 结果时可以切换到 `F32x4`，避免 radiance 被打包到 R10G10B10。

尺寸映射如下：

```text
width  = raysPerProbe
height = probeCount.x * probeCount.z
slices = probeCount.y
```

例如当前调试中看到的 `64 x 4 x 2` 表示：

```text
raysPerProbe = 64
probeCount.x * probeCount.z = 4
probeCount.y = 2
```

如果总共有 8 个 probe，通常对应：

```text
probeCount = 2 x 2 x 2
```

## 与 Persistent Atlas 的关系

`raysPerProbe` 只决定本帧 `ProbeRayData` 的 width 和调试 metadata。持久化的 `ProbeIrradiance`、`ProbeDistance`、`ProbeData` atlas 尺寸由 `probeCount` 与各自 atlas tile texel 数决定，因此 persistent atlas identity 不包含 `raysPerProbe`；单独调整 ray 数不会清空这些 atlas 历史。

Shader 侧权威约定见 `docs/DDGI/ShaderFoundation.md` 与 `Assets/YutrelRP/Shader/DDGI/DDGI.hlsl`。后续 DDGI pass 应复用其中的 probe index、ProbeRayData texel、atlas tile 与 octahedral helper。

## Slice 含义

`Texture2DArray` 的每个 slice 对应一层 Y 方向的 probe：

```text
Slice 0 = probeY 0
Slice 1 = probeY 1
Slice N = probeY N
```

也就是说，slice 不是颜色通道，也不是 mip，而是 DDGI Volume 在 Y 方向上的 probe 层。

## 行列含义

每个 slice 内部：

```text
x/column = rayIndex
y/row    = probeX + probeZ * probeCount.x
```

对于 `probeCount = 2 x 2 x 2`，每个 slice 有 4 行：

```text
row 0 = probe (x=0, y=slice, z=0)
row 1 = probe (x=1, y=slice, z=0)
row 2 = probe (x=0, y=slice, z=1)
row 3 = probe (x=1, y=slice, z=1)
```

每一行的 `column 0..raysPerProbe-1` 是该 probe 发出的所有 ray。

## 数据编码

每个像素保存一条 ray 的 radiance 与 signed distance/state：

```text
F32x2:
R = asfloat(R10G10B10 packed radiance)
G = signed distance / state
B/A unused

F32x4:
RGB = radiance
A   = signed distance / state
```

Shader 侧必须通过 `DDGIProbeRayDataLoadRadiance`、`DDGIProbeRayDataLoadDistance`、`DDGIProbeRayDataEncode*Value` 和 `DDGIProbeRayDataSet*DistanceOnly` 读写，不要直接假设 `.g` 或 `.a` 是 distance。

### Radiance

Radiance 表示 trace ray 的当前阶段 diffuse radiance。front-face hit 时包含 trace material base color、方向光 direct diffuse、可选方向光 visibility，以及 history irradiance feedback；miss 时写入 environment / fallback skylight radiance。`F32x2` 会把 radiance 饱和到 `[0, 1]` 并打包到 R10G10B10，`F32x4` 保留 32-bit float RGB，适合验证 Probe Trace 的原始结果。

### Signed Distance / State

Distance/state 的逻辑语义与 RTXGI 保持一致：

```text
front-face hit: +distance
back-face hit : -distance * 0.2
miss          : +1e27
```

因此 `distance >= 0.5e27` 为 miss，`distance < 0` 为 back-face hit，`0 <= distance < 0.5e27` 为 front-face hit。后续 visibility、relocation、classification 可以继续复用这个状态区分。

## RenderDoc 查看建议

RenderDoc 中选中 `RayTracing.Dispatch` 后，右侧 `Outputs` 通常不会直接显示这张贴图，因为它不是 render target，而是 ray tracing shader 写入的 UAV。

需要在资源列表中按以下特征查找：

```text
name      ~= DDGI ProbeRayData
dimension = Texture2DArray
format    = R32G32_FLOAT 或 R32G32B32A32_FLOAT
size      = raysPerProbe x (probeCount.x * probeCount.z) x probeCount.y
```

查看时建议：

1. 先看 RGB 或 DDGI ProbeRayData debug view，确认 radiance 是否随方向光颜色、强度、方向变化。
2. F32x2 看 G 通道，F32x4 看 A 通道，确认 miss/front-face/back-face 分布。
3. 查看 distance 通道时，建议显示范围覆盖 `-probeMaxRayDistance..probeMaxRayDistance`；miss 是 `1e27`，通常会被显示范围夹到白色。
4. 最后才看合成图。合成图可能受 HDR 数值和显示范围影响，不适合判断精确数据。

## 基础验证方式

推荐使用极简场景验证：

1. 只放一个开启 `Contribute GI` 的地板。
2. 使用 `probeCount = 2 x 2 x 2`，`raysPerProbe = 64`。
3. 把 DDGI Volume 放在地板上方。
4. 查看下层和上层 slice。

预期结果：

```text
朝向地板的 ray 应该命中，A 为正距离或负距离。
朝向天空的 ray 应该 miss，distance 约为 `1e27`。
关闭地板 Contribute GI 后，相关命中应该消失。
```

如果出现大量 `R = 2`，说明 ray 命中了背面，通常需要检查几何体三角形朝向、模型法线或是否从几何体内部发射。
