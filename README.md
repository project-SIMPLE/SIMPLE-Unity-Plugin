# SIMPLE Unity Plugin

`com.project-simple.gama-unity` is a Unity Package Manager package for SIMPLE VR scenes that connect to the `simple.webplatform` middleware.

The runtime client connects to the webplatform headset WebSocket endpoint, not directly to GAMA Server:

- Unity headset runtime: `ws://<webplatform-host>:8080/`
- Web monitor UI: `ws://<webplatform-host>:8001/`
- GAMA Server, used by webplatform: `ws://<gama-host>:1000/`

## Package Contents

- `Runtime/`: webplatform connection, simulation, serialization, localization, movement, and geometry utilities.
- `Runtime/ThirdParty/NativeWebSocket/`: vendored NativeWebSocket transport used by the runtime client.
- `Editor/`: package editor menu entries. Legacy geometry import/export is stubbed by default because the old implementation depended on WebSocketSharp.
- `Samples~/`: importable Unity scenes and templates.
- `Tests/`: basic runtime and editor test assemblies.
- `Documentation~/`: package notes and protocol details.

## Installation

Use Unity Package Manager with **Add package from disk...** and select this package's `package.json`.

For local project testing, you can also add it to a Unity project's `Packages/manifest.json`:

```json
{
  "dependencies": {
    "com.project-simple.gama-unity": "file:../path/to/SIMPLE-Unity-Plugin"
  }
}
```

Do not add another copy of NativeWebSocket unless you remove the vendored copy from this package.

## Runtime Usage

1. Start `simple.webplatform`.
2. Make sure the headset WebSocket port is `8080` or set `HEADSET_WS_PORT` on the webplatform side.
3. In Unity, set `PlayerPrefs["IP"]` to the machine running webplatform and `PlayerPrefs["PORT"]` to `8080`.
4. Add `ConnectionManager` to the startup scene before simulation managers need the connection.

The runtime sends `connection`, `pong`, `expression`, `ask`, and `disconnect_properly` messages. It receives `ping`, `json_state`, and `json_output` from webplatform.

## Current Status

The package layout, assembly names, and runtime WebSocket dependency are now in place.

### New in this release

- **Dynamic species overrides in `SimulationManager`**: visual overrides are now centralized directly in `SimulationManager` (and `SimulationManagerSolo`) through inspector entries per species (`speciesId`). You can override prefab, scale, and color in one place. Species are auto-detected from incoming GAMA `properties`, persisted for later edits, and reset automatically when a new experiment is detected.
- **Real prefab import**: agents are no longer rendered as plain default cubes. The runtime now resolves prefabs by path through `Resources` and the Unity AssetDatabase, instantiates the actual visual asset, and only falls back to a procedural mesh (vehicle, character, or box) when the prefab cannot be found.
- **Automatic GAMA-driven coloring**: per-agent and per-property colors sent by GAMA (in `properties` and `attributes`: `rgb`, `rgba`, `color`, `shade`, `tint`, named CSS colors, hex strings, etc.) are detected automatically and applied to the imported prefab materials. The previous uniform "fallback grey/blue" behavior is now opt-in.

Known remaining work:

- Add required runtime resources for `Resources.Load` paths.
- Decide whether legacy geometry import/export should become a separate optional package.
- Verify import and compilation in a clean Unity 6 project.

## Unity Version

The source project targets Unity 6. The package manifest currently declares Unity `6000.0`.

## License

MIT License. See `LICENSE`.

Third-party notices are listed in `THIRD_PARTY_NOTICES.md`.
