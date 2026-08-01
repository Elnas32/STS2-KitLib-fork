# KitLib MCP (stdio proxy)

stdio MCP proxy for KitLib / KitDevTools in-game tools. Configure your MCP client to launch this project; tool names and payloads match the in-game MCP server.

There is no separate product docs page for MCP on the Valaxy site — use this README and the tool schemas exposed by the proxy.

## Tools

Notes on tools whose payload is not obvious from the schema alone.

- **`get_map_state`** — Full act map for route planning: every node (`row`, `col`, `pointType`, `children`), the visited path, current position, the nodes reachable next, and the boss. Works with the map screen closed; `get_game_state`'s `mapNodes` only lists what is reachable from where you stand
