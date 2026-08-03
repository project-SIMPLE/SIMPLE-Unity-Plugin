# 0. Middleware and GAMA requirements

This chapter explains how to correctly expose the middleware and the GAMA model so Unity can preview and
render the experiment.

## 0.1 GAMA Requirements

This tutorial requires a GAMA experiment converted into a Unity-compatible **vr_xp** experiment. This conversion is done with the **SIMPLE plugin**.

Before continuing, make sure the **SIMPLE plugin** is installed in GAMA. Installation instructions are available [here](https://doc.project-simple.eu/gama/installation).

> [!WARNING]
> Without the SIMPLE Unity plugin, you will not be able to convert the experiment to **vr_xp**, and Unity will not receive the simulation data correctly.


![Open a GAMA experiment](../images/tutorial/02-open-gama-experiment.png)
_Example of the experiment that will be used throughout this tutorial_

## 0.2 Middleware Requirements

Use the branch dev with : 

`git clone -b dev --single-branch https://github.com/project-SIMPLE/simple.webplatform.git`

Open the Websocket connection thanks to [this tutorial](https://github.com/project-SIMPLE/simple.webplatform).

![Open the middleware](../images/tutorial/02-open-middleware.png)

Default endpoints:

```text
Unity runtime / headset WebSocket: ws://localhost:8080/
Monitor WebSocket: ws://localhost:8001/
GAMA Server behind webplatform: ws://localhost:1000/
```
