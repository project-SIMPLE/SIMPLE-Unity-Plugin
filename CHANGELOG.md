# Changelog

All notable changes to this project will be documented in this file.

## [1.0.0-preview.1] - 2026-04-24

### Changed
- **WebSocket Transport**: Switched to `NativeWebSocket` for better platform compatibility (WebGL/Android/PC).
- **Middleware Focus**: The package is now optimized to work with `simple.webplatform` (port 8080) instead of direct GAMA connection (port 1000).
- **Project Structure**: Cleaned up `package.json` and `.asmdef` files to match UPM standards.

### Fixed
- **Missing Resources**: Added default assets in `Runtime/Resources` for localization and materials to prevent runtime crashes.
- **Scene Setup**: `GAMA > Setup Scene` now creates required tags and builds the base VR scene procedurally, including the managers, teleport area, FPS player, controller/interactor hierarchy, and locomotion hierarchy.
- **Teleportation Setup**: Runtime teleportation areas are now generated in code instead of loading the corrupt `TeleportAreaRaw.prefab`.
- **Dependency Cleanup**: Removed `WebSocketSharp` references that were causing compilation errors in clean projects.
- **Editor Stubs**: Replaced legacy geometry import/export tools with stubs to maintain project compilation.

### Added
- **Default Assets**: Added `LocalizationData.csv` and default materials.
- **Third Party Notices**: Documented the use of `NativeWebSocket`.
