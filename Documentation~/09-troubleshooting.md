# Troubleshooting

This page lists recurring problems encountered when connecting GAMA,
`simple.webplatform`, and Unity.

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

## `simple.webplatform` Fails To Start Because Of Invalid JSON

If `npm start` fails with an error similar to:

```text
SyntaxError: Bad escaped character in JSON
```

one of the JSON settings files contains an invalid path. On Windows, a single
backslash in JSON can be interpreted as an escape character.

Use one of these forms instead:

```json
"C:/Users/name/path/to/file"
```

or:

```json
"C:\\Users\\name\\path\\to\\file"
```

After fixing the JSON file, restart `simple.webplatform`.

## No Agents Appear In Unity After Play Mode Starts

If GAMA starts, Unity connects, but no simulation objects appear, first check
whether the GAMA experiment was correctly converted to `vr_xp`.

During conversion, each species that should appear in Unity must be exported in
the **Export species** step. Selecting **Next** without adding the species can
produce a `vr_xp` experiment that creates the Unity player but sends no agents.

The generated GAML model should contain one `unity_property` per exported
species and a `send_geometries` reflex similar to:

```gaml
reflex send_geometries {
	do add_geometries_to_send(prey, up_prey);
	do add_geometries_to_send(predator, up_predator);
	do add_geometries_to_send(vegetation_cell, up_vegetation_cell);
}
```

If Unity logs a JSON output with `names=0`, `pointsLoc=0`, or `pointsGeom=0`,
GAMA is probably sending an empty geometry payload.

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

## Runtime Agents Do Not Appear

Check the Unity Console for:

```text
[GAMA][RUNTIME][CONNECTION]
[GAMA][CONNECTION]
[GAMA][RUNTIME][FLOW]
```

If Unity logs that the socket is not open, verify that `simple.webplatform` is
running and that the Unity runtime/player WebSocket is available on `8080`.

If the socket is open but no objects are created, check that the GAMA `vr_xp`
experiment sends geometries for the expected species.

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

## Prefab Does Not Change In Play Mode

For Play Mode, the prefab must be loadable from a Unity `Resources` path.

If a prefab is outside a `Resources` folder, Edit Mode preview may still use it,
but runtime loading can fall back to the default GAMA geometry.

Move runtime prefabs under a `Resources` folder or use a prefab path that can be
resolved at runtime.

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
