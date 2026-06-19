# 4. Generate and Configure the Unity Preview

After validating the Play Mode, we'll generate a static preview to inspect the scene in
Unity Edit Mode.

The preview is useful because it lets you tune visual parameters without
launching the full live experiment every time.

## 4.1 Generate The Preview

Open **GAMA > GAMA Panel > Generate Preview from GAMA**.

![Generate Preview from GAMA button](../images/tutorial/03-generate-preview-button.png)

During capture, the GAMA Panel shows that the preview is being built.

![Preview building in the GAMA Panel](../images/tutorial/03-preview-building-panel.png)

GAMA may start or update the experiment while Unity receives the preview data.

## 4.2 Expected Result

The Unity scene should show the map and detected agents without entering Play
Mode.

The scene now contains the generated static preview.

![Generated static preview scene](../images/tutorial/03-static-preview-scene-built.png)

The GAMA Panel now contains the detected species settings.

![Captured preview species settings](../images/tutorial/03-preview-captured-species-settings.png)

## 4.3 Parameters You Can Modify In The Preview

For each detected species, the preview exposes visual settings that can later be
applied to Play Mode runtime agents:

- **Prefab**: replace the default GAMA geometry with a Unity prefab.
- **Color**: force a stable color for the species.
- **Scale**: change the visual size without changing the logical
- **Visible**: show or hide the species in preview and runtime.
- **Reset**: return the species to the values received from GAMA.

You can choose a prefab from the GAMA Panel.

![Change a prefab from the GAMA Panel](../images/tutorial/04-change-prefab-from-gama-panel.png)

### 4.4 Prefab Rules

For Edit Mode preview, Unity can use a direct prefab object reference.

For Play Mode runtime loading, the prefab should be under a Unity `Resources`
folder so it can be loaded with a resource path.

Recommended example:

```text
Assets/Resources/Visual Prefabs/Character/Ghost.prefab
```

Resource path:

```text
Visual Prefabs/Character/Ghost
```

## 4.5 Scale Rules

The scale multiplier is a visual multiplier.

It should not move the logical agent position or change the global runtime root.
For cell-like species, the logical parent should stay at scale `(1, 1, 1)` and
the visual child should receive the visual scale.

## Important Behavior

Generating a new preview should clean previous generated preview/runtime objects
before rebuilding the scene. This avoids visual superposition with older example
scenes or older previews.

## Result

At the end of this chapter, the static preview should look close to the desired
Unity scene, and the same species settings should be ready to reuse in Play
Mode.
