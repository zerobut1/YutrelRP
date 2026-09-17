# NTE material integration decisions

This document records the YutrelRP-side contract for the versioned NTE 2026-09
adapters. The accepted material arithmetic remains in the Sandbox
`Assets/NTE/Shaders/2026_09/Core` tree; the package contains only pipeline
infrastructure and does not reference Sandbox assets.

## Pipeline contract

- `enableDepthPrepass` is opt-in and defaults to false. It draws opaque
  `DepthOnly` passes before Base. Materials without that pass are unaffected.
- Base keeps the same depth attachment with read/write access, so it preserves
  prepass results while leaving each shader's depth state authoritative.
- The four deferred attachments are fixed to:
  - A: `R8G8B8A8_SRGB`; logical RGB is converted by the render target and A is
    the YutrelRP shading-model byte.
  - B: `R16G16B16A16_UNorm`; existing normal and auxiliary values.
  - C: `R8G8B8A8_UNorm`; existing material channels, including native bytes.
  - D: `R8G8B8A8_UNorm`; existing specular/custom payload.
- `SHADING_MODEL_NTE` is pipeline ID 4. Native NTE material model 13 remains a
  separate byte inside the NTE payload and is never decoded as the pipeline ID.
- Standard and OpenPBR keep the existing `EncodeGBuffer`/`DecodeGBuffer`
  channel contract. Environment and DDGI passes still accept Standard only;
  NTE is deliberately excluded.

## Camera and Volume ownership

`NteVolumeSettings` owns five unnamed interpolation palettes (`shape`,
`atOne`, `atZero`), their common endpoint, the two screen-depth rim endpoints,
rim width, and an independent output multiplier. Defaults are copied from the
Nanally capture. The names intentionally avoid assigning unverified ambient
light semantics.

`ResolvedNteSettings` is an immutable per-camera snapshot. `NteShaderGlobals`
binds that snapshot for Base, deferred directional lighting, and forward
overlays. This prevents a later camera or a no-light camera from inheriting
direction or Volume values from an earlier camera.

Materials continue to own lit/shade colors, ramp rows, MatCaps, face controls,
palette selection, and material-direction overrides. The adapter owns camera
matrices, depth, the first directional-light direction, CSM visibility, and the
fallback direction used when no directional light exists.

## Exposure convention

YutrelRP uses a fixed reference EV100 of 14 and compensation 0. Its reference
pre-exposure is `1 / (1.2 * 16384)`. NTE scene-color RGB receives exactly:

`outputMultiplier * currentPreExposure / referencePreExposure`

Native exposure input remains 1. GBuffer payloads, alpha, and multiplicative
EyeShadow/BangsShadow factors do not receive this scale. The NTE directional
extension returns already-scaled scene color, so the package's Standard
`ApplyPreExposure` path is not applied again.

## Directional-light extension

The renderer data may provide a directional-light Shader override. The package
default remains unchanged when no override is assigned, and the cached
material is rebuilt whenever the selected Shader changes. The Sandbox override
uses NTE Core `Model13Directional` only for pipeline model 4 and only for
directional light index 0. CSM visibility maps to `shadowFactor`; auxiliary and
diffuse shadow inputs stay neutral at 1. Later directional lights return zero
for NTE while other shading models retain their package behavior.

## Acceptance boundary

Offline Shader compilation and C# compilation are required for this stage.
Unity runtime image acceptance, material creation, renderer assignment,
prefabs, and scene assembly are separate work. No successful offline result is
recorded as a runtime visual pass.
