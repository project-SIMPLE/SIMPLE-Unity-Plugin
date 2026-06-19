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


## 4.3 Parameters You Can Modify In The Preview

For each detected species, the preview exposes visual settings that can later be
applied to Play Mode runtime agents:

![Captured preview species settings](../images/tutorial/03-preview-captured-species-settings.png)


1. **Info**: details about the captured static preview data.
2. **Prefab**: replace the default GAMA geometry with a Unity prefab.
3. **Color**: force a stable color for the species.
4. **Scale**: change the visual size without changing the logical scale.
5. **Visible**: show or hide the species in preview and runtime.
6. **Reset**: return the species to the values received from GAMA.
7. **Validate**: apply the settings to your Unity agents and close the panel.

## 4.4 Preview Configuration Example

With the **Prey Predator 7** model, start by checking that the static background
species is visible. Here, only the vegetation grid is clearly displayed in the
Unity preview.

![Preview with vegetation only](../images/tutorial/04-preview-vegetation-only.png)

The GAMA Panel can be used to keep the vegetation visible while preparing the
other species.

![Vegetation preview settings](../images/tutorial/04-preview-vegetation-settings.png)

Then enable the prey and predator species and increase their scale in the GAMA
Panel. Before assigning colors, they appear as grey points on top of the
vegetation grid.

![Grey prey and predator settings](../images/tutorial/04-preview-gray-agents-settings.png)

![Grey prey and predator result](../images/tutorial/04-preview-gray-agents-result.png)

Finally, assign stable colors to distinguish the two dynamic species. In this
example, prey are blue and predators are red.

![Colored prey and predator settings](../images/tutorial/04-preview-colored-agents-settings.png)

![Colored prey and predator result](../images/tutorial/04-preview-colored-agents-result.png)

## Important Behavior

Generating a new preview should clean previous generated preview/runtime objects
before rebuilding the scene. This avoids visual superposition with older example
scenes or older previews.

## Result

At the end of this chapter, the static preview should look close to the desired
Unity scene, and the same species settings should be ready to reuse in Play
Mode.
