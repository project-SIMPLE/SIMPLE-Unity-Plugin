# 5. Drive Dynamic Properties From GAMA Attributes

Static species settings are not always enough. Some visual properties should
change continuously while the GAMA simulation runs.

This chapter explains how to drive Unity visuals from per-agent attributes sent
by GAMA at runtime. The main example is dynamic color, because it is easy to
verify visually.

## Attribute Requirements

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

## Discrete Example: Contaminated People

Use this mode when an attribute represents a small set of states.

Goal:

```text
is_infected = false -> green
is_infected = true  -> red
```

Steps:

1. Select the `Game Manager`.
2. Find the target species, for example `people`.
3. Enable **Override Dynamic Color**.
4. Set **Dynamic Color Mode** to **Discrete**.
5. Select the runtime attribute, for example `is_infected`.
6. Add two rules: `false` = green and `true` = red.

If Unity has already received attributes for that species, the attribute field is
shown as a dropdown. If no attributes have been received yet, type the attribute
name manually, then enter Play Mode again.

## Continuous Example: Prey/Predator Or Vegetation Value

Use this mode when an attribute is numeric and should produce a gradual visual
change.

Example goal for a prey/predator model:

```text
low energy  -> light green
high energy -> dark green
```

Steps:

1. Select the `Game Manager`.
2. Find the target species, for example `prey`.
3. Enable **Override Dynamic Color**.
4. Set **Dynamic Color Mode** to **Continuous**.
5. Select the runtime attribute, for example `energy`.
6. Set **Base Color** to green.
7. Set **Min Value** and **Max Value** to match the expected GAMA range.
8. Adjust the light/dark amounts if needed.

The same pattern can be used with vegetation, pollution, health, infection
probability, hunger, or any numeric value sent by GAMA.

## Runtime Behavior

Dynamic colors are applied per agent during Play Mode.

They do not replace the static preview workflow:

- the preview defines the default species representation;
- dynamic rules define how individual agents change during runtime;
- if the attribute is missing or cannot be parsed, Unity keeps the static/GAMA
  color instead of crashing.

## Result

At the end of this chapter, Unity should be able to show both static species
settings and per-agent runtime variations, such as infected people turning red
or prey becoming greener as a numeric value changes.

## Navigation

| Previous | Next |
|---|---|
| [4. Generate and Configure the Unity Preview](04-configure-species.md) | [6. Apply Preview Settings In Play Mode](06-dynamic-colors.md) |
