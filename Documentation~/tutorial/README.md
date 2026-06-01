# Tutorial: SIMPLE Unity Plugin With GAMA

This tutorial is the main learning path for using the SIMPLE Unity Plugin with
GAMA through `simple.webplatform`.

The goal is to start from an empty Unity scene, run a live GAMA experiment in
Play Mode, then introduce the Unity Editor preview as a faster way to inspect
and tune the scene before running the live simulation again.

## Tutorial Flow

1. Install the Unity package.
2. Prepare the middleware and the GAMA experiment.
3. Run the experiment in Unity Play Mode to validate the basic live workflow.
4. Generate an Editor preview and configure species visual parameters.
5. Drive dynamic visual properties from GAMA runtime attributes.
6. Apply preview settings back to Play Mode and validate the live result.
7. Optimize large models when needed.
8. Troubleshoot common issues.

## Why Play Mode Comes Before Preview

The simplest way to prove that the connection works is to enter Play Mode while
`simple.webplatform` and the target GAMA experiment are running. Unity should
receive live objects and update them during the simulation.

That direct workflow has one drawback: to check whether the scene looks right,
the user must launch the full experiment. The Editor preview solves this by
capturing the selected GAMA experiment, building a static representation in
Unity Edit Mode, and allowing species settings to be adjusted before Play Mode.

## Global Screenshot Checklist

Add screenshots for:

- Unity Package Manager installation.
- GAMA Panel after opening.
- Setup Scene result in the Unity hierarchy.
- Play Mode runtime hierarchy under `[GAMA] Runtime Live Agents`.
- Generate Preview from GAMA button.
- Static preview result in Scene view.
- Species table with prefab, color, scale, visibility, and reset controls.
- Game Manager inspector species settings.
- Dynamic Color configuration for discrete and continuous modes.
- Preview settings applied back to Play Mode.
- Performance settings and `[GAMA][PERF]` logs.
- Common troubleshooting logs.

## Recommended Test Models

Use at least two experiments while writing and validating the tutorial:

- a small dynamic model, such as prey/predator;
- a model with a discrete state variable, such as infected/non-infected people;
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

## Navigation

| Previous | Next |
|---|---|
| - | [1. Install the Unity Package](01-installation-and-setup.md) |
