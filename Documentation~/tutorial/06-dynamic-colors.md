# 6. Drive Colors From GAMA Attributes

This chapter configures per-agent dynamic colors using attributes received from
GAMA at runtime.

Dynamic colors are configured per species in the `Game Manager` /
`Simulation Manager` inspector.

> Screenshot to add: the Dynamic Color section for one species in the
> `Game Manager` inspector.

## Attribute Requirements

The GAMA model must send the attribute in `add_geometries_to_send(...)`.

Example boolean attribute:

```gaml
list<bool> people_infected <- people collect each.is_infected;
map<string, list<bool>> people_atts <- ["is_infected":: people_infected];

do add_geometries_to_send(people, up_people, people_atts);
```

> Screenshot to add: GAMA code sending `is_infected` as a runtime attribute.

Example numeric attribute:

```gaml
list<float> grass_food <- vegetation_cell collect each.food;
map<string, list<float>> grass_atts <- ["food":: grass_food];

do add_geometries_to_send(vegetation_cell, up_vegetation_cell, grass_atts);
```

> Screenshot to add: GAMA code sending a numeric grass attribute such as `food`.

## Discrete Color Example

Goal:

```text
false -> green
true  -> red
```

Steps:

1. Select the `Game Manager`.
2. Find the target species, for example `people`.
3. Enable **Override Dynamic Color**.
4. Set **Dynamic Color Mode** to **Discrete**.
5. Select the runtime attribute, for example `is_infected`.
6. Add two rules:
   - `false` = green;
   - `true` = red.

> Screenshot to add: Discrete Dynamic Color setup with `is_infected`,
> `false -> green`, and `true -> red`.

> Screenshot to add: Unity Scene view where infected agents are red and
> non-infected agents are green.

If Unity has already received attributes for that species, the attribute field is
shown as a dropdown. If no attributes have been received yet, type the attribute
name manually, then enter Play Mode again.

> Screenshot to add: attribute dropdown populated with runtime attributes.

> Screenshot to add: fallback text field when no runtime attributes have been
> received yet.

## Continuous Color Example

Goal:

```text
0 -> light green
1 -> dark green
```

Steps:

1. Select the `Game Manager`.
2. Find the target species, for example `vegetation_cell`.
3. Enable **Override Dynamic Color**.
4. Set **Dynamic Color Mode** to **Continuous**.
5. Select the runtime attribute, for example `food`.
6. Set **Base Color** to green.
7. Set **Min Value** to `0`.
8. Set **Max Value** to `1`.
9. Adjust **Light Amount** and **Dark Amount** if needed.

> Screenshot to add: Continuous Dynamic Color setup with base green, min `0`,
> max `1`.

> Screenshot to add: Unity result with low values light green and high values
> dark green.

## Fallback Behavior

If the attribute is missing or the value cannot be parsed, Unity keeps the
static/GAMA color. The dynamic color system should not crash or erase existing
color overrides.

> Screenshot to add: Console diagnostic for missing attributes, if available.
