# 7. Apply Preview Settings In Play Mode

This chapter closes the loop between Edit Mode preview and live Play Mode.

The goal is to confirm that the visual settings adjusted in the preview are
actually reused when Unity receives live GAMA agents.

## Validate The Preview Settings

After generating and configuring the preview, click **Validate Preview and Close
Panel** when the scene looks correct.

![Validate Preview and Close Panel](../images/tutorial/05-validate-preview-button.png)

Then press **Play** in Unity.

![Press Play from the preview scene](../images/tutorial/05-press-play-from-preview.png)

## What Should Happen In Play Mode

When Play Mode starts:

- the static preview root is hidden to avoid duplicate objects;
- Unity connects to `simple.webplatform`;
- live runtime agents are created under `[GAMA] Runtime Live Agents`;
- species settings from the preview are applied to live agents;
- dynamic attributes can keep changing per agent while the simulation runs.

Runtime agents are created under:

```text
[GAMA] Runtime Live Agents
```

When Play Mode works, the static preview objects are hidden and runtime objects
are created.

![Play Mode with runtime agents](../images/tutorial/05-play-mode-runtime-preview-hidden.png)

## What To Check

Check the following items after pressing Play:

- the species that received a prefab override uses that prefab at runtime;
- scale, position offset, rotation offset, visibility, and color overrides match
  the preview;
- fallback cubes still appear for species with no prefab;
- dynamic color rules still update per agent from GAMA attributes;
- static/background species are not removed just because a later live tick sends
  only dynamic agents.

## Player Position

By default, outgoing Unity player position should come from the Main Camera world
position.

This avoids sending the `Game Manager`, `Connection Manager`, or a fixed root
position as the player position.

Expected diagnostic shape:

```text
[GAMA][OUT][PLAYER_POS] source=MainCamera
```

## Result

At the end of this chapter, the workflow should be clear:

1. use Play Mode once to prove the live connection works;
2. generate a preview to tune the Unity representation faster;
3. validate the preview settings;
4. press Play again and check that those settings apply to live agents.

## Navigation

| Previous | Next |
|---|---|
| [6. Drive Dynamic Properties From GAMA Attributes](06-live-preview.md) | [8. Optimize Large Simulations](08-large-models-performance.md) |
