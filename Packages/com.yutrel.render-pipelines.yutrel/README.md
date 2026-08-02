# Yutrel Render Pipeline

YutrelRP is a desktop-focused deferred Scriptable Render Pipeline for Unity 6000.5.

## Install from disk

1. Open **Window > Package Management > Package Manager**.
2. Choose **Add package from disk**.
3. Select `Packages/com.yutrel.render-pipelines.yutrel/package.json` from the YutrelRP repository.

For a manifest dependency, point `file:` at the same package directory. For Git installs, use the repository URL with `?path=/Packages/com.yutrel.render-pipelines.yutrel`.

## Create and select the pipeline

1. Choose **Assets > Create > Rendering > YutrelRP Asset**.
2. Assign the new asset under **Project Settings > Graphics > Default Render Pipeline**.
3. Clear existing render pipeline overrides under **Project Settings > Quality**, or assign the YutrelRP asset to each override.
4. Unity creates `Assets/Settings/YutrelRPGlobalSettings.asset` when the pipeline is initialized.

URP can remain installed while YutrelRP is active. Remove it only if the consuming project no longer needs it.

## DDGI requirements

DDGI is disabled by default. Enabling it requires Windows, Direct3D 12, ray-tracing-capable hardware, and Unity ray tracing support.
