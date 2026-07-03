# 5. Dynamic Colors From GAMA Attributes

Static species settings are not always enough. Some visual properties should be
driven by values coming from GAMA.

After generating the preview, dynamic colors can be configured in Unity Edit
Mode from the Inspector. In the previous **Prey Predator** model, a useful example is
the `food` attribute of each `vegetation_cell`: instead of showing every grass
cell with the same green, Unity can use a more or less intense green depending
on the `food` value, like in the GAMA display.

## 5.1 Attribute Requirements

The GAMA model must send the attribute in `add_geometries_to_send(...)`.

The attribute list must stay aligned with the agent list sent to Unity.

Example boolean attribute:

```gaml
list<bool> people_infected <- people collect each.is_infected;
map<string, list<bool>> people_atts <- ["is_infected":: people_infected];

do add_geometries_to_send(people, up_people, people_atts);
```

Example numeric attribute:

```gaml
list<float> prey_energy <- prey collect each.energy;
map<string, list<float>> prey_atts <- ["energy":: prey_energy];

do add_geometries_to_send(prey, up_prey, prey_atts);
```

## 5.2 Continuous Example: Vegetation Food

### 5.2.1 Attributes Sent By GAMA
Unity can only use dynamic color attributes that are sent by GAMA through the websocket.
In this experiment, the `send_geometries` reflex sends the agents and geometries to Unity. By default, `vegetation_cell` is sent without its `food` attribute:
```gaml
do add_geometries_to_send(vegetation_cell, up_vegetation_cell);
```
To make `food` available in Unity, collect the `food` values and pass them as the third argument of `add_geometries_to_send`:
```gaml
list<float> grass_food <- vegetation_cell collect each.food;
map<string, list<float>> grass_atts <- ["food":: grass_food];
do add_geometries_to_send(vegetation_cell, up_vegetation_cell, grass_atts);
```
The complete reflex should look like this:
```gaml
reflex send_geometries {
	list<float> grass_food <- vegetation_cell collect each.food;
	map<string, list<float>> grass_atts <- ["food":: grass_food];
	do add_geometries_to_send(prey, up_prey);
	do add_geometries_to_send(predator, up_predator);
	do add_geometries_to_send(vegetation_cell, up_vegetation_cell, grass_atts);
	do add_geometries_to_send(generic_species, up_generic_species);
}
```
> [!NOTE]
> The key `food` is the attribute name that will be used later in Unity. This attribute must already exist on the GAMA `vegetation_cell` agents.


### 5.2.2 Using The Attribute In Unity

After generating the preview, Unity can use the `food` attribute sent by GAMA through the websocket.

In this tutorial, to display the numeric attribute `food`, you should follow the next steps in the Edit mode of a preview :

1. Select the `Game Manager`.
2. In the Inspector, find `vegetation_cell`.
3. Open the **Dynamic Color** foldout.
4. Enable **Override Dynamic Color**.
5. Set **Dynamic Color Mode** to **Continuous**.
6. Set **Attribute Name** to `food`.
7. Set **Base Color** to green.
8. Set **Min Value** and **Max Value** to the expected GAMA range.
9. Adjust **Light Amount** and **Dark Amount** until the contrast is readable.

The numbered close-up below shows the important controls:

![Dynamic food color settings legend](../images/tutorial/05-dynamic-color-food-settings-legend.png)

In this example, the `vegetation_cell` species receives a numeric `food` attribute. The goal is:

```text
low food  -> lighter/darker green
high food -> stronger green
```

> [!NOTE]
> Unity can only use attributes that were explicitly sent by GAMA. If `food` is not included in `add_geometries_to_send`, it will not be available in the Unity dynamic color settings.

1. Open the **Dynamic Color** foldout.
2. Enable the override.
3. Choose **Continuous** mode.
4. Enter the attribute name, here `food`.
5. Pick the base color.
6. Set the numeric range.
7. Tune the light and dark variation. In the example below, both sliders are set
   to `0.5`.

With **Base Color** set to green, **Min Value** set to `0`, **Max Value** set to
`1`, **Invert** disabled, and both light/dark sliders set to `0.5`, Unity maps
the `food` value directly to green intensity. This gives a first readable view
of the grass food distribution.

![Food dynamic color without invert](../images/tutorial/05-dynamic-color-preview-before-food.png)

If **Invert** is enabled, the same `food` values are mapped in the opposite
direction. This is useful when the first gradient reads backwards for the
meaning of the attribute: for example, when low values should look visually
stronger than high values.

![Food dynamic color with invert enabled](../images/tutorial/05-dynamic-color-preview-food-result.png)

## 5.3 Discrete Colors For States

For attributes that represent a small set of states, use **Discrete** mode
instead. This is useful for experiments with states such as:

- contaminated, dead, or recovered agents;
- voters choosing between several opinions;
- agents belonging to different roles or categories.

For example:

```text
state = contaminated -> red
state = recovered    -> green
state = dead         -> black
```

## 5.4 Runtime Behavior

Dynamic colors are applied per agent when Unity receives GAMA attributes.

They do not replace the static preview workflow:

- the preview defines the default species representation;
- dynamic rules define how individual agents change from their own attributes;
- if the attribute is missing or cannot be parsed, Unity keeps the static/GAMA
  color instead of crashing.

## 5.4 Result

At the end of this chapter, Unity should be able to show both static species
settings and per-agent attribute variations, such as vegetation cells becoming
more or less green depending on their `food` value.

You came to the end of this tutorial! Thank you for following it : it is now time to experiment it yourself...
