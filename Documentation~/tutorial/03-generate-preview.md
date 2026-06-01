# 3. Run the GAMA Experiment in Play Mode

Before using the Editor preview, first validate that the live runtime workflow
works from a clean Unity scene.

This chapter shows the baseline behavior: Unity enters Play Mode, connects to
`simple.webplatform`, receives the live GAMA simulation, and creates runtime
objects in the scene.

## Steps

1. Make sure the scene was prepared with **GAMA > GAMA Panel > Setup Scene**.
2. Start `simple.webplatform`.
3. Open the target experiment in GAMA.
4. Start or run the experiment from GAMA.
5. Press **Play** in Unity.

Press **Play** from the Unity scene.

![Press Play from the preview scene](../images/tutorial/05-press-play-from-preview.png)

Runtime agents are created under:

```text
[GAMA] Runtime Live Agents
```

When Play Mode works, Unity receives live objects from GAMA and updates them
while the experiment is running.

![Play Mode with runtime agents](../images/tutorial/05-play-mode-runtime-preview-hidden.png)

## Expected Result

During Play Mode:

- Unity connects to `simple.webplatform`;
- live agents are created, updated, and removed by stable agent id;
- static background species and dynamic agents are grouped by species;
- the Unity player or camera position can be sent back to GAMA when configured.

Dynamic agents should be synchronized by:

```text
speciesName + "::" + agentId
```

Expected behavior:

- existing agents update instead of duplicating;
- newborn agents appear;
- dead agents disappear after a complete live update;
- static/background species are not pruned just because they are absent from a
  dynamic tick.

## Why This Is Not Enough For Visual Setup

This direct Play Mode workflow proves that the connection works, but it is slow
for visual iteration.

Every time you want to check whether a species has the right prefab, scale,
color, visibility, or offset, you need to run the experiment again. That is why
the next chapter introduces the Editor preview: it lets you build a static
snapshot of the experiment in Unity, adjust the visual parameters there, and then
reuse those settings in Play Mode.

## If Runtime Agents Do Not Appear

Check:

- `simple.webplatform` is running;
- GAMA is running and the experiment is started;
- the Unity middleware port is `8080`;
- the scene contains a `Connection Manager` and a `Game Manager`;
- the GAMA model sends geometries through the Unity linker.
