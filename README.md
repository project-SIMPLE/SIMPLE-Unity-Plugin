# SIMPLE Unity Plugin

`com.project-simple.unity-plugin` is a Unity Package Manager package for SIMPLE VR scenes that connect to the `simple.webplatform` middleware.

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
    "com.project-simple.unity-plugin": "file:../path/to/SIMPLE-Unity-Plugin"
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

## GAMA Defaults And Unity Corrections

When the middleware sends simulation properties, the `SimulationManager` imports them into two
components on the same GameObject:

- `GamaAgentSceneSettings` for visual defaults and manual per-agent corrections.
- `GamaPrefabSceneSettings` for prefab reference translation (GAMA keys -> Unity prefabs).

Imported GAMA defaults stay separate from manual Unity corrections, so new middleware updates can
refresh defaults without erasing local edits.

Use `GAMA/Setup Scene` to add both settings components to existing managers. In the inspector:

- `Defaults imported from GAMA properties` shows one entry per GAMA property id.
- `Ordered rule overrides` applies manual corrections with ordered filters (`propertyId`, `tag`, `prefab`, `agentName`, regex).
- `Instance corrections` tracks runtime agents by name, up to the configured limit.
- Enable the manual override toggles to correct color, relative scale, position offset, rotation offset, or visibility.
- `GamaPrefabSceneSettings > Imported GAMA prefab references` tracks prefab ids used by the experiment.
- Fill `unityPrefab` (or `unityResourcesPath`) to translate each GAMA prefab key to the Unity prefab to instantiate.
- Optional `Ordered key translations` lets you remap reusable key patterns without hardcoding shape logic.

Override precedence is deterministic:

1. GAMA property defaults.
2. Per-agent attributes from middleware messages.
3. Per-property manual overrides in Unity.
4. Ordered rule overrides in Unity.
5. Per-agent-instance manual overrides in Unity.

Prefab resolution precedence is deterministic:

1. Per-property mapping in `GamaPrefabSceneSettings`.
2. Ordered key translations in `GamaPrefabSceneSettings`.
3. `Resources.Load` fallback from normalized GAMA keys (path + filename variants).
4. Placeholder cube if nothing resolves.

## Current Status

The package layout, assembly names, and runtime WebSocket dependency are now in place.

Known remaining work:

- Add required runtime resources for `Resources.Load` paths.
- Decide whether legacy geometry import/export should become a separate optional package.
- Verify import and compilation in a clean Unity 6 project.

## Unity Version

The source project targets Unity 6. The package manifest currently declares Unity `6000.0`.

## License

MIT License. See `LICENSE`.

Third-party notices are listed in `THIRD_PARTY_NOTICES.md`.
