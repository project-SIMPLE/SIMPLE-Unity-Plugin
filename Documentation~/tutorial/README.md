# Tutorial: SIMPLE Unity Plugin With GAMA

This tutorial is the main learning path for using the SIMPLE Unity Plugin with
GAMA through `simple.webplatform`.

The goal is to start from an empty Unity scene, run a live GAMA experiment in
Play Mode, then introduce the Unity Editor preview as a faster way to inspect
and tune the scene before running the live simulation again.

## Tutorial Flow

0. Prepare the middleware and the GAMA experiment.

1. Install the Unity package.
2. Run the experiment in Unity Play Mode to validate the basic live workflow.
3. Personalize agents during Play Mode.
4. Generate an Editor preview and configure species visual parameters.
5. Drive dynamic visual properties from GAMA runtime attributes.

## Before You Start

You need:

- Unity 6000.3.2f1;
- GAMA;
- `simple.webplatform` on branch **dev** using : 
git clone -b dev --single-branch https://github.com/project-SIMPLE/simple.webplatform.git;
