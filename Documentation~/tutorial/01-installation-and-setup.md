# 1. Install the Unity Package

This chapter installs the SIMPLE Unity Plugin in a Unity project and prepares the
scene for GAMA communication.

## Install From GitHub

1. Open Unity.
2. Open **Window > Package Manager**.
3. Click **+**.
4. Select **Add package from git URL...**.
5. Enter:

```text
https://github.com/project-SIMPLE/SIMPLE-Unity-Plugin.git
```

To install a specific branch:

```text
https://github.com/project-SIMPLE/SIMPLE-Unity-Plugin.git#branch-name
```

> Screenshot to add: Unity Package Manager with **Add package from git URL...**
> open and the package URL field visible.

## Install From Local Disk

For local development:

1. Open **Window > Package Manager**.
2. Click **+**.
3. Select **Add package from disk...**.
4. Select the package `package.json`.

> Screenshot to add: Unity Package Manager with **Add package from disk...**
> and the package `package.json` selected.

## Setup The Unity Scene

1. Open **GAMA > GAMA Panel**.
2. Click **Setup Scene**.
3. Verify that the scene contains:
   - a player or camera rig;
   - a `Connection Manager`;
   - a `Game Manager`;
   - required scene roots for preview and runtime objects.

> Screenshot to add: the **GAMA > GAMA Panel** menu entry.

> Screenshot to add: the GAMA Panel with the **Setup Scene** button visible.

> Screenshot to add: Unity hierarchy after setup, showing player/camera,
> `Connection Manager`, and `Game Manager`.

## Middleware Requirements

Start `simple.webplatform` before generating a preview or entering Play Mode.

Default endpoints:

```text
Unity runtime / headset WebSocket: ws://localhost:8080/
Monitor WebSocket: ws://localhost:8001/
GAMA Server behind webplatform: ws://localhost:1000/
```

## Result

At the end of this chapter, Unity is ready to communicate with the middleware.
