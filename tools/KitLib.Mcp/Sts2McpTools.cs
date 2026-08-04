using System.ComponentModel;
using System.Text.Json.Nodes;
using ModelContextProtocol.Server;

namespace KitLib.McpProxy;

[McpServerToolType]
internal sealed class Sts2McpTools {
    private readonly GameBridgeClient _bridge;

    public Sts2McpTools(GameBridgeClient bridge) => _bridge = bridge;

    [McpServerTool(Name = "get_game_state", ReadOnly = true), Description(
        "Get the current STS2 game state. In combat includes playerPowers, phase, and indexed enemies with powers.")]
    public Task<string> GetGameState(CancellationToken cancellationToken) =>
        _bridge.CallToolAsync("get_game_state", new JsonObject(), cancellationToken);

    [McpServerTool(Name = "get_combat_stats", ReadOnly = true), Description(
        "Combat stats timeline: events with source attribution, creature snapshots. " +
        "Use since_sequence after combat_action to read only new events.")]
    public Task<string> GetCombatStats(
        [Description("Return events with sequence greater than this.")]
        int since_sequence = 0,
        [Description("Max events to return (default 200).")]
        int max_events = 200,
        [Description("Optional turn filter.")]
        int? turn = null,
        CancellationToken cancellationToken = default) {
        var args = new JsonObject {
            ["since_sequence"] = since_sequence,
            ["max_events"] = max_events,
        };
        if (turn.HasValue)
            args["turn"] = turn.Value;
        return _bridge.CallToolAsync("get_combat_stats", args, cancellationToken);
    }

    [McpServerTool(Name = "combat_action"), Description(
        "Execute a combat action. play_card may return pendingSelection; pass selection_card_id for auto-pick.")]
    public Task<string> CombatAction(
        [Description("The combat action to perform: play_card, end_turn, or use_potion.")]
        string action,
        [Description("Index of the card in hand to play (for play_card).")]
        int card_index = 0,
        [Description("Index of the enemy to target (for targeted cards). -1 for untargeted.")]
        int target_index = -1,
        [Description("For play_card: auto-pick this card id when a selection screen opens.")]
        string? selection_card_id = null,
        [Description("For play_card: auto-pick this option index when a selection screen opens.")]
        int? selection_index = null,
        CancellationToken cancellationToken = default) {
        var args = new JsonObject {
            ["action"] = action,
            ["card_index"] = card_index,
            ["target_index"] = target_index,
        };
        if (!string.IsNullOrWhiteSpace(selection_card_id))
            args["selection_card_id"] = selection_card_id.Trim();
        if (selection_index.HasValue)
            args["selection_index"] = selection_index.Value;
        return _bridge.CallToolAsync("combat_action", args, cancellationToken);
    }

    [McpServerTool(Name = "get_selection_state", ReadOnly = true), Description(
        "Get the active card selection UI: combat pile / hand pick with indexed options.")]
    public Task<string> GetSelectionState(CancellationToken cancellationToken = default) =>
        _bridge.CallToolAsync("get_selection_state", new JsonObject(), cancellationToken);

    [McpServerTool(Name = "selection_action"), Description(
        "Pick card(s) on the active selection screen. Use get_selection_state to list options.")]
    public Task<string> SelectionAction(
        [Description("Single option index from get_selection_state.options.")]
        int? card_index = null,
        [Description("Multiple option indices for multi-select prompts.")]
        int[]? card_indices = null,
        [Description("Pick the first visible option matching this card model id.")]
        string? card_id = null,
        [Description("Click confirm/proceed when enabled (default true).")]
        bool confirm = true,
        CancellationToken cancellationToken = default) {
        var args = new JsonObject { ["confirm"] = confirm };
        if (!string.IsNullOrWhiteSpace(card_id))
            args["card_id"] = card_id.Trim();
        if (card_indices is { Length: > 0 }) {
            var arr = new JsonArray();
            foreach (var index in card_indices)
                arr.Add(index);
            args["card_indices"] = arr;
        }
        else if (card_index.HasValue)
            args["card_index"] = card_index.Value;
        return _bridge.CallToolAsync("selection_action", args, cancellationToken);
    }

    [McpServerTool(Name = "map_action"), Description(
        "Execute a non-combat action: select map node, pick card reward, choose event option, interact with shop, rest, or proceed.")]
    public Task<string> MapAction(
        [Description(
            "The action to perform: select_map_node, pick_card_reward, skip_card_reward, select_event_choice, " +
            "purchase_shop_item, remove_card_at_shop, leave_shop, rest, upgrade_card, collect_reward, " +
            "dismiss_rewards, or proceed.")]
        string action,
        [Description("Index of the target (node, card, item, etc.).")]
        int target_index = 0,
        CancellationToken cancellationToken = default) =>
        _bridge.CallToolAsync("map_action", new JsonObject {
            ["action"] = action,
            ["target_index"] = target_index,
        }, cancellationToken);

    [McpServerTool(Name = "get_map_state", ReadOnly = true), Description(
        "Full act map for route planning: every node with row/col, type and children (edges), plus the visited "
        + "path, current position, the nodes reachable next, and the boss. Works with the map screen closed. "
        + "get_game_state's mapNodes only lists the nodes reachable from where you stand.")]
    public Task<string> GetMapState(CancellationToken cancellationToken = default) =>
        _bridge.CallToolAsync("get_map_state", new JsonObject(), cancellationToken);

    [McpServerTool(Name = "dev_ui"), Description(
        "List and drive on-screen UI controls (buttons, checkboxes, dropdowns, text fields, and mod-added "
        + "controls that handle their own gui_input) by index. Use action 'list' first, then "
        + "click/toggle/select/set_text/focus. This is how menus and dialogs are driven; dev_hover only "
        + "covers hover-only game surfaces. Use 'focus' where a screen acts on Godot's FocusEntered rather "
        + "than on a press -- character select is one, where clicking does nothing.")]
    public Task<string> DevUi(
        [Description("list, click, toggle, select, set_text, or focus.")]
        string action = "list",
        [Description("Case-insensitive substring on label, node name or type. Indexes are relative to the FILTERED list.")]
        string? filter = null,
        [Description("Which control, from the list this tool returns.")]
        int index = -1,
        [Description("For set_text: the value to type.")]
        string? text = null,
        [Description("For select: the item index inside a dropdown.")]
        int option = -1,
        [Description("For toggle: desired state. Omit to flip.")]
        bool? pressed = null,
        CancellationToken cancellationToken = default) {
        var args = new JsonObject {
            ["action"] = action,
            ["index"] = index,
            ["option"] = option,
        };
        if (!string.IsNullOrWhiteSpace(filter))
            args["filter"] = filter;
        if (text != null)
            args["text"] = text;
        if (pressed.HasValue)
            args["pressed"] = pressed.Value;
        return _bridge.CallToolAsync("dev_ui", args, cancellationToken);
    }

    [McpServerTool(Name = "dev_read_text", ReadOnly = true), Description(
        "Read visible on-screen text (labels, tooltips, panels, forecasts) in tree order. Use after "
        + "dev_hover to read what a preview actually says; dev_ui lists only interactive controls.")]
    public Task<string> DevReadText(
        [Description("Only return entries whose text contains this, case-insensitive.")]
        string? filter = null,
        [Description("Maximum entries to return (default 120).")]
        int limit = 120,
        [Description("Skip text shorter than this (default 1).")]
        int min_length = 1,
        CancellationToken cancellationToken = default) {
        var args = new JsonObject {
            ["limit"] = limit,
            ["min_length"] = min_length,
        };
        if (!string.IsNullOrWhiteSpace(filter))
            args["filter"] = filter;
        return _bridge.CallToolAsync("dev_read_text", args, cancellationToken);
    }

    [McpServerTool(Name = "dev_add_potion"), Description(
        "Grant a potion to the local player by model id. Pass 'search' instead to look an id up.")]
    public Task<string> DevAddPotion(
        [Description("Potion model ID, e.g. FIRE_POTION, BLOCK_POTION.")]
        string? potion_id = null,
        [Description("List potion ids containing this text instead of granting. Empty string lists all.")]
        string? search = null,
        CancellationToken cancellationToken = default) {
        var args = new JsonObject();
        // search is checked FIRST by the bridge and needs no run, so forward it alone when given --
        // sending both would silently turn a grant into a lookup.
        if (search != null)
            args["search"] = search;
        else if (!string.IsNullOrWhiteSpace(potion_id))
            args["potion_id"] = potion_id.Trim();
        return _bridge.CallToolAsync("dev_add_potion", args, cancellationToken);
    }

    [McpServerTool(Name = "dev_get_session", ReadOnly = true), Description(
        "Get DevMode session state: run active, game phase, dev-run flag, and blocking startup prompts.")]
    public Task<string> DevGetSession(CancellationToken cancellationToken = default) =>
        _bridge.CallToolAsync("dev_get_session", new JsonObject(), cancellationToken);

    [McpServerTool(Name = "dev_list_save_slots", ReadOnly = true), Description(
        "List DevMode save slots with metadata and debug notes for AI slot selection.")]
    public Task<string> DevListSaveSlots(CancellationToken cancellationToken = default) =>
        _bridge.CallToolAsync("dev_list_save_slots", new JsonObject(), cancellationToken);

    [McpServerTool(Name = "dev_tag_save_slot"), Description(
        "Set debug notes on a save slot so agents can identify what the save is for.")]
    public Task<string> DevTagSaveSlot(
        [Description("Save slot ID (0 = quick save).")]
        int slot_id,
        [Description("Debug label, e.g. combat:ironclad-act1-boss.")]
        string notes,
        CancellationToken cancellationToken = default) =>
        _bridge.CallToolAsync("dev_tag_save_slot", new JsonObject {
            ["slot_id"] = slot_id,
            ["notes"] = notes,
        }, cancellationToken);

    [McpServerTool(Name = "dev_load_save_slot"), Description(
        "Load a DevMode save slot. Poll dev_get_session until runActive is true.")]
    public Task<string> DevLoadSaveSlot(
        [Description("Save slot ID (default 0 = quick save).")]
        int slot_id = 0,
        CancellationToken cancellationToken = default) =>
        _bridge.CallToolAsync("dev_load_save_slot", new JsonObject {
            ["slot_id"] = slot_id,
        }, cancellationToken);

    [McpServerTool(Name = "dev_start_test_run"), Description(
        "Start a new DevMode test run from the main menu (opens character select). Optional seed.")]
    public Task<string> DevStartTestRun(
        [Description("Optional run seed override.")]
        string? seed = null,
        CancellationToken cancellationToken = default) {
        var args = new JsonObject();
        if (!string.IsNullOrWhiteSpace(seed))
            args["seed"] = seed.Trim();
        return _bridge.CallToolAsync("dev_start_test_run", args, cancellationToken);
    }

    [McpServerTool(Name = "dev_list_cards", ReadOnly = true), Description(
        "List cards in deck/hand/draw/discard/exhaust piles, or all piles.")]
    public Task<string> DevListCards(
        [Description("Pile: deck, hand, draw, discard, exhaust, or all (default all).")]
        string target = "all",
        CancellationToken cancellationToken = default) =>
        _bridge.CallToolAsync("dev_list_cards", new JsonObject {
            ["target"] = target,
        }, cancellationToken);

    [McpServerTool(Name = "dev_add_card"), Description(
        "Add a card to deck or a combat pile (DevMode card browser API).")]
    public Task<string> DevAddCard(
        [Description("Card model ID, e.g. IRONCLAD_CARD_STRIKE.")]
        string card_id,
        [Description("Target pile: deck, hand, draw, discard, exhaust (default hand).")]
        string target = "hand",
        [Description("perm or temp for combat piles (default perm).")]
        string duration = "perm",
        [Description("Upgrade levels to apply (default 0).")]
        int upgrade_levels = 0,
        CancellationToken cancellationToken = default) =>
        _bridge.CallToolAsync("dev_add_card", new JsonObject {
            ["card_id"] = card_id,
            ["target"] = target,
            ["duration"] = duration,
            ["upgrade_levels"] = upgrade_levels,
        }, cancellationToken);

    [McpServerTool(Name = "dev_remove_card"), Description(
        "Remove a card from a pile by card_id or pile_index.")]
    public Task<string> DevRemoveCard(
        [Description("Target pile: deck, hand, draw, discard, exhaust (default hand).")]
        string target = "hand",
        [Description("Card model ID (first match in pile).")]
        string? card_id = null,
        [Description("Index in pile from dev_list_cards.")]
        int? pile_index = null,
        [Description("Remove from run deck when deleting from combat piles (default true).")]
        bool permanent = true,
        CancellationToken cancellationToken = default) {
        var args = new JsonObject {
            ["target"] = target,
            ["permanent"] = permanent,
        };
        if (!string.IsNullOrWhiteSpace(card_id))
            args["card_id"] = card_id.Trim();
        if (pile_index.HasValue)
            args["pile_index"] = pile_index.Value;
        return _bridge.CallToolAsync("dev_remove_card", args, cancellationToken);
    }

    [McpServerTool(Name = "dev_add_relic"), Description(
        "Grant a relic to the local player by model id (DevMode relic browser API). "
        + "Pass 'search' instead to look an id up.")]
    public Task<string> DevAddRelic(
        [Description("Relic model ID, e.g. DRIFTWOOD, PAELS_WING, DREAM_CATCHER.")]
        string? relic_id = null,
        [Description("List relic ids/rarities containing this text instead of granting. Empty string lists all.")]
        string? search = null,
        CancellationToken cancellationToken = default) {
        var args = new JsonObject();
        // search is checked FIRST by the bridge and needs no run, so forward it alone when given --
        // sending both would silently turn a grant into a lookup.
        if (search != null)
            args["search"] = search;
        else if (!string.IsNullOrWhiteSpace(relic_id))
            args["relic_id"] = relic_id.Trim();
        return _bridge.CallToolAsync("dev_add_relic", args, cancellationToken);
    }

    [McpServerTool(Name = "dev_hover"), Description(
        "Hover (focus) a UI element so hover-only behaviour fires. target takes an alias (map_node, "
        + "reward_alternative, rest_option, relic, boss_icon, event_option, potion, card, creature, "
        + "treasure_relic) or ANY node type name, including plain Godot Controls a MOD added "
        + "(PanelContainer, HBoxContainer, TextureRect) — those are driven by emitting mouse_entered; "
        + "'list' discovers what is hoverable on screen, 'none' unhovers. Listed instances carry "
        + "via=focus|signal|none. Pair with dev_read_text to read the panel the hover opens.")]
    public Task<string> DevHover(
        [Description("Alias, any node type name (e.g. NTopBarBossIcon, or a mod's own PanelContainer), 'list' to discover, or 'none' to unhover.")]
        string target,
        [Description("Which one to hover, from the list this tool returns. Omit to only list them.")]
        int index = -1,
        [Description("Also click it after focusing (default false).")]
        bool click = false,
        [Description("Unhover what is already hovered first (default true). Pass false to STACK a hover — needed to read a panel a hover opened, since such panels poll the cursor's real position and close when their opener reports an exit.")]
        bool release_previous = true,
        CancellationToken cancellationToken = default) =>
        _bridge.CallToolAsync("dev_hover", new JsonObject {
            ["target"] = target,
            ["index"] = index,
            ["click"] = click,
            ["release_previous"] = release_previous,
        }, cancellationToken);

    [McpServerTool(Name = "dev_list_monsters", ReadOnly = true), Description(
        "List known monster model IDs for dev_add_monster.")]
    public Task<string> DevListMonsters(
        [Description("Optional ID prefix filter, e.g. OVICO.")]
        string? prefix = null,
        CancellationToken cancellationToken = default) {
        var args = new JsonObject();
        if (!string.IsNullOrWhiteSpace(prefix))
            args["prefix"] = prefix.Trim();
        return _bridge.CallToolAsync("dev_list_monsters", args, cancellationToken);
    }

    [McpServerTool(Name = "dev_list_enemies", ReadOnly = true), Description(
        "List enemies currently in combat.")]
    public Task<string> DevListEnemies(CancellationToken cancellationToken = default) =>
        _bridge.CallToolAsync("dev_list_enemies", new JsonObject(), cancellationToken);

    [McpServerTool(Name = "dev_add_monster"), Description(
        "Add a monster to the current combat (DevMode enemy browser).")]
    public Task<string> DevAddMonster(
        [Description("Monster model ID, e.g. OVICOPTER.")]
        string monster_id,
        CancellationToken cancellationToken = default) =>
        _bridge.CallToolAsync("dev_add_monster", new JsonObject {
            ["monster_id"] = monster_id,
        }, cancellationToken);

    [McpServerTool(Name = "dev_set_cheat"), Description(
        "Toggle patch/runtime cheats or set multiplier values.")]
    public Task<string> DevSetCheat(
        [Description("Cheat id, e.g. freeze_enemies, damage_multiplier, god_mode.")]
        string cheat,
        [Description("For toggles. Omit to flip current state.")]
        bool? enabled = null,
        [Description("For multipliers or extra_draw amount.")]
        double? value = null,
        CancellationToken cancellationToken = default) {
        var args = new JsonObject { ["cheat"] = cheat };
        if (enabled.HasValue)
            args["enabled"] = enabled.Value;
        if (value.HasValue)
            args["value"] = value.Value;
        return _bridge.CallToolAsync("dev_set_cheat", args, cancellationToken);
    }

    [McpServerTool(Name = "dev_set_stat"), Description(
        "Set run/combat stats or enable stat locks.")]
    public Task<string> DevSetStat(
        [Description("gold, current_hp, max_hp, current_energy, max_energy, stars, orb_slots, potion_slots.")]
        string stat,
        [Description("Target value.")]
        int value,
        [Description("When true, lock stat at value. When false, disable lock. Omit for one-shot set.")]
        bool? @lock = null,
        CancellationToken cancellationToken = default) {
        var args = new JsonObject {
            ["stat"] = stat,
            ["value"] = value,
        };
        if (@lock.HasValue)
            args["lock"] = @lock.Value;
        return _bridge.CallToolAsync("dev_set_stat", args, cancellationToken);
    }

    [McpServerTool(Name = "dev_jump_floor"), Description(
        "Jump a run forward by ACTS (the game's own act transition) and/or FLOORS (synthetic — pads the "
        + "floor counter without playing rooms, for testing floor-gated behaviour like act-3 relic gates). "
        + "Reaches a DEPTH, not a depth by a particular route: use it where the path taken does not matter "
        + "to what is being tested.")]
    public Task<string> DevJumpFloor(
        [Description("Target act INDEX (0-based). Must be at or ahead of the current act.")]
        int? act = null,
        [Description("Target TotalFloor. Synthetic: enters no rooms and draws no RNG. Padding lands in a finished act's history when the run is past act 0, so the current act's history stays exact; ActFloor moves with it.")]
        int? floor = null,
        CancellationToken cancellationToken = default) {
        var args = new JsonObject();
        if (act.HasValue)
            args["act"] = act.Value;
        if (floor.HasValue)
            args["floor"] = floor.Value;
        return _bridge.CallToolAsync("dev_jump_floor", args, cancellationToken);
    }

    [McpServerTool(Name = "dev_enter_room"), Description(
        "Enter a room directly, by event id (e.g. CRYSTAL_SPHERE) or by room type, instead of walking the "
        + "map to find one. Pass 'search' to list matching event ids. Enters a REAL room: the floor advances.")]
    public Task<string> DevEnterRoom(
        [Description("Event model id to enter, e.g. CRYSTAL_SPHERE.")]
        string? event_id = null,
        [Description("Room type when no event is named: Monster, Elite, Boss, Treasure, Shop, RestSite, Event.")]
        string? room = null,
        [Description("List event ids containing this text instead of entering anything. Empty string lists all.")]
        string? search = null,
        CancellationToken cancellationToken = default) {
        var args = new JsonObject();
        // search is checked FIRST by the bridge and returns before entering anything, so forward it alone
        // when given -- sending it alongside event_id/room would silently turn entering a room into a lookup.
        if (search != null) {
            args["search"] = search;
        }
        else {
            if (!string.IsNullOrWhiteSpace(event_id))
                args["event_id"] = event_id.Trim();
            if (!string.IsNullOrWhiteSpace(room))
                args["room"] = room.Trim();
        }
        return _bridge.CallToolAsync("dev_enter_room", args, cancellationToken);
    }
}
