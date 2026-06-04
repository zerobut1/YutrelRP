# DDGI Gather Coverage 与 Visibility 说明

本文记录当前阶段 `ProbeIrradiance / ProbeDistance -> screen-space gather` 的数据契约。目标是让靠近 DDGI volume 边界、甚至略在边界外的墙面和地面继续获得可调试的 diffuse DDGI 贡献，同时用 distance atlas 降低明显被遮挡 probe 的贡献。

## Coverage

`DDGIVolumeCoverage` 使用 world-space volume bounds 与 `probe_spacing_ws` 计算 coverage：

```text
volume 内部或边界上: coverage = 1
volume 外侧小于对应轴 probe spacing: 按 smoothstep 平滑衰减
volume 外侧超过对应轴 probe spacing: coverage = 0
```

外延距离来自每个轴的 probe spacing，不新增独立 author-facing fade 参数。这个行为用于匹配 RTXGI-style volume blend：volume 仍定义可查询区域，但边界外允许一小段过渡，避免墙面或地面因为 probe grid 与几何体保持安全距离而立即失去 DDGI。

## Surface Bias

Gather 对 visibility 使用一个 biased surface position：

```text
biasedPosition = position + normal * ProbeNormalBias
               + viewDirection * ProbeViewBias
```

coverage 仍使用原始 surface position 计算，避免 bias 把边界表面错误推出覆盖范围。bias 只参与 probe-to-surface distance visibility，用于减少贴表面采样带来的自遮挡黑斑和数值不稳定。

实际 bias 距离来自 `YutrelDDGIVolume.ProbeNormalBias` 与 `YutrelDDGIVolume.ProbeViewBias`，不是硬编码比例。`viewDirection` 约定为 surface 指向 camera 的 world-space 方向。

## Visibility

每个 trilinear probe contribution 会额外采样 `ProbeDistance` atlas：

```text
R = mean(distance / probeMaxRayDistance)
G = mean((distance / probeMaxRayDistance)^2)
B = hit ratio
```

采样方向为 probe 指向 biased surface position。当前 visibility 是第一版近似：

1. 如果 surface distance 没有超过 stored mean distance 加 tolerance，则认为该 probe 可见。
2. 如果 surface distance 明显更远，则用 moments 的 Chebyshev-style 权重降低贡献。
3. miss-heavy 方向不会强行遮挡，`hit ratio` 越高，distance moments 对 visibility 的影响越强。
4. backface ray 只提供低置信度 distance 信息，避免把 probe 内部或背面命中解释成大面积黑洞。
5. invalid / NaN / Inf moments 会按“无可靠遮挡信息”处理，不参与强遮蔽。

Surface normal 与 surface-to-probe 方向关系属于 probe selection weight，不再写入 `DDGI VisibilityCoverage` 的 visibility 通道。这样 debug view 显示的是 distance occlusion 本身，不会把 probe 格点的 normal compatibility 权重误看成遮挡。

probe selection 仍使用 wrap-shading floor 压低背面 probe，避免穿墙漏光，同时避免所有邻近 probe 因 normal 权重同时归零。

最终 diffuse gather 对 8 个邻近 probe 做 trilinear accumulate：

```text
contribution = trilinearWeight * distanceVisibility * surfaceWeight
irradiance += probeIrradiance * contribution
irradiance /= sum(contribution)
visibilityDebug += trilinearWeight * distanceVisibility
finalDiffuse = irradiance * coverage
```

这不是最终 RTXGI 质量实现，也不包含 relocation/classification/multi-bounce，但已经把 distance atlas 纳入了 per-pixel gather 的质量调节闭环。visibility 现在主要用于 probe 选择和 debug 诊断，不再作为未归一化亮度遮罩直接把 probe 格点烙到墙面上。

当前 stored irradiance 已经是 probe ray radiance 的低频方向滤波结果；screen-space gather 不再除以 PI。最终写入 `scene_color` 前，只在 `EnvironmentLightingPass` 中应用材质 diffuse color、AO、DDGI diffuse intensity、coverage 和 pre-exposure。

## Debug View

当前 DDGI debug view 的含义：

```text
DDGI ProbeRayData        : 查看 miss / front-face / back-face ray 状态与 direct radiance
DDGI ProbeIrradiance    : 查看 persistent irradiance atlas
DDGI ProbeDistance      : 查看 persistent distance moments atlas
DDGI Diffuse Only       : 查看 coverage + visibility 后的 diffuse DDGI 贡献
DDGI Coverage           : 查看 coverage 标量
DDGI VisibilityCoverage : RGB = coverage / visibility / coverage * visibility
```

如果 `DDGI VisibilityCoverage` 中：

```text
R 低: surface 超出 volume 外延覆盖范围
G 低: surface 主要被 distance visibility 压低
B 低: 最终 DDGI gather 权重低
```

则可以区分问题来自 volume 覆盖、probe visibility，还是上游 atlas 没有有效 radiance。

## Volume 摆放建议

本阶段不要求把 probe 推入墙体或地板。推荐让 probe grid 与主要静态几何保持少量安全距离，再依靠 `probe_spacing_ws` 外延覆盖边界表面。如果某些表面距离 volume 超过一个对应轴 probe spacing，仍需要后续调 volume 或引入 multi-volume/scrolling，当前阶段不承诺无限外延覆盖。
