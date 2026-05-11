# GAMA Unity Package

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

The package is actively maintained and includes a large set of runtime fixes and optimization work completed during recent integration sessions.

### Recently Completed Work (Summary)

#### Connectivity and Runtime Safety

- WebSocket flow is stabilized around middleware messages (`json_state`, `json_output`, `ping/pong`) with safer state transitions in `ConnectionManager`.
- `SimulationManager` got additional null-guards in critical loops (`FixedUpdate`, player update path, camera/converter checks) to prevent repeated runtime exceptions.
- Missing-script related regressions were handled in package code paths (editor-side temporary maintenance helper was added then removed on request).

#### Serialization and Color Robustness

- `Attributes` helper accessors are implemented and used by simulation settings:
  - `TryGetBool`
  - `TryGetFloat`
  - `TryGetVector3`
  - `TryGetString`
- GAMA color parsing now supports multiple forms consistently:
  - integer lists (`color`, `rgb`, `rgba`)
  - float lists (`rgbFloat`, `rgbaFloat`)
  - packed/hex/named/string variants
- `GamaVisualUtility` color extraction was fixed to avoid int/float list type mismatches.

#### Prefab Resolution and Overrides

- Per-species and per-property visual override workflows are integrated through runtime settings and inspector tooling.
- Prefab resolution precedence remains deterministic:
  1. explicit property mapping
  2. key translations
  3. `Resources.Load` normalized lookup
  4. placeholder fallback
- Missing prefab warnings are deduplicated to reduce log spam.

#### Geometry and Scene Rendering Pipeline

- Polygon/road extrusion path was restored and hardened (`PolyExtruderLight`) to avoid broken route rendering.
- Camera-driven streaming/culling was implemented and iterated:
  - frustum-based visibility
  - optional render distance + hysteresis (primarily for prefab agents)
  - budgeted round-robin evaluation per tick
  - immediate visibility update during geometry application to reduce one-frame flicker
- Culling now uses Game camera logic (not SceneView) for runtime decisions.
- Init-loaded objects are tracked in `geometryMap` so they can be managed by streaming logic afterward.

#### Performance-Oriented Runtime Controls

- Prefab pooling lifecycle was added:
  - `TryGetPooledPrefab`
  - `ReleasePrefabInstance`
  - `DrainPrefabPools`
- GPU instancing enablement is applied on shared materials without creating per-instance material copies.
- Agent update throttling was introduced:
  - max processed agents per tick
  - resumable processing across ticks
  - optional diagnostics for processing budget
- Streaming diagnostics and throttled logs were added for runtime tuning.

#### Flicker / Visibility Regression Fixes

- Multiple visibility regressions were addressed:
  - transient global reactivation when camera reference is unavailable
  - first-frame loaded structures staying visible forever
  - over-aggressive missing-data cull causing mass disappearances
- Missing-data based culling behavior was constrained to avoid destroying non-prefab geometry by default.

#### Rebase / Merge Recovery Work

- After a problematic rebase, residual conflict markers were removed from:
  - `SimulationManager.cs`
  - `Attributes.cs`
  - `ConnectionManager.cs`
  - `Editor/SimulationManagerInspector.cs`
- Broken mixed blocks were replaced with coherent file versions to restore compile viability.

#### Scope Discipline

- Changes were kept in the package workspace (`unity-package-template`) with explicit user-driven constraints to avoid modifying unrelated repositories.

### Remaining Validation Items

- Validate final compilation and play mode in a clean Unity 6 environment.
- Re-check XR/OpenXR project-specific warnings (`OpenXRPackageSettings.asset`) on the test project side.
- Tune runtime culling/update budgets (`prefabStreamingBudgetPerTick`, update limits, hysteresis) according to target hardware.

## Unity Version

The source project targets Unity 6. The package manifest currently declares Unity `6000.0`.

## License

MIT License. See `LICENSE`.

Third-party notices are listed in `THIRD_PARTY_NOTICES.md`.
