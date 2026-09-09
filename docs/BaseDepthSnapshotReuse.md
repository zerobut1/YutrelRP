# Base深度快照的复用范围与性能判断

2026-09-09。用户决定保留深度复制与固定功能深度/模板测试。本轮检查复用范围，
未修改运行时代码、没有启动Unity或测量GPU耗时。

## 当前资源语义

- scene_depth：D32_SFloat_S8_UInt附件，后续Forward/Transparent继续更新，保留模板。
- depthSnapshot：Base结束后的R32_SFloat原始device-depth快照，之后只读，不包含模板。

快照已经按相机复制一次；新增消费者不需要额外复制或分配纹理。
它与Base生成的GBuffer处于同一时点，适合作为Base/GBuffer相关着色的公共采样输入。

## 可复用的现有消费者

| 消费者 | 当前读取位置 | 可复用原因 |
|---|---|---|
| ShadowMaskPass | scene_depth，PS Sample .r重建位置 | 位于Base与Forward之间，无模板采样 |
| DirectionalLightPass（每盏方向光） | scene_depth，GBuffer位置重建 | 同上 |
| ScreenSpaceAmbientOcclusionPass（SSAO/HBAO/GTAO） | scene_depth，重建/邻域采样 | 同一时点、全分辨率、raw depth格式兼容 |
| EnvironmentLightingPass | scene_depth，GBuffer位置重建 | 同上 |
| DDGILightingPass | scene_depth，GBuffer位置重建 | 同上；当前与EnvironmentLighting走不同分支 |
| 当前头发描边736 | _CameraDepthTexture | 已使用快照，继续维持 |

前五类Pass目前均为RasterPass，只用UseTexture声明深度，在材质/MPB中绑定_SceneDepth，
没有把该资源作为深度附件，也不使用比较采样器或模板采样。
因此可以只调整C#选择的资源句柄及RenderGraph声明，保留现有HLSL算法和属性名。

不能直接替换：Base/Forward/Skybox/Transparent的固定功能深度附件；需要Stencil的用途；
Renderer最终sceneDepth输出；显示最终SceneDepth的调试，以及按最终可见深度遮挡的探针可视化。
未来软粒子等若需要“不透明Forward完成后的深度”，也不能默认使用Base快照。

## 状态转换方面的实际边界

现在的逻辑访问序列大致是：

```text
原深度：Base附件写 → Copy及屏幕光照连续采样读 → Forward附件读写 → 后续阶段
副本：  Copy颜色写 → Forward采样读
```

迁移后：

```text
原深度：Base附件写 → Copy采样读 → Forward附件读写 → 后续阶段
副本：  Copy颜色写 → 屏幕光照及Forward连续采样读
```

原序列在Copy与各屏幕Pass之间没有新的深度写入，不能按消费者数量推断有多次读写layout往返。
迁移后原深度仍须为Copy进入采样状态、为Forward回到附件状态，副本也仍须从颜色写变成采样读。
所以从当前代码依赖图无法得出“替换五类消费者就省掉多次transition”的结论。
副本复用能缩短原深度的采样依赖范围，但不保证转换数量减少或GPU更快。

还会增加一个相反方向的约束：原先Copy和ShadowMask等同为Base深度的读取者，
资源依赖上可并列；改读副本之后，这些Pass必须等待Copy写入完成。
即使当前按同一图形队列记录，额外的RAW依赖仍可能影响后端重叠与barrier安排。
Forward对ShadowMask、AO以及scene_color的其它依赖不会因切换深度句柄而消失。
不应据此直接引入Async Compute或手动设置资源状态。

R32与D32S8的缓存、压缩/解压行为可能不同，收益依设备而定；本次没有实际Unity GPU截帧，
不将资源格式变化或缩短依赖范围当作已证明的性能提升。

## 建议的最小后续方案

1. FrameData明确区分原生附件和Base快照，禁止把scene_depth整体替换成R32句柄。
2. 给Base之后、Forward之前的采样消费者提供统一的Base深度选择入口：有效快照存在则可复用，
   没有快照时直接读原深度。这里的回退仅适用于这些不绑定深度附件的Pass，不能套用到Forward材质。
3. 保持按需生成；不要仅为迁移这些原本能够直接读取深度的Pass而强制新增复制。
4. 保持一份原深度路径作A/B对照，核对画面和原始深度数值，再测Copy至Forward整段GPU时间、
   实际barrier/layout、native pass拆分情况，而非只比较单个ShadowMask耗时。
5. 最终是否默认让这五类Pass读快照，以实际结果决定。当前可以确定的是复用合法，性能收益待测。

这属于通用相机/GBuffer资源管理，不需要RenderFeature，也不需要描边专用管线行为。
