# Runtime Notes

The runtime assembly targets the SIMPLE webplatform headset WebSocket endpoint.

Default endpoint:

- Host: `PlayerPrefs["IP"]`, fallback `localhost`
- Port: `PlayerPrefs["PORT"]`, fallback `8080`

`ConnectionManager` registers a player with `type=connection`, answers heartbeat `ping` messages with `pong`, and routes simulation payloads from `json_output` to simulation managers.

The localization system still expects `Resources/Localization/LocalizationData`. Move that resource into the package or replace the `Resources.Load` usage before publishing.
