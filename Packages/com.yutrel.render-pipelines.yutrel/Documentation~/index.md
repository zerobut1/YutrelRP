# Yutrel Render Pipeline

YutrelRP is a custom deferred Scriptable Render Pipeline targeting high-performance desktop platforms.

See the package README for installation and project setup. Runtime rendering depends on `com.unity.render-pipelines.core`; DDGI additionally requires Direct3D 12 and hardware ray tracing.

YutrelRP uses a `YutrelRendererData` to `YutrelRenderer` architecture. The built-in `YutrelDeferredRendererData` owns Deferred-only Shadow, AO, and DDGI settings. A pipeline asset stores the Renderer Data list and its default entry, while `YutrelAdditionalCameraData` can select an entry for a Game Camera.

Every renderer returns a scene color in pre-exposed linear BT.709/D65. The pipeline then applies the common Tone Mapping pass, the renderer's optional after-post hook, and the common Final pass.
