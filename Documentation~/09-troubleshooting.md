# Troubleshooting

This page lists recurring problems encountered when connecting GAMA,
`simple.webplatform`, and Unity.

Use it as a first diagnostic checklist before changing code.

## Quick Checks

Before investigating a specific symptom, check the following points:

- GAMA is open and the target experiment is selected.
- The experiment used by Unity is the generated `vr_xp` experiment withe the Unity Plugin, not the
  original GAMA experiment.
- `simple.webplatform` is running with `npm start`.
- The Unity package is up to date.

## Port Reference

`simple.webplatform` exposes several ports. They do not have the same role:

```text
8000: web interface opened in the browser
8001: monitor WebSocket used by Unity to drive preview capture
8080: Unity player/runtime WebSocket used in Play Mode
1000: GAMA Server behind simple.webplatform
```

Seeing the web interface on `http://localhost:8000/` does not guarantee that
the monitor socket on `8001` or the Unity player socket on `8080` is running.

## GAMA Model Was Not Converted To `vr_xp`

The original GAMA experiment cannot be used directly by Unity. It must first be
converted into a Unity-compatible `vr_xp` experiment with the SIMPLE Unity
plugin for GAMA.

If this step is skipped, Unity may connect to `simple.webplatform`, but it will
not receive the expected Unity geometry data.

## Species Were Not Exported During `vr_xp` Conversion

During the `vr_xp` conversion, each species that should appear in Unity must be
explicitly exported in the **Export species** step withe the Unity Plugin in GAMA.

Do not simply click **Next** through the conversion wizard. Select each species
on the left and click **+** under **Aspect in Unity**.

If Unity logs a JSON output with `names=0`, `pointsLoc=0`, or `pointsGeom=0`,
GAMA is probably sending an empty geometry payload.

## Generate Preview Button Is Used Before GAMA Is Ready

Before using **Generate Preview from GAMA**, the target experiment must be
selected or already open in GAMA. For the tutorial workflow, start the
`vr_xp` experiment in GAMA first, then generate the preview from Unity.

If the experiment is not selected or not ready, Unity may connect to the
middleware but fail to capture useful geometry data.

Steps **in this exact order**:

1. Start `simple.webplatform` with `npm start`.
2. Open the `vr_xp` experiment in GAMA.
3. Then, and only then, click **Generate Preview from GAMA** in Unity.

## Generate Preview Says That `localhost:8001` Is Not Responding

Unity shows this kind of message:

```text
The existing monitor ws://localhost:8001/ is not responding.
Launch simple.webplatform manually, then try again.
```

This means Unity cannot reach the `simple.webplatform` monitor WebSocket. The
browser page on `8000` may still be visible, but the backend service needed by
Unity is not responding on `8001`.

Check that `simple.webplatform` was started with:

```bash
npm start
```

Then check that the monitor and player sockets are listening.

On macOS or Linux:

```bash
lsof -nP -iTCP:8001 -sTCP:LISTEN
lsof -nP -iTCP:8080 -sTCP:LISTEN
```

On Windows:

```powershell
netstat -ano | findstr :8001
netstat -ano | findstr :8080
```

If only port `8000` is open, the web interface is running but Unity cannot
generate the preview. Restart `simple.webplatform` and check the terminal for
backend errors.

## Unity Connects But No Agents Appear In Play Mode

If GAMA starts, Unity connects, but no simulation objects appear, check the
following points:

- the experiment is the generated `vr_xp` experiment;
- species were exported during the `vr_xp` conversion;
- the generated model contains `add_geometries_to_send(...)`;
- `simple.webplatform` is listening on `8080`;
- the Unity Console does not show repeated socket close messages;
- the GAMA experiment is actually sending geometry updates.

Useful Unity Console filters:

```text
[GAMA][RUNTIME][CONNECTION]
[GAMA][CONNECTION]
[GAMA][RUNTIME][FLOW]
```

If the socket is open but the JSON output is empty, the problem is usually on
the GAMA model/export side, not on Unity rendering.

## `unity_linker` Or Player Creation Fails

Sometimes GAMA or `simple.webplatform` may report that the Unity linker/player
could not be created. This can happen when the middleware keeps stale state or
when the `vr_xp` experiment is incomplete.

Fix:

1. Stop the GAMA experiment.
2. Stop `simple.webplatform`.
3. Restart `simple.webplatform`.
4. Re-open the experiment in GAMA.
5. Enter Play Mode again in Unity.

If the issue persists, inspect the generated GAML model:

- `unity_linker_species` should point to the linker species.
- `create_player(string id)` should ask the `unity_linker` to create the player.
- `player_unity_properties` should not point to missing or invalid properties.
- the species to display should be sent with `add_geometries_to_send(...)`.

## GAMA Reports A Nil Value Error

A GAMA error such as `nil value detected` can break the Unity flow even if the
middleware and Unity are running.

Check:

- the generated `vr_xp` model compiles without errors;
- exported Unity properties are initialized before they are used;
- `player_unity_properties` does not contain an invalid property;
- the experiment was regenerated after changing exported species;
- GAMA and `simple.webplatform` were both restarted after a failed connection.

If the error happens during `create_player`, regenerate the `vr_xp` experiment
and make sure the Unity linker and exported species are present.

## No Preview Is Generated

Check:

- `simple.webplatform` is running;
- GAMA is running;
- the target experiment is selected or already open in GAMA;
- the GAMA experiment was converted to `vr_xp`;
- the Unity player socket uses port `8080`;
- the monitor socket on `8001` is reachable;
- the GAMA model sends geometries through the Unity linker.

If the preview still fails, open the Unity Console and search for:

```text
[GAMA][CAPTURE]
[GAMA][PREVIEW]
[GAMA][CONNECTION]
```

## Preview Capture Is Cancelled Or Incomplete

If the preview ends with a cancelled or incomplete capture, the most common
causes are:

- the GAMA experiment was not ready when the capture started;
- the monitor socket on `8001` disconnected;
- the player socket on `8080` did not receive `json_output`;
- GAMA was still starting or compiling the experiment;
- an old middleware/player state was still active.

Fix:

1. Stop Play Mode in Unity.
2. Stop or reset the GAMA experiment.
3. Restart `simple.webplatform`.
4. Start the `vr_xp` experiment in GAMA.
5. Generate the preview again.

If the second attempt works, the first capture most likely started before all
services were synchronized.

## Preview Settings And Game Manager Settings Seem To Do The Same Thing

The preview panel and the `Game Manager` Inspector edit the same species visual
override data.

Changing values in the preview panel updates the preview scene and stores the
settings used later by Play Mode. Editing the same species in the `Game Manager`
Inspector changes the same kind of data.

The **Validate Preview and Close Panel** button is mainly a workflow action:

- it applies the current preview settings;
- it keeps the generated preview available in Edit Mode;
- it closes the preview panel;
- it makes the chosen settings ready for reuse in Play Mode.

If the scene already updates while editing values, this is expected. The
validate button confirms and exits the preview workflow.

## Colors Do Not Follow Attributes

Unity can only use attributes that GAMA explicitly sends.

Check:

- the GAMA model sends the attribute in `add_geometries_to_send(...)`;
- Unity receives non-empty attributes;
- the species dynamic color override is enabled;
- the selected attribute name matches the GAMA attribute key exactly;
- discrete rules match the received values.

For example, to expose a `food` attribute on `vegetation_cell`:

```gaml
list<float> grass_food <- vegetation_cell collect each.food;
map<string, list<float>> grass_atts <- ["food":: grass_food];

do add_geometries_to_send(vegetation_cell, up_vegetation_cell, grass_atts);
```

If `food` is not sent by GAMA, it will not be available in Unity dynamic color
settings.

## Discrete Dynamic Colors Cannot Be Tested On The Tutorial Model

Discrete dynamic colors require an attribute with distinct values, for example a
state, status, type, or category.

If the current model only exposes continuous numeric values such as `food`, use
the continuous dynamic color mode instead.

To test discrete colors, first add or expose a categorical GAMA attribute, then
send it through `add_geometries_to_send(...)` and configure the discrete mapping
in Unity.

## Prefab Does Not Change In Play Mode

For Play Mode, the prefab must be loadable from a Unity `Resources` path.

If a prefab is outside a `Resources` folder, Edit Mode preview may still use it,
but runtime loading can fall back to the default GAMA geometry.

Move runtime prefabs under a `Resources` folder or use a prefab path that can be
resolved at runtime.

## FPS Player Prefab Does Not Work

The default FPS player prefab may not be configured for every project. This does
not prevent GAMA agents from being imported, but it can make navigation or
interaction unusable.

Fix:

- use the default scene setup only as a connection baseline;
- replace the FPS player with a project-specific player prefab if needed;
- check camera, input, colliders, and movement scripts before testing
  interaction.

## Scene Is Not Saved After Default Setup

Unity does not always save the scene automatically after pressing **Default
Setup** in the GAMA Panel.

Fix:

1. Press **Default Setup**.
2. Check that the scene contains the required managers.
3. Save the scene manually with **File > Save**.

If the scene is not saved, the required objects may disappear after reopening
the project.

## Scale Looks Different Between Preview And Play Mode

The scale multiplier is a visual override. It should change the size of each
agent visual without changing the logical spread of the species.

If the species looks stretched or contracted as a whole:

- check that the latest Unity package version is installed;
- regenerate the preview;
- reset the affected species override;
- verify that the scale is applied to agent visuals, not to the common species
  parent object.

Cell-like background species and moving agent species can have very different
geometry shapes, so compare the result in both Edit Mode preview and Play Mode.

## Geometry Mesh Needs Height Instead Of Scale

When no Unity prefab is assigned, Unity uses the geometry mesh sent by GAMA. In
that case, changing a global visual scale may not be the best control for flat
or background geometries.

Workaround:

- keep the scale multiplier close to `1` for background meshes;
- adjust the geometry height/depth in the GAMA Unity aspect when possible;
- use prefabs when per-agent visual control is required.

This is a known usability limitation: a dedicated height control for GAMA
geometry meshes would be clearer than a generic scale control.

## Play Mode Start Or Stop Is Slow

Starting or stopping Play Mode may take several seconds on large scenes or when
the connection stack is restarting.

Possible causes:

- WebSocket initialization or shutdown;
- large numbers of runtime agents;
- repeated console logging;
- Unity recompilation or scene reload;
- `simple.webplatform` reconnecting or purging old players.

Fix:

- use the `main` package branch for user testing because it has reduced default
  logging;
- use the `develop` branch only when detailed logs are needed;
- keep the Unity Console collapsed when testing large simulations;
- restart `simple.webplatform` if old sockets remain open.

## Runtime Appearance Changes Cause Performance Spikes

Changing colors, scale, visibility, or prefab overrides during Play Mode can
trigger updates on many agents at once.

This is expected to be more expensive on large simulations.

Workaround:

- configure species appearance in Edit Mode preview when possible;
- avoid repeatedly changing overrides while thousands of agents are active;
- use reduced logging for user testing;
- test large visual changes on a smaller model first.

## Editor Becomes Slow When Editing Game Manager Values

In Edit Mode, changing `Game Manager` Inspector values can immediately update
the preview. On large previews, this can cause editor stutter.

Workaround:

- prefer the GAMA Panel preview workflow for common appearance changes;
- avoid dragging sliders continuously on very large scenes;
- type numeric values directly instead of scrubbing controls;
- hide or reduce the number of visible species while configuring one species.

A future improvement would be a manual **Apply** button for expensive preview
updates instead of applying every editor value change immediately.

## Simulation Stops When Unity Loses Focus

If Unity is not the focused application during runtime, the connection can slow
down, pause, or be closed depending on the platform and middleware state.

Workaround:

- keep the Unity window focused while testing Play Mode;
- avoid switching to another application during connection-sensitive tests;
- if the socket closes, stop Play Mode, restart `simple.webplatform`, and run
  the experiment again.

## Agents Freeze Or Accumulate

For dynamic species, Unity should receive complete enough updates to know which
agents still exist.

If agents accumulate or freeze:

- check that GAMA is still running;
- check that Unity is still connected to `simple.webplatform`;
- verify that the runtime socket on `8080` remains open;
- make sure the model sends regular geometry updates for dynamic species.

Static or background species should not be removed just because they are absent
from a dynamic tick.

## Background Geometry Is Not Explicitly Defined During GAMA Conversion

The GAMA Unity conversion wizard currently exports species and aspects, but it
does not always make the distinction between dynamic agents and background
geometry obvious.

Workaround:

- export background species separately;
- configure their appearance in Unity preview;
- keep background species visible and stable;
- hide or reduce dynamic species while configuring the background.

For road/building/static geometry workflows, it is useful to keep those species
as separate exported entries so their Unity appearance can be controlled
independently.

## Background Geometry Changes Seem To Be Applied Repeatedly

If background geometry or static species appear to be modified continuously,
check whether GAMA is resending the same static geometry every cycle.

Workaround:

- keep background species separate from dynamic species;
- avoid sending unnecessary changing attributes for background geometry;
- configure background appearance once in Unity preview;
- hide dynamic species temporarily while checking the background;
- if possible, send static/background geometry less frequently than dynamic
  agents.

A future improvement on the GAMA conversion side would be an explicit
background-geometry option and a way to avoid repeatedly applying unchanged
background visual data.

## Too Many Debug Logs In Unity

Very frequent `Debug.Log` calls can slow down Unity, especially when thousands
of agents are updated.

For user testing, use the `main` branch of the Unity package, where default
logging is reduced.

Use the `develop` branch only when collecting detailed logs for debugging.

A future improvement would be a runtime **Developer/Debug mode** option instead
of requiring a separate branch for verbose logs.
