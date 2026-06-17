# 0. Middleware and GAMA requirements

This chapter explains how to correctly expose the middleware and the GAMA model so Unity can preview and
render the experiment.

## 0.1 GAMA Requirements

Open your target VR-model in GAMA in the configuration of the screenshot below (ready to run). For explanations on how to install the Simple Unity Plugin, follow [this link](https://doc.project-simple.eu/gama/installation). 

![Open a GAMA experiment](../images/tutorial/02-open-gama-experiment.png)
_Exemple of an opened experiment in "Library models\Tutorials\Predator Prey\models"_

## 0.2 Middleware Requirements

Open the Websocket connection thanks to [this tutorial](https://github.com/project-SIMPLE/simple.webplatform).

![Open the middleware](../images/tutorial/02-open-middleware.png)

Default endpoints:

```text
Unity runtime / headset WebSocket: ws://localhost:8080/
Monitor WebSocket: ws://localhost:8001/
GAMA Server behind webplatform: ws://localhost:1000/
```

## Navigation

| Previous | Next |
|---|---|
| [Tutorial Overview](README.md) | [1. Package Installation and Setup](01-installation-and-setup.md) |
