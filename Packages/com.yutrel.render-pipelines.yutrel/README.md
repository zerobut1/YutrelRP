# Yutrel Render Pipeline

YutrelRP is a desktop-focused deferred Scriptable Render Pipeline for Unity 6000.5.

## Install from disk

1. Open **Window > Package Management > Package Manager**.
2. Choose **Add package from disk**.
3. Select `Packages/com.yutrel.render-pipelines.yutrel/package.json` from the YutrelRP repository.

For a manifest dependency, point `file:` at the same package directory. For Git installs, use the repository URL with `?path=/Packages/com.yutrel.render-pipelines.yutrel`.

## Create and select the pipeline

1. Choose **Assets > Create > Rendering > YutrelRP Asset (with Deferred Renderer)**. Unity creates a pipeline asset and a matching Deferred Renderer Data asset.
2. Assign the new asset under **Project Settings > Graphics > Default Render Pipeline**.
3. Clear existing render pipeline overrides under **Project Settings > Quality**, or assign the YutrelRP asset to each override.
4. Unity creates `Assets/Settings/YutrelRPGlobalSettings.asset` when the pipeline is initialized.

URP can remain installed while YutrelRP is active. Remove it only if the consuming project no longer needs it.

## Renderer selection

The pipeline asset contains an ordered Renderer Data list and a default Renderer. Add `YutrelAdditionalCameraData` to a Game Camera to select another entry; Scene View, Preview, and Reflection cameras always use the default Renderer.

Existing YutrelRP assets are upgraded automatically. Their GUID and Graphics/Quality references remain unchanged, while the old Deferred settings are moved to a new Renderer Data asset beside the pipeline asset.

Custom renderers derive from `YutrelRendererData` and `YutrelRenderer`. Their scene output must be finite, pre-exposed linear RGB using BT.709 primaries and a D65 white point. Tone Mapping and the Final Pass are owned by YutrelRP and are shared by every renderer.

## DDGI requirements

DDGI is disabled by default. Enabling it requires Windows, Direct3D 12, ray-tracing-capable hardware, and Unity ray tracing support.
