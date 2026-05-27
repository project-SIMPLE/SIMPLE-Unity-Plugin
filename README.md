# SIMPLE Unity Plugin

The SIMPLE Unity Plugin is a Unity Package Manager package designed to connect Unity scenes with GAMA simulations through the SIMPLE Webplatform middleware.

It provides a lightweight Unity workflow to set up a scene, connect to the middleware, generate a static preview from an experiment opened in GAMA, adjust species visual settings, and run the simulation in Play Mode. The package is designed to work without modifying `simple.webplatform`: the middleware is launched separately and Unity connects to it through WebSocket.

The current demo workflow is centered around the **GAMA Panel**, which groups scene setup, preview generation, species settings, and runtime visualization tools in one place.

## Documentation

- [Tutorial](Documentation~/tutorial/README.md)
- [User guide draft](Documentation~/user-guide/README.md)
- [Technical documentation draft](Documentation~/technical/README.md)
- [Runtime architecture notes](Documentation~/gama-unity.md)

---

## Installation

### Install from Git URL

In Unity:

1. Open **Window > Package Manager**.
2. Click the **+** button.
3. Select **Add package from git URL...**.
4. Paste one of the following URLs.

For the main branch :

```text
https://github.com/project-SIMPLE/SIMPLE-Unity-Plugin.git
```


For a branch :

```text
https://github.com/project-SIMPLE/SIMPLE-Unity-Plugin.git#name-of-the-branch
```


### Install from local disk

For local development, you can also install the package from disk:

1. Open **Window > Package Manager**.
2. Click the **+** button.
3. Select **Add package from disk...**.
4. Select this package's `package.json`.

For local project testing, you can also add the package directly to a Unity project's `Packages/manifest.json`:

```json
{
  "dependencies": {
    "com.project-simple.gama-unity": "file:../path/to/SIMPLE-Unity-Plugin"
  }
}
```

Do not add another copy of NativeWebSocket unless you remove the vendored copy from this package.

---

## Requirements

- Unity 6.x
- GAMA
- SIMPLE Webplatform middleware
- Middleware headset/player WebSocket port: `8080`
- GAMA connected to the middleware
- `simple.webplatform` launched manually

This Unity package does **not** modify or automatically launch `simple.webplatform`. The middleware is expected to be started separately.

The runtime client connects to the webplatform headset WebSocket endpoint, not directly to GAMA Server:

- Unity headset/runtime client: `ws://<webplatform-host>:8080/`
- Web monitor UI: `ws://<webplatform-host>:8001/`
- GAMA Server, used internally by webplatform: `ws://<gama-host>:1000/`

---

## Quick Start

1. Start `simple.webplatform` manually.
2. Start GAMA.
3. Open or select the target experiment in GAMA.
4. Open Unity.
5. Open **GAMA > GAMA Panel**.
6. Run **Setup Scene** if needed.
7. Use **Generate Preview from GAMA**.
8. Adjust species settings.
9. Apply settings to the preview.
10. Press **Play**.

The experiment does **not** need to be already running in GAMA. It only needs to be open or selected. Unity reproduces a Play-like middleware initialization flow through port `8080` to generate the static preview.

---

## Main Demo Workflow

### 1. Prepare GAMA and the middleware

Start `simple.webplatform`, then start GAMA and open or select the experiment you want to preview.

The standard preview workflow does not require the middleware catalogue, `settings.json`, or `LEARNING_PACKAGE_PATH`.

### 2. Generate a static preview

Open the **GAMA Panel** in Unity and click:

```text
Generate Preview from GAMA
```

Unity connects to the existing middleware on port `8080`, receives JSON data, and builds a static preview in the Unity scene.

### 3. Adjust species settings

After the preview is generated, detected species appear in the GAMA Panel and in the Simulation Manager Inspector.

For each species, you can adjust:

- Prefab Override
- Resources Path Override
- Color Override
- Scale Multiplier
- Position Offset
- Rotation Offset
- Visibility

These settings can be edited from the GAMA Panel or later from the **Game Manager / Simulation Manager Inspector**.

### 4. Press Play

When Play Mode starts:

- the static preview is hidden to avoid duplicates;
- live runtime agents are created from middleware data;
- species settings are applied to live agents;
- runtime agents are grouped by species under:

```text
[GAMA] Runtime Live Agents
├── species_name
│   ├── agent_0
│   ├── agent_1
│   └── ...
```

---

## GAMA Panel Overview

The **GAMA Panel** is the main entry point for the demo workflow.

It includes:

- **Setup Scene**  
  Prepares the Unity scene for GAMA communication and runtime visualization.

- **Workspace Explorer**  
  Allows browsing local GAMA workspaces and experiments.

- **Preview from the Open GAMA Experiment**  
  Generates a static preview from the experiment currently opened or selected in GAMA.

- **Species Settings**  
  Lets users adjust visual settings per species.

- **Advanced Sections**  
  Contains diagnostics, captured JSON tools, and legacy catalogue-based workflows.

Advanced sections are collapsed by default and are not required for the standard demo workflow.

---

## Preview from the Open GAMA Experiment

This is the main preview workflow.

It uses the middleware player WebSocket endpoint:

```text
ws://localhost:8080/
```

It does not require the middleware catalogue or `settings.json` workflow.

The preview process:

1. Connects to the middleware.
2. Reproduces a Play-like initialization sequence.
3. Receives JSON output from GAMA through the middleware.
4. Builds a static Unity preview.
5. Detects species from the generated preview.

Detected species become available after preview generation.

---

## Species Settings

Species settings are shared between:

- the static preview in Edit Mode;
- live runtime agents in Play Mode;
- the Simulation Manager Inspector.

Available settings:

- **Prefab Override**  
  Assign a Unity prefab to represent this species.

- **Resources Path Override**  
  Use a prefab available through Unity `Resources`.

- **Color Override**  
  Override the color received from GAMA.

- **Scale Multiplier**  
  Adjust the visual size of all agents of this species.

- **Position Offset**  
  Shift all agents of this species.

- **Rotation Offset**  
  Rotate all agents of this species.

- **Visibility**  
  Show or hide one species.

You can still adjust these settings later from the **Game Manager / Simulation Manager Inspector**.

---

## Runtime Play Mode

During Play Mode, Unity connects to the middleware and receives live simulation data.

The static preview is hidden automatically to avoid visual duplicates. Runtime agents are then created live and organized under:

```text
[GAMA] Runtime Live Agents
```

Each species gets its own parent object in the hierarchy, making the scene easier to inspect during demos.

Example:

```text
[GAMA] Runtime Live Agents
├── road
│   ├── road_0
│   ├── road_1
│   └── ...
├── building
│   ├── building_0
│   ├── building_1
│   └── ...
├── pedestrian
│   ├── pedestrian_0
│   ├── pedestrian_1
│   └── ...
└── car
    ├── car_0
    ├── car_1
    └── ...
```

---

## Simulation Manager Inspector

The Simulation Manager Inspector is organized into compact sections:

- **GAMA Species Overview and Attributes**  
  Shows detected species, GAMA attributes, and Unity overrides.

- **Rendering Settings**  
  Controls camera visibility, render distance, and rendering-related options.

- **Preview and Play Settings**  
  Controls synchronization between the static preview and Play Mode.

- **Scene References**  
  Contains scene and XR references required by the setup.

- **Interaction Scenario**  
  Contains scenario-specific settings such as hotspots, vehicles, day/night logic, GAMA asks, and interaction feedback.

- **Performance and Streaming**  
  Contains runtime performance options such as object pooling, update budgets, culling, and streaming.

- **Advanced Debug**  
  Contains troubleshooting and verbose diagnostic settings.

The most important demo sections appear first. Advanced performance and debug options are collapsed by default.

---

## Prefabs and Fallbacks

The package can resolve prefabs from Unity `Resources`.

If no prefab is found for a species, a fallback cube is used so the agent remains visible in the scene.

Species can be customized through:

- prefab overrides;
- Resources path overrides;
- per-species visual settings.

Default primitive suggestions and improved prefab recommendations in the GAMA Panel are planned for future updates.

---

## Importing Prefabs

Prefab import from the GAMA Panel is currently being improved.

The intended workflow is to copy external prefabs into a Unity `Resources` folder so they can be mapped to GAMA species.

Target import folder:

```text
Assets/Resources/GAMAImportedPrefabs/
```

Recommended supported assets:

- `.prefab`
- `.fbx`
- `.obj`
- `.mat`
- `.png`
- `.jpg`
- `.jpeg`
- `.tga`

External scripts should not be imported by default.

---

## Workspace Explorer

The Workspace Explorer allows users to inspect a local GAMA workspace without requiring middleware connectivity.

It can:

- choose a workspace folder path;
- scan `.gaml` files;
- discover declared `experiment` blocks;
- list experiments with a heuristic capability label:
  - `VR`
  - `Non-VR`
  - `VR + Non-VR`
  - `Unknown`

Notes:

- capability detection is heuristic and depends on keywords or metadata present in experiment blocks;
- invalid paths, missing metadata, and permission issues are handled without crashing the editor;
- Workspace Explorer is useful for browsing experiments, but the main preview workflow is based on the experiment currently opened or selected in GAMA.

---

## Package Contents

- `Runtime/`  
  Webplatform connection, simulation, serialization, localization, movement, preview, and geometry utilities.

- `Runtime/ThirdParty/NativeWebSocket/`  
  Vendored NativeWebSocket transport used by the runtime client.

- `Editor/`  
  GAMA Panel, workspace explorer, setup tools, preview tools, inspector tooling, and editor-only utilities.

- `Samples~/`  
  Importable Unity scenes and templates.

- `Tests/`  
  Runtime and editor test assemblies.

- `Documentation~/`  
  Package notes and protocol details.

---

## Troubleshooting

### No preview is generated

Check that:

- `simple.webplatform` is running;
- GAMA is running;
- the target experiment is open or selected in GAMA;
- middleware port `8080` is available.

### Species list is empty

Generate a preview first and make sure JSON output was received from the middleware.

### Preview duplicates live agents

Enable:

```text
Hide Preview During Play
```

### Settings do not affect Play Mode

Enable:

```text
Apply Preview Settings to Play
```

Then apply or save the species settings again.

### Missing prefabs

Use either:

- Prefab Override;
- Resources Path Override.

If no prefab is found, the fallback cube is used.

### Unity does not refresh the package

Try one of the following:

- close and reopen Unity;
- remove `Packages/packages-lock.json` from the Unity project;
- reinstall the package from the Git URL;
- use the fixed commit URL for a stable demo version.

---

## Known Limitations

- Compatibility still needs to be tested on more GAMA experiments.
- Validation on real SIMPLE/GAMA projects is still pending.
- The advanced catalogue / `settings.json` workflow is not the main demo workflow.
- Prefab import still needs final hardening.
- Documentation will evolve after more experiment compatibility tests.
- Default primitive and prefab suggestions in the GAMA Panel are planned but not fully finalized.

---

## Roadmap

- Test on more GAMA experiments.
- Test on current real SIMPLE/GAMA projects.
- Improve default prefab suggestions in the GAMA Panel.
- Improve prefab import workflow.
- Generate full user documentation.
- Generate developer documentation.
- Prepare demo screenshots or a demo video.

---

## Development Notes

- Do not modify `simple.webplatform` from this package.
- The middleware is launched separately.
- Runtime code should avoid Editor-only dependencies where possible.
- Demo UI must remain in English.
- Advanced options should remain collapsed unless needed.
- The package should remain installable through Unity Package Manager.

---

## Unity Version

The package targets Unity 6.x.

The package manifest currently declares Unity `6000.0`.

---

## License

MIT License. See `LICENSE`.

Third-party notices are listed in `THIRD_PARTY_NOTICES.md`.
