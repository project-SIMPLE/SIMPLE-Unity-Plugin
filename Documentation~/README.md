# SIMPLE Unity Plugin Documentation

This folder contains the GitHub-readable documentation for the SIMPLE Unity Plugin.

Start with the tutorial first. The user guide and technical documentation follow the
same structure so each tutorial chapter can later be expanded into reference
documentation.

## Tutorial

- [Tutorial overview](tutorial/README.md)
- [1. Install the Unity package](tutorial/01-installation-and-setup.md)
- [2. Prepare a GAMA experiment for Unity](tutorial/02-gama-model-preparation.md)
- [3. Generate a GAMA Preview in Unity](tutorial/03-generate-preview.md)
- [4. Configure species visuals](tutorial/04-configure-species.md)
- [5. Run the Live Preview from GAMA](tutorial/05-live-preview.md)
- [6. Drive colors from GAMA attributes](tutorial/06-dynamic-colors.md)
- [7. Optimize large simulations](tutorial/07-large-models-performance.md)
- [8. Troubleshooting](tutorial/08-troubleshooting.md)

## Documentation To Expand

- [User guide](user-guide/README.md)
- [Technical documentation](technical/README.md)
- [Runtime architecture notes](gama-unity.md)

## Screenshots And Media

Store screenshots in [images](images/README.md).

Use this convention when replacing screenshot placeholders:

```md
![Short description](../images/tutorial/file-name.png)
```

If a step is easier to understand with motion, add a short GIF or video next to
the screenshot.

## Writing Workflow

1. Complete the tutorial chapters first.
2. Validate the tutorial on one small model and one large model.
3. Convert each tutorial chapter into user-guide pages.
4. Add technical details only after the workflow is stable.
5. Keep screenshots close to the step they explain.
