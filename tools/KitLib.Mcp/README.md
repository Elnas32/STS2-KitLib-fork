# KitLib MCP (stdio proxy)

stdio MCP proxy for KitLib / KitDevTools in-game tools. Configure your MCP client to launch this project; tool names and payloads match the in-game MCP server.

There is no separate product docs page for MCP on the Valaxy site — use this README and the tool schemas exposed by the proxy.

## Tools

Notes on tools whose payload is not obvious from the schema alone.
- **`get_map_state`** — Full act map for route planning: every node (`row`, `col`, `pointType`, `children`), the visited path, current position, the nodes reachable next, and the boss. Works with the map screen closed; `get_game_state`'s `mapNodes` only lists what is reachable from where you stand
- **`dev_ui`** — List and drive ordinary UI controls (buttons, checkboxes, dropdowns, text fields), plus mod-added controls that handle their own `gui_input`: `list` to see what is on screen, then `click` / `toggle` / `select` / `set_text` / `focus` by index. Drives menus and dialogs; `dev_hover` only covers hover-only game surfaces. Use `focus` where a screen acts on Godot's `FocusEntered` instead of a press — character select selects on focus and ignores clicks entirely
- **`dev_read_text`** — Read visible on-screen text (labels, tooltips, panels, forecasts), BBCode stripped. Pair with `dev_hover` to read what a preview says; `dev_ui` lists only interactive controls
- **`dev_add_potion`** — Grant a potion by model id (`potion_id`), or pass `search` to list ids
- **`dev_hover`** — Focus a UI element so hover-only behaviour fires. `target` takes an alias (`map_node`, `reward_alternative`, `rest_option`, `relic`, `boss_icon`, `event_option`, `potion`, `card`, `creature`, `treasure_relic`) or **any node type name**, including plain Godot `Control`s a mod added — those are driven by emitting `mouse_entered`. `target: "list"` reports what is hoverable on screen, `"none"` unhovers, `click` presses after focusing, listed instances carry `via=focus|signal|none`, and `release_previous: false` stacks a hover so a panel it opened can still be read
