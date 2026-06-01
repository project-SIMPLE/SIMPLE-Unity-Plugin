# 2. Middleware and GAMA requirements

This chapter explains how to correctly expose the middleware and the GAMA model so Unity can preview and
render the experiment.

## In GAMA

Open the target VR-model in GAMA. For explanations on how to install the Simple Unity Plugin; follow [this link](https://doc.project-simple.eu/gama/installation). 

![Open a new GAMA tab](../images/tutorial/02-gama-new-tab.png)

Select an experiment that is ready to run.

![Open a GAMA experiment](../images/tutorial/02-open-gama-experiment.png)

## Middleware Requirements

Start `simple.webplatform` before generating a preview or entering Play Mode.

Default endpoints:

```text
Unity runtime / headset WebSocket: ws://localhost:8080/
Monitor WebSocket: ws://localhost:8001/
GAMA Server behind webplatform: ws://localhost:1000/
```


## Result

At the end of this chapter, the GAMA experiment exposes species, geometries, and
optional attributes that Unity can receive.
