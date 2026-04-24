# SIMPLE GAMA Unity Package Notes

This package is extracted from the SIMPLE Unity Template VR project.

## Runtime Architecture

The Unity runtime connects to `simple.webplatform`, not directly to GAMA Server.

- `ConnectionManager` sends headset/player messages to webplatform on port `8080` by default.
- `simple.webplatform` forwards `expression` and `ask` messages to GAMA Server on port `1000`.
- The web monitor UI uses a separate monitor WebSocket on port `8001`.

## Runtime Protocol

Unity sends:

- `connection`: registers the headset/player id and heartbeat interval.
- `pong`: answers webplatform heartbeat `ping` messages.
- `expression`: forwards a GAML expression through webplatform.
- `ask`: forwards a structured GAMA ask with action, args, and agent.
- `disconnect_properly`: asks webplatform to close and purge the player.

Unity receives:

- `ping`: heartbeat request from webplatform.
- `json_state`: player connection and in-game state.
- `json_output`: simulation output filtered by player id.

## Dependencies

NativeWebSocket is vendored in `Runtime/ThirdParty/NativeWebSocket` and referenced by `ProjectSimple.GamaUnity`.

WebSocketSharp is intentionally not part of the runtime assembly. The legacy geometry import/export workflow depended on WebSocketSharp and is currently stubbed in `Editor/`.

## Extraction Checklist

- Move required `Resources.Load` assets into package-owned runtime resources or replace them with explicit configuration.
- Decide whether legacy geometry import/export should become a separate optional package.
- Validate samples after importing them into a clean Unity project.
- Keep `.meta` files for Unity-imported assets and scripts.

## Test Procedure

1. Create a clean Unity 6 project.
2. Add this package through Package Manager with **Add package from disk...**.
3. Import each sample from the Package Manager package details panel.
4. Check the Unity console for compile errors and missing asset references.
5. Run `simple.webplatform` and connect the Unity runtime to `ws://<host>:8080/`.
