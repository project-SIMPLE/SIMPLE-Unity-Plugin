# Technical Documentation

This section will become the developer documentation for the Unity package.

It should follow the same structure as the tutorial, but explain how the system
works internally.

## Planned Pages

1. Package architecture.
2. Middleware connection flow.
3. GAMA JSON parsing and runtime attributes.
4. Static preview generation.
5. Species render override asset.
6. Runtime agent lifecycle.
7. Prefab, color, scale, visibility, and offsets.
8. Dynamic color resolution.
9. Culling and performance.
10. Debug logs and diagnostics.

## Writing Rule

Each technical page should document:

- the main classes involved;
- the data flow;
- the source of truth;
- runtime versus editor-only behavior;
- important limitations.

## Diagram And Screenshot Plan

Add:

- architecture diagram for Unity, `simple.webplatform`, and GAMA;
- data-flow diagram from `json_output` to runtime GameObjects;
- screenshot of key inspector fields only when they map to serialized fields;
- code references for the source of truth of species overrides;
- example logs for connection, override lookup, lifecycle, and performance.
