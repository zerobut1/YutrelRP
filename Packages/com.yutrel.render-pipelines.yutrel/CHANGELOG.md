# Changelog

All notable changes to this package are documented in this file.

## [Unreleased]

- Updated the minimum Unity version to 6000.6 and the SRP Core dependency to 17.6.0.
- Kept camera warning deduplication keyed by the full `EntityId` instead of its integer hash.
- Removed the ShadowMask shader debug-symbol pragma; shader debugging can be enabled explicitly when needed.

## [0.1.0] - 2026-08-02

- Added the initial embedded UPM package layout.
- Added isolated Runtime, Editor, and Editor Tests assemblies.
- Added project-local pipeline asset creation and global settings initialization.
- Moved runtime shaders, DDGI resources, and the DFG LUT into the package.
- Added Renderer Data lists, default and per-Game-Camera Renderer selection, and dirty-instance rebuilding.
- Moved the existing render path into `YutrelDeferredRenderer` and centralized exposure, Tone Mapping, and Final output.
- Added automatic migration for legacy pipeline assets while preserving their GUIDs and project references.
