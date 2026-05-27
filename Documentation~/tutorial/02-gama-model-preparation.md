# 2. Prepare a GAMA Experiment for Unity

This chapter explains what the GAMA model must expose so Unity can preview and
render it.

## Unity Linker

A Unity-compatible experiment normally defines a species that extends
`abstract_unity_linker`.

The linker declares:

- the Unity player species;
- the list of Unity properties;
- the species or geometries to send to Unity;
- optional runtime attributes sent with each agent.

> Screenshot to add: GAMA editor with the `unity_linker` species visible.

> Screenshot to add: GAMA Model Outline showing `unity_linker`,
> `unity_player`, and the Unity experiment.

## Unity Properties

Each visible species should have a `unity_property`.

Typical fields define:

- the property id used by Unity;
- the species name;
- the default GAMA aspect;
- interaction settings;
- whether the geometry is static or dynamic.

Example structure:

```gaml
unity_aspect people_aspect <- geometry_aspect(1.0, #gray, precision);
up_people <- geometry_properties("people", "people", people_aspect, #no_interaction, false);
unity_properties << up_people;
```

> Screenshot to add: highlighted GAMA code where `unity_property` entries are
> declared for the visible species.

## Static And Dynamic Geometries

Use static/background geometries for objects that do not need to be resent every
tick.

Use live `add_geometries_to_send(...)` calls for agents that move, appear,
disappear, or change attributes.

Examples:

```gaml
do add_background_geometries(vegetation_cell, up_vegetation_cell);
```

```gaml
do add_geometries_to_send(people, up_people);
```

> Screenshot to add: GAMA code showing one static/background species and one
> dynamic species sent in `reflex send_geometries`.

## Runtime Attributes

Unity can use runtime attributes to drive dynamic colors.

Example for a boolean infection state:

```gaml
list<bool> people_infected <- people collect each.is_infected;
map<string, list<bool>> people_atts <- ["is_infected":: people_infected];

do add_geometries_to_send(people, up_people, people_atts);
```

Example for a continuous grass value:

```gaml
list<float> grass_food <- vegetation_cell collect each.food;
map<string, list<float>> grass_atts <- ["food":: grass_food];

do add_geometries_to_send(vegetation_cell, up_vegetation_cell, grass_atts);
```

> Screenshot to add: GAMA code showing the exact attribute map sent to Unity.

> Optional media to add: short GIF showing the GAMA attribute changing and Unity
> color updating.

## Result

At the end of this chapter, the GAMA experiment exposes species, geometries, and
optional attributes that Unity can receive.
