# Tutorial: SIMPLE Unity Plugin With GAMA

This tutorial is the main learning path for using the SIMPLE Unity Plugin with
GAMA through `simple.webplatform`.

The goal is to go from an opened GAMA experiment to a Unity static preview, then
to a live Play Mode visualization using the same species settings.

## Tutorial Flow

1. Install the Unity package.
2. Prepare the Unity scene.
3. Prepare or verify the GAMA Unity wrapper.
4. Generate a static GAMA Preview.
5. Configure species visuals.
6. Validate the preview.
7. Run Play Mode and receive live GAMA agents.
8. Configure dynamic colors from runtime attributes.
9. Optimize large models when needed.
10. Troubleshoot common issues.

## Global Screenshot Checklist

Add screenshots for:

- Unity Package Manager installation.
- GAMA Panel after opening.
- Setup Scene result in the Unity hierarchy.
- Generate Preview from GAMA button.
- Static preview result in Scene view.
- Species table with prefab, color, scale, visibility, and reset controls.
- Game Manager inspector species settings.
- Play Mode runtime hierarchy under `[GAMA] Runtime Live Agents`.
- Dynamic Color configuration for discrete and continuous modes.
- Performance settings and `[GAMA][PERF]` logs.
- Common troubleshooting logs.

## Recommended Test Models

Use at least two experiments while writing and validating the tutorial:

- a small dynamic model, such as prey/predator;
- a larger model, such as Ant Sorting or a city simulation.

The tutorial should avoid model-specific assumptions. When an example is needed,
state clearly which species and attributes are examples.

## Before You Start

You need:

- Unity 6.x;
- GAMA;
- `simple.webplatform`;
- this Unity package installed in a Unity project;
- a GAMA experiment opened or selected in GAMA.

Unity connects to `simple.webplatform`, not directly to GAMA Server.
