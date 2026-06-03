# DDGI Shader Foundation 约定

本文记录当前阶段 DDGI shader 侧的最小 contract。后续 probe blending、atlas 写入和 screen-space gather 应优先复用 `Assets/YutrelRP/Shader/DDGI/DDGI.hlsl`，不要重新定义索引、方向或 atlas 布局。

## Probe 坐标

DDGI 目前只支持单个固定 Volume。Probe 坐标为：

```text
probeCoord = (probeX, probeY, probeZ)
0 <= probeX < probeCount.x
0 <= probeY < probeCount.y
0 <= probeZ < probeCount.z
```

Y 轴是 `Texture2DArray` slice 轴。一个 Y slice 内的平面索引为：

```text
planeProbeIndex = probeX + probeZ * probeCount.x
probeX = planeProbeIndex % probeCount.x
probeZ = planeProbeIndex / probeCount.x
```

Probe world position 与 CPU 侧 `YutrelDDGIVolume` 保持一致：

```text
probeWorldPosition = volumeWorldMin + probeCoord * probeSpacingWS
```

因此 `(0, 0, 0)` 位于 Volume world min，`(countX-1, countY-1, countZ-1)` 位于 Volume world max。

## ProbeRayData

`ProbeRayData` 是当前 probe trace 的帧内输出，格式为 `Texture2DArray R16G16B16A16_SFloat`：

```text
width  = raysPerProbe
height = probeCount.x * probeCount.z
slices = probeCount.y
```

任意 probe/ray 对应 texel：

```text
x     = rayIndex
y     = probeX + probeZ * probeCount.x
slice = probeY
```

当前 ray direction 使用 deterministic golden-angle sphere 分布：

```text
normalizedIndex = (rayIndex + 0.5) / raysPerProbe
y = 1 - 2 * normalizedIndex
phi = rayIndex * 2.39996322972865332
direction = normalize(float3(cos(phi) * radius, y, sin(phi) * radius))
```

`DDGIProbeTrace.raytrace` 写入和 `DebugViewPass.hlsl` 读取都复用 `DDGIProbeRayDataTexel` 与 `DDGIBuildProbeRayDirection`。

## Irradiance / Distance Atlas

`ProbeIrradiance` 与 `ProbeDistance` 使用相同的 tile 布局。每个 probe 占一个 octahedral tile，tile 包含 1 texel border：

```text
tileTexelSize = interiorTexels + 2
atlasWidth    = probeCount.x * tileTexelSize
atlasHeight   = probeCount.z * tileTexelSize
slices        = probeCount.y
```

任意 probe 的 tile 起点与 interior 起点为：

```text
tileBaseTexel     = (probeX, probeZ) * tileTexelSize
interiorBaseTexel = tileBaseTexel + (1, 1)
slice             = probeY
```

Tile 内 `localTexel.x == 0`、`localTexel.y == 0`、`localTexel.x == tileTexelSize - 1` 或 `localTexel.y == tileTexelSize - 1` 均为 border。其它 texel 是 octahedral interior。

现有 atlas debug view 会在空 atlas 上显示结构化可视化：border 为暖色边框，interior 使用 octahedral decode 后的方向颜色，并叠加 probe index tint。

Probe blending 写入 border 时使用 `DDGIAtlasWrappedInteriorTexel`。该 helper 将 border texel 映射到 octahedral 对边或对角的 interior texel，再用对应的 interior 方向生成当前帧值；因此 border 不是未定义清空色，也不会跨 probe tile 读取。

## Octahedral 方向

`DDGIOctahedralEncode` 输入单位方向，输出 `[0, 1]` 范围 UV。`DDGIOctahedralDecode` 输入 `[0, 1]` 范围 UV，输出单位方向。零长度输入会回退到确定性的 `float3(0, 1, 0)`，避免 NaN/Inf。

Atlas interior debug 使用：

```text
interiorUV = (localInteriorTexel + 0.5) / interiorTexels
direction  = DDGIOctahedralDecode(interiorUV)
```

后续 blending 写入 irradiance/distance atlas 时，应使用同一套 encode/decode 和同一套 border/interior 约定。

当前 probe blending 阶段通过 `DDGIAtlasInteriorUV(localTexel, interiorTexels)` 取得 interior 或 border-wrapped interior 方向。

## ProbeData

`ProbeData` 是 per-probe metadata atlas：

```text
width  = probeCount.x
height = probeCount.z
slices = probeCount.y
texel  = (probeX, probeZ, probeY)
rgba   = offset.xyz / state
```

当前最小 contract 为：

```text
offset.xyz = 0
state.w    = 1 表示 active
```

Probe relocation 与 classification 暂不实现；后续阶段可以扩展 state 语义，但不能破坏上述初始含义。
