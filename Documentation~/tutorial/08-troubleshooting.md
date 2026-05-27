# 8. Troubleshooting

This chapter lists common problems and the first checks to perform.

## No Preview Is Generated

Check:

- `simple.webplatform` is running;
- GAMA is running;
- the experiment is open or selected;
- the Unity middleware port is `8080`;
- the model sends geometries through the Unity linker.

> Screenshot to add: GAMA Panel state when preview generation fails.

> Screenshot to add: middleware terminal or console showing whether
> `simple.webplatform` is running.

## Runtime Agents Do Not Appear

Check the Unity console for:

```text
[GAMA][RUNTIME][CONNECTION]
[GAMA][CONNECTION]
[GAMA][RUNTIME][FLOW]
```

If Unity logs that the socket is not open, verify the middleware and connection
settings.

> Screenshot to add: Console filtered on `[GAMA][RUNTIME][CONNECTION]` and
> `[GAMA][CONNECTION]`.

> Screenshot to add: `Connection Manager` inspector with host and ports.

## Colors Do Not Follow Attributes

Check:

- the GAMA model sends the attribute in `add_geometries_to_send(...)`;
- Unity receives non-empty attributes;
- the species dynamic color mode is enabled;
- the selected attribute name matches the GAMA attribute key exactly;
- discrete rules match the received values.

> Screenshot to add: Dynamic Color setup next to a console log proving whether
> attributes were received.

## Prefab Does Not Change In Play Mode

For Play Mode, the prefab should be loadable from a Unity `Resources` path.

If a prefab is outside `Resources`, Edit Mode preview can use it, but runtime
loading may fallback and log a warning.

> Screenshot to add: prefab outside `Resources` warning in the console.

## Scale Is Too Large In Play Mode

Check:

- the scale multiplier is not applied twice;
- the species override asset contains the expected context-specific entry;
- cell-like species keep their logical parent at scale `(1, 1, 1)`;
- the visual child receives the visual scale.

> Screenshot to add: inspector showing scale fields in the `Game Manager`.

> Screenshot to add: hierarchy showing logical parent scale and visual child
> scale.

## Agents Freeze Or Accumulate

For dynamic species, check that the live updates are complete or cumulative.
Unity should remove dynamic agents only after a complete update confirms they are
missing.

Static/background species should not be pruned just because they are absent from
a dynamic tick.

> Optional GIF to add: predator/prey or dynamic agents disappearing and
> appearing correctly during Play Mode.
