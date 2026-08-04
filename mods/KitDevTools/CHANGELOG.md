# Changelog

All notable changes to **KitDevTools** are documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

### Added

- **AutoSlay** — DevMode title-screen menu starts the official AutoSlay smoke bot with an optional seed. It is no longer a Dev side-rail tab. Single-player only; Steam release builds cannot use `--autoslay`, so this calls `AutoSlayer.Start` directly and skips the bot's process quit when the run ends.
- Initial standalone product release: in-run side rail, browsers, cheats, saves, logs, and related developer tools.
- KitModPanel is optional: Progress Guard and related settings register through KitLib host APIs when ModPanel is present.
- **Card browser drag-and-drop** — Drag a card outside the panel: top → draw pile, right → discard, bottom → hand.
- **Hover to open** — Hovering a side-rail icon opens that panel (no click required).
- **Mod test room teleports** — Room browser **Mod test — Rest site** and **Mod test — Treasure** spawn extra fake players to preview official multiplayer UI in solo dev runs.
- **Log export screenshots** — The log ZIP can include a game screenshot (KitLib UI hidden), extra images, a short description, official-style category chips (`Bug - this is a bug`), reaction tags, and the last combat snapshot.
- **Pseudo co-op ascension and modifiers** — `PseudoCoopLobbyHost.LaunchOptions` carries an ascension level and a run modifier list, so a launch can reproduce an existing run instead of inheriting character select's last ascension and no modifiers.
- **MCP `get_map_state`** — Full act map for route planning: every node with row/col, type and children (edges), plus the visited path, current position, the nodes reachable next, and the boss. Works with the map screen closed.
- **MCP `dev_ui`** — List and drive ordinary UI controls (buttons, checkboxes, dropdowns, text fields) by index, so menus and dialogs can be driven without a mouse. Also drives mod-added controls that are not buttons at all and handle their own `gui_input` — rows, panel handles, footer controls — and has a `focus` action for screens that act on Godot's `FocusEntered` rather than on a press, which is what makes character select reachable.

### Changed

- **Cheat sidebar tabs** — Cards, Cheats, Card Test, Save/Load, and the other cheat rail entries are registered by this product, not by KitLib Core.
- **Pseudo-coop harness** — SyncBot phantom spawn, lobby host, and simulated-peer combat/map patches live here and call Core NetPlay APIs. KitAI no longer owns that test driving.
- **Panel switching** — Revisiting already-opened Dev panels is much snappier.
- **Hand drop zone** — Bottom drop label is now **Hand**.
- **AI Host tab removed** — AI controls no longer appear on the Dev side rail; use KitAI when a dedicated AI panel is available.

### Fixed

- **Mod test — Rest site** — Leaving the preview room restores your solo layout on stable game builds.
- **Pseudo co-op restart** — Starting pseudo co-op a second time in one session no longer fails to open its host. The ENet host on port 33771 was never released when a run ended, so every later start failed with `CantCreate` until the game was restarted.
- **Pseudo co-op seed** — Entering a seed no longer aborts "Start pseudo co-op" outright. The seed is applied and synced; only the standard character-select screen refuses the UI notification, and that refusal used to escape and take the whole host start with it.
