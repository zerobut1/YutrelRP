# Yutrel Render Pipeline

YutrelRP is a custom deferred Scriptable Render Pipeline targeting high-performance desktop platforms.

See the package README for installation and project setup. Runtime rendering depends on `com.unity.render-pipelines.core`; DDGI additionally requires Direct3D 12 and hardware ray tracing.

YutrelRP uses a `YutrelRendererData` to `YutrelRenderer` architecture. The built-in `YutrelDeferredRendererData` owns Deferred-only Shadow, AO, and DDGI settings. A pipeline asset stores the Renderer Data list and its default entry, while `YutrelAdditionalCameraData` can select an entry for a Game Camera.

Every renderer returns a scene color in pre-exposed linear BT.709/D65. A renderer may also return a same-size Unity device-depth attachment; linear depth AOVs must first be converted to the active graphics API's depth convention.

The pipeline owns Scene View geometry emission, pre-image-effect Gizmos, Tone Mapping, the renderer's optional after-post hook, post-image-effect Gizmos, and the Final pass. Custom renderers must not duplicate those common operations. If no depth attachment is returned, rendering continues normally and the common Gizmos passes are skipped.
