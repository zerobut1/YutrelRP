# DDGI ProbeRayData 贴图说明

`DDGI ProbeRayData` 是 DDGI Probe Trace 阶段生成的调试贴图，用来保存每个 probe 发出的每条 ray 的原始命中结果。它目前不是最终的 GI 辐照度贴图，也不是最终参与光照采样的缓存，而是用于验证 probe trace 是否正确命中场景几何。

## 贴图尺寸

贴图类型为 `Texture2DArray`，格式为 `R16G16B16A16_SFloat`。

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

Shader 侧权威约定见 `docs/DDGI/ShaderFoundation.md` 与 `Assets/YutrelRP/Shader/Utils/DDGI.hlsl`。后续 DDGI pass 应复用其中的 probe index、ProbeRayData texel、atlas tile 与 octahedral helper。

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

## RGBA 编码

每个像素保存一条 ray 的命中结果：

```text
R = hitKind
G = ray distance
B = normalized distance cue
A = 1
```

### R 通道

`R` 表示命中类型：

```text
0 = miss
1 = front-face hit
2 = back-face hit
```

在 RenderDoc 中建议单独查看 R 通道，并把显示范围设为 `0..2`。

### G 通道

`G` 表示命中距离：

```text
front-face hit: +distance
back-face hit : -distance
miss          : -1
```

在 RenderDoc 中查看 G 通道时，建议把范围设为 `0..probeMaxRayDistance`。如果看 RGBA 合成图，距离值经常会超过 `0..1`，因此容易被夹成白色。

### B 通道

`B` 是距离的归一化提示值：

```text
B = saturate(1 - distance / probeMaxRayDistance)
```

越近越亮，越远越暗。miss 时为 `0`。

### A 通道

`A` 固定为 `1`，目前没有额外含义。

## RenderDoc 查看建议

RenderDoc 中选中 `RayTracing.Dispatch` 后，右侧 `Outputs` 通常不会直接显示这张贴图，因为它不是 render target，而是 ray tracing shader 写入的 UAV。

需要在资源列表中按以下特征查找：

```text
name      ~= DDGI ProbeRayData
dimension = Texture2DArray
format    = RGBA16F / R16G16B16A16_FLOAT
size      = raysPerProbe x (probeCount.x * probeCount.z) x probeCount.y
```

查看时建议：

1. 先看 R 通道，确认 miss/front-face/back-face 分布。
2. 再看 G 通道，确认命中距离是否符合 probe 到几何体的距离。
3. 再看 B 通道，确认近处更亮、远处更暗。
4. 最后才看 RGBA 合成图。合成图只适合快速感知分布，不适合判断精确数据。

## 基础验证方式

推荐使用极简场景验证：

1. 只放一个开启 `Contribute GI` 的地板。
2. 使用 `probeCount = 2 x 2 x 2`，`raysPerProbe = 64`。
3. 把 DDGI Volume 放在地板上方。
4. 查看下层和上层 slice。

预期结果：

```text
朝向地板的 ray 应该命中，R 为 1 或 2，G 为到地板的距离。
朝向天空的 ray 应该 miss，R 为 0。
关闭地板 Contribute GI 后，相关命中应该消失。
```

如果出现大量 `R = 2`，说明 ray 命中了背面，通常需要检查几何体三角形朝向、模型法线或是否从几何体内部发射。
