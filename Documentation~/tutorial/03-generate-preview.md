# 3. Generate a GAMA Preview in Unity

This chapter creates a static preview from the GAMA experiment currently opened
or selected in GAMA.

## Steps

1. In `simple.webplatform`, start the middleware before generating the preview.
![Open the middleware](../images/tutorial/02-open-middleware.png)
3. Open or select the target experiment in GAMA.
4. Open **GAMA > GAMA Panel > Generate Preview from GAMA**.
![GAMA Preview page](../images/tutorial/03-gama-preview-page.png)
Click **Generate Preview from GAMA**.
![Generate Preview from GAMA button](../images/tutorial/03-generate-preview-button.png)

During capture, the GAMA Panel shows that the preview is being built.

![Preview building in the GAMA Panel](../images/tutorial/03-preview-building-panel.png)

GAMA may start or update the experiment while Unity receives the preview data.

![GAMA running during preview capture](../images/tutorial/03-gama-running-during-preview-capture.png)

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


## If Nothing Appears

Check:

- `simple.webplatform` is running;
- GAMA is running;
- the experiment is opened or selected;
- Unity uses the same port as the middleware;
- the GAMA model sends at least one geometry.
