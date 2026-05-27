# 3. Generate a GAMA Preview in Unity

This chapter creates a static preview from the GAMA experiment currently opened
or selected in GAMA.

## Steps

1. Start `simple.webplatform`.
2. Start GAMA.
3. Open or select the target experiment in GAMA.
4. Open Unity.
5. Open **GAMA > GAMA Panel**.
6. Click **Generate Preview from GAMA**.

Start the middleware before generating the preview.

![Open the middleware](../images/tutorial/02-open-middleware.png)

Open the preview workflow from the GAMA Panel.

![Open GAMA Preview menu](../images/tutorial/03-open-gama-preview-menu.png)

The preview page exposes the GAMA preview controls.

![GAMA Preview page](../images/tutorial/03-gama-preview-page.png)

Click **Generate Preview from GAMA**.

![Generate Preview from GAMA button](../images/tutorial/03-generate-preview-button.png)

Unity receives JSON output from the middleware and builds a static preview under:

```text
[GAMA] Static Experiment Preview
```

During capture, the GAMA Panel shows that the preview is being built.

![Preview building in the GAMA Panel](../images/tutorial/03-preview-building-panel.png)

GAMA may start or update the experiment while Unity receives the preview data.

![GAMA running during preview capture](../images/tutorial/03-gama-running-during-preview-capture.png)

Wait until Unity finishes building the scene.

![Wait while Unity builds the preview](../images/tutorial/03-wait-preview-building.png)

## Expected Result

The Unity scene should show the map and detected agents without entering Play
Mode.

The GAMA Panel should list detected species in a table similar to:

```text
Agent / Species | Count | Prefab | Color | Scale | Visible | Reset
```

The scene now contains the generated static preview.

![Generated static preview scene](../images/tutorial/03-static-preview-scene-built.png)

The GAMA Panel now contains the detected species settings.

![Captured preview species settings](../images/tutorial/03-preview-captured-species-settings.png)

## Important Behavior

Generating a new preview should clean previous generated preview/runtime objects
before rebuilding the scene. This avoids visual superposition with older example
scenes or older previews.

> Optional before/after screenshots to add: an old generated scene before
> preview, then the cleaned scene after preview generation.

## If Nothing Appears

Check:

- `simple.webplatform` is running;
- GAMA is running;
- the experiment is opened or selected;
- Unity uses the middleware port `8080`;
- the GAMA model sends at least one geometry.
