# 2. Run the GAMA Experiment in Play Mode

It is time to run the first GAMA experiment in Unity with this package. For this
tutorial, use the **6th prey Predator** model located in the following hierarchy from GAMA.

![Prey Predator 6 model location](../images/tutorial/02-prey-predator-7-location.png)

This experiment is used throughout the rest of the tutorial because it covers the
main features provided by the package: static background species, dynamic agents,
species-specific rendering, live updates, and interaction with Unity objects.

This chapter validates the baseline live workflow: Unity enters Play Mode,
connects to `simple.webplatform`, receives the running GAMA simulation, and
creates Unity objects from the GAMA agents.

> [!WARNING]
> The original GAMA experiment cannot be used directly in Unity. It must first be converted into a `vr_xp` experiment with the SIMPLE Unity plugin.

## 2.1 Convert the GAMA Experiment to `vr_xp`

Before running the model in Unity, convert the GAMA experiment with the SIMPLE Unity plugin. During this conversion, each species that should appear in Unity must be explicitly exported.

On the first **Definition of the VR experiment** screen, keep the default values and click **Next**.

![VR experiment general parameters](../images/tutorial/02-vr-generation-general-parameters.png)

On the **Export species** screen, do not immediately click **Next**. Select a species on the left, then click the **+** button under **Aspect in Unity**.

![Export species add one by one](../images/tutorial/02-vr-generation-export-species-add.png)

Keep the default property name and click **OK**.

![Keep default Unity property name](../images/tutorial/02-vr-generation-property-name.png)

Repeat this for each species that must be visible in Unity, for example:

- `prey`
- `predator`
- `vegetation_cell`

At the end, these species should be marked as exported.

![Final exported species selection](../images/tutorial/02-vr-generation-export-species-final.png)

The `generic_species` entry is abstract and is not required for the visual result. Exporting it is harmless, but it can also be ignored.

> [!IMPORTANT]
> If the species are not added on the **Export species** screen, the experiment may still start in GAMA and the Unity player may be created, but no simulation agents will appear in Unity.

After this, click **Next**, keep the default values, set the number of players between `0` and `1`, then click **Finish**.

For more details, this step is explained [in this tutorial](https://doc.project-simple.eu/tutorials/Tutorial-Step-1)

## 2.2 Steps

> [!WARNING]
> These steps must be followed **exactly in the order shown below**.  
> If you change the order, the Unity connection may fail!!!

1. Make sure the scene was prepared with **GAMA > GAMA Panel > Setup Scene**.
2. Start `simple.webplatform` with  `npm start`
3. Open and run the **vr_xp** version of **Prey Predator 6** in GAMA.

![Windows Overview](../images/tutorial/02-windows-overview-gama-unity.png)
4. Press **Play** in Unity.

![Unity Play Mode button](../images/tutorial/02-unity-play-mode-button-cropped.png)

Unity only attaches to the experiment that is already active in GAMA. Pressing
Play does not select a saved model, replace the current experiment, or send a
launch command. If the wrong experiment is open, stop and select the intended
one directly in GAMA before trying again.

Runtime agents are created under:

```text
[GAMA] Runtime Live Agents
```

When Play Mode works, Unity receives live objects from GAMA and updates them
while the experiment is running.

![Runtime live overview](../images/tutorial/02-runtime-live-overview.png)

## 2.3 Expected Result

During Play Mode, Unity should connect to `simple.webplatform` and create live
Unity objects from the agents received from the **Prey Predator 6** model.

The imported agents are grouped by species in the Unity hierarchy.

![Agents grouped by species](../images/tutorial/02-agents-grouped-by-species.png)

At this stage, the important result is that the connection works and that GAMA
agents are imported into Unity while the experiment is running.

## 2.4 Into the Next Step

This is already useful: we now have a functional connection between GAMA,
`simple.webplatform`, and Unity. The preys, predators and vegetation cells agents are imported and
converted into Unity objects automatically.

However, the raw Unity rendering is still not clear enough to understand the
experiment visually. At this point, objects are created blindly: they exist in
the scene, but their default appearance does not make the simulation easy to
read.

The next step of the tutorial focuses on Play Mode personalization. The goal is
to adjust the visual parameters of the imported objects directly in Unity so the
Prey Predator experiment becomes readable while it is running.
