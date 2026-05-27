# 7. Optimize Large Simulations

This chapter covers performance options for large GAMA models.

Large models can send many objects or very large JSON messages. Examples include
large grids, city maps, or experiments with thousands of live agents.

## Symptoms

Common symptoms:

- Unity freezes during import;
- the console shows many JSON chunks;
- the hierarchy contains tens of thousands of objects;
- each tick updates many unchanged objects.

> Screenshot to add: Unity hierarchy or stats showing a very large object count.

> Screenshot to add: Console showing large/chunked GAMA messages.

## Performance Options

In the `Game Manager` / `Simulation Manager` inspector, use the performance and
streaming settings to control:

- incremental import;
- large species threshold;
- large geometry threshold;
- huge message byte threshold;
- maximum object updates per frame;
- skipping unchanged objects;
- culling agents outside the camera view or render distance.

> Screenshot to add: `Game Manager` performance and streaming settings.

## Expected Optimization Behavior

For large messages, Unity should log summary diagnostics such as:

```text
[GAMA][PERF][STREAM] ...
[GAMA][PERF][SPECIES] ...
[GAMA][PERF][IMPORT] ...
[GAMA][PERF][JSON] ...
```

> Screenshot to add: Console filtered on `[GAMA][PERF]`.

The first import can still be heavy. Later ticks should skip unchanged objects
whenever possible.

> Optional before/after screenshots to add: profiler or console timing before
> optimization, then after unchanged-object skipping.

## Rules For Large Models

- Do not reduce the GAMA model just to make Unity work.
- Do not hardcode species names for optimization.
- Prefer skipping unchanged objects before adding model-specific logic.
- Use batching only when object-level interaction is not needed.
