# SIMPLE Unity Plugin

The SIMPLE Unity Plugin is a Unity Package Manager package designed to connect Unity scenes with GAMA simulations through the SIMPLE Webplatform middleware.

It provides a lightweight Unity workflow to set up a scene, connect to the middleware, generate a static preview from an experiment opened in GAMA, adjust species visual settings, and run the simulation in Play Mode. The package is designed to work without modifying `simple.webplatform`: the middleware is launched separately and Unity connects to it through WebSocket.

The current demo workflow is centered around the **GAMA Panel**, which groups scene setup, preview generation, species settings, and runtime visualization tools in one place.

---

## Installation

### Install from Git URL

In Unity:

1. Open **Window > Package Manager**.
2. Click the **+** button.
3. Select **Add package from git URL...**.
4. Paste one of the following URLs.

For the main branch :

```text
https://github.com/project-SIMPLE/SIMPLE-Unity-Plugin.git
