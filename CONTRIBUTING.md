# Contributing

This package is currently being extracted from the SIMPLE Unity Template VR project.

Before opening a pull request:

1. Install the package in a clean Unity project with **Add package from disk...**.
2. Verify that the Unity console has no package compilation errors.
3. Keep runtime code in `Runtime/`, editor-only code in `Editor/`, and importable examples in `Samples~/`.
4. Keep Unity `.meta` files for package assets, scripts, assemblies, scenes, prefabs, materials, and sample content.
5. Do not commit generated Unity folders such as `Library/`, `Temp/`, `Obj/`, `Logs/`, or `UserSettings/`.

Known extraction work should be handled before publishing:

- Resolve the WebSocket dependency strategy.
- Move required runtime resources into package-owned paths.
- Replace generated documentation with package-specific documentation.
