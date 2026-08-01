using System.Collections.Generic;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using Godot;
using KitLib.AI.Sts2.Helpers;
using MegaCrit.Sts2.Core.Nodes.GodotExtensions;
using MegaCrit.Sts2.Core.Nodes.Relics;
using MegaCrit.Sts2.Core.Nodes.RestSite;
using MegaCrit.Sts2.Core.Nodes.Screens.CardSelection;
using MegaCrit.Sts2.Core.Nodes.Screens.Map;

namespace KitLib.Mcp.Tools;

/// <summary>
/// Focuses (hovers) a UI element, so hover-driven behaviour can be exercised without a mouse.
/// </summary>
/// <remarks>
/// <para>
/// The bridge could CLICK things but never HOVER one, and a whole class of mod behaviour — every preview,
/// tooltip and forecast — exists only on hover. Those surfaces were mechanically untestable: an agent could
/// walk a route and take a reward, but never see what a mod claimed the reward WOULD be.
/// </para>
/// <para>
/// Focus is delivered by invoking the node's own <c>OnFocus</c> / <c>OnUnfocus</c> override, which is what the
/// game itself calls. Harmony rewrites method BODIES, so a reflected invoke runs every patch attached to them
/// exactly as a real mouse would — that is the property that makes this a faithful test and not a simulation.
/// The methods are protected and declared per node type, so the lookup walks the type chain rather than
/// assuming the level it sits at.
/// </para>
/// <para>
/// The previously focused node is unfocused first, because hover previews key their tip sets by owner and
/// several throw on a double-keyed owner. Call with <c>target: "none"</c> to clear without focusing anything.
/// </para>
/// </remarks>
internal sealed class DevHoverTool : IMcpTool {
    // What is currently focused, innermost last. Normally holds one — a mouse has one pointer — but a panel
    // that OPENS on a hover has to be read while the thing that opened it is still hovered, so
    // release_previous:false stacks onto this instead of replacing. Released in reverse, innermost first,
    // which is the order a real cursor leaves a nested surface.
    private static readonly List<(Node Node, bool ViaSignal)> _focused = new();

    private static readonly StringName MouseEnteredSignal = Control.SignalName.MouseEntered;
    private static readonly StringName MouseExitedSignal = Control.SignalName.MouseExited;

    public string Name => "dev_hover";

    public string Description =>
        "Hover (focus) a UI element so hover-only behaviour fires. target takes an alias (map_node, "
        + "reward_alternative, rest_option, relic, boss_icon, event_option, potion, card, creature, "
        + "treasure_relic) or ANY node type name, including plain Godot Controls a MOD added "
        + "(PanelContainer, HBoxContainer, TextureRect) — those are driven by emitting mouse_entered. "
        + "Use target 'list' to see what is hoverable on screen, 'none' to unhover. Omit index to list "
        + "that target's instances, each tagged via=focus|signal|none. Pair with dev_read_text to "
        + "read the panel the hover opens.";

    public string InputSchemaJson => """
    {
        "type": "object",
        "properties": {
            "target": {
                "type": "string",
                "description": "Alias (map_node, reward_alternative, rest_option, relic, boss_icon, event_option, potion, card, creature, treasure_relic), any node type name (e.g. NTopBarDeckButton, or a mod's own PanelContainer / TextureRect), 'list' to discover what is on screen, or 'none' to unhover."
            },
            "index": {
                "type": "integer",
                "description": "Which one to hover, from the list this tool returns. Omit to only list them.",
                "default": -1
            },
            "click": {
                "type": "boolean",
                "description": "Also click it after focusing. Lets a hover preview be compared against what committing to it actually does.",
                "default": false
            },
            "release_previous": {
                "type": "boolean",
                "description": "Unhover what is already hovered first (default true). Pass false to STACK a hover — needed to read a panel that a hover opened, since such panels poll the cursor's real position and close as soon as their opener reports an exit. 'none' releases the whole stack.",
                "default": true
            }
        },
        "required": ["target"]
    }
    """;

    public Task<JsonNode> ExecuteAsync(JsonObject args) => Task.FromResult(Run(args));

    private static JsonNode Run(JsonObject args) {
        if (!args.TryGetPropertyValue("target", out var targetNode)
            || targetNode?.GetValueKind() != JsonValueKind.String) {
            return DevCardMcpHelper.Fail("Missing or invalid target.");
        }

        var target = targetNode.GetValue<string>()!.Trim().ToLowerInvariant();
        if (target == "none") {
            var cleared = UnfocusAll();
            return new JsonObject { ["ok"] = true, ["unfocused"] = cleared };
        }

        if (Engine.GetMainLoop() is not SceneTree tree)
            return DevCardMcpHelper.Fail("Scene tree unavailable.");

        if (target == "list") {
            var types = ListHoverableTypes(tree.Root);
            return new JsonObject { ["ok"] = true, ["count"] = types.Count, ["hoverableTypes"] = types };
        }

        List<Node>? candidates = Collect(target, tree.Root);
        if (candidates == null)
            return DevCardMcpHelper.Fail(
                $"Unknown target '{target}'. Pass an alias, a node type name, or 'list' to see what is on screen.");

        var listed = new JsonArray();
        foreach (var node in candidates)
            listed.Add(Describe(node));

        var index = args.TryGetPropertyValue("index", out var indexNode)
            && indexNode?.GetValueKind() == JsonValueKind.Number
                ? indexNode.GetValue<int>()
                : -1;

        // No index is a LIST request, not an error — discovering what is on screen is the first half of
        // driving it, and the indices this returns are what the caller then hovers by.
        if (index < 0)
            return new JsonObject { ["ok"] = true, ["target"] = target, ["count"] = listed.Count, ["hoverable"] = listed };

        if (index >= candidates.Count)
            return DevCardMcpHelper.Fail($"index {index} out of range — {candidates.Count} '{target}' on screen.");

        // Releasing what is already hovered is right for a pointer moving between siblings, and WRONG for
        // reading a panel a hover opened: those panels poll the cursor's real position and take themselves
        // down the moment their opener reports an exit, so the rows would be gone before they could be read.
        var releasePrevious = !args.TryGetPropertyValue("release_previous", out var releaseNode)
            || releaseNode?.GetValueKind() != JsonValueKind.False;
        if (releasePrevious)
            UnfocusAll();

        var chosen = candidates[index];

        // OnFocus first, because that is what the game calls on its own nodes. A node WITHOUT one is not
        // un-hoverable: a Control that wires Godot's mouse_entered signal directly is hovered by emitting
        // it, which is the same call Godot makes when a real cursor crosses the control. That covers every
        // surface a MOD adds to a screen — panels, rows and injected icons are plain Controls with signal
        // handlers, not game node types with an override — and those were unreachable until now.
        var viaSignal = false;
        if (!Invoke(chosen, "OnFocus")) {
            if (!EmitMouseSignal(chosen, MouseEnteredSignal))
                return DevCardMcpHelper.Fail(
                    $"{chosen.GetType().Name} has no OnFocus to invoke and nothing listening on mouse_entered.");

            viaSignal = true;
        }

        _focused.Add((chosen, viaSignal));

        var clicked = false;
        if (args.TryGetPropertyValue("click", out var clickNode)
            && clickNode?.GetValueKind() == JsonValueKind.True) {
            if (chosen is not NClickableControl clickable)
                return DevCardMcpHelper.Fail($"{chosen.GetType().Name} is not clickable.");

            // ForceClick is the game's own programmatic press, so the button's real handler runs -- which is
            // what makes a hover preview comparable against actually committing to it.
            clickable.ForceClick();
            clicked = true;
            _focused.Clear();   // the node this focused is usually torn down by its own handler
        }

        return new JsonObject {
            ["ok"] = true,
            ["target"] = target,
            ["index"] = index,
            ["hovered"] = Describe(chosen),
            ["clicked"] = clicked,
            ["count"] = listed.Count,
        };
    }

    /// <summary>Friendly names for the surfaces asked for most; any other type name works directly.</summary>
    private static readonly Dictionary<string, string> Aliases = new() {
        ["map_node"] = nameof(NMapPoint),
        ["reward_alternative"] = nameof(NCardRewardAlternativeButton),
        ["rest_option"] = nameof(NRestSiteButton),
        ["relic"] = nameof(NRelicInventoryHolder),
        ["boss_icon"] = "NTopBarBossIcon",
        ["event_option"] = "NEventOptionButton",
        ["potion"] = "NPotionHolder",
        ["card"] = "NCardHolder",
        ["creature"] = "NCreature",
        ["treasure_relic"] = "NTreasureRoomRelicHolder",
    };

    /// <remarks>
    /// Resolution is by TYPE NAME rather than a fixed list, because 124 node types implement
    /// <c>OnFocus()</c> and hard-coding a handful means a new surface needs a new build every time.
    /// The aliases above are shorthand for the common ones; anything else is reachable by naming its
    /// type, and <c>target: "list"</c> reports what is actually on screen right now.
    /// </remarks>
    private static List<Node>? Collect(string target, Node root) {
        var typeName = Aliases.TryGetValue(target, out var mapped) ? mapped : target;

        var matches = new List<Node>();
        CollectByTypeName(root, typeName, matches);
        return matches.Count > 0 || IsKnownTypeName(root, typeName) ? Widen(matches) : null;
    }

    private static void CollectByTypeName(Node node, string typeName, List<Node> found) {
        if (node is CanvasItem { Visible: false })
            return;

        if (string.Equals(node.GetType().Name, typeName, StringComparison.OrdinalIgnoreCase))
            found.Add(node);

        foreach (var child in node.GetChildren())
            CollectByTypeName(child, typeName, found);
    }

    /// <summary>
    /// Distinguishes "no such surface on screen" (an empty list, which is a valid answer) from
    /// "you named something that is not a node type here" (an error worth reporting).
    /// </summary>
    private static bool IsKnownTypeName(Node root, string typeName) =>
        Aliases.ContainsValue(typeName)
        || typeName.StartsWith("N", StringComparison.Ordinal)
        // Mod-added surfaces are plain Godot Controls (PanelContainer, HBoxContainer, TextureRect), so the
        // N-prefix rule would reject the very names 'list' now reports as hoverable.
        || ClassDB.ClassExists(typeName);

    /// <summary>Every focusable type currently on screen, so a caller can discover targets.</summary>
    private static JsonArray ListHoverableTypes(Node root) {
        var counts = new Dictionary<string, int>();
        CountFocusable(root, counts);

        var reverse = new Dictionary<string, string>();
        foreach (var pair in Aliases)
            reverse[pair.Value] = pair.Key;

        var arr = new JsonArray();
        foreach (var pair in counts.OrderByDescending(p => p.Value)) {
            var entry = new JsonObject {
                ["type"] = pair.Key,
                ["count"] = pair.Value,
            };
            if (reverse.TryGetValue(pair.Key, out var alias))
                entry["alias"] = alias;
            arr.Add(entry);
        }
        return arr;
    }

    private static void CountFocusable(Node node, Dictionary<string, int> counts) {
        if (node is CanvasItem { Visible: false })
            return;

        // "Focusable" is exactly what this tool can drive: a type declaring OnFocus somewhere in its
        // chain, OR a Control listening on mouse_entered. Anything listed here is a valid target value.
        if (node is Control && (HasMethod(node, "OnFocus") || ListensForMouse(node))) {
            var name = node.GetType().Name;
            counts[name] = counts.TryGetValue(name, out var n) ? n + 1 : 1;
        }

        foreach (var child in node.GetChildren())
            CountFocusable(child, counts);
    }

    private static bool HasMethod(Node node, string method) {
        const BindingFlags flags =
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;

        for (var type = node.GetType(); type != null; type = type.BaseType) {
            if (type.GetMethod(method, flags, null, System.Type.EmptyTypes, null) != null)
                return true;
        }
        return false;
    }

    private static List<Node> Widen<T>(List<T> nodes) where T : Node {
        var result = new List<Node>(nodes.Count);
        foreach (var node in nodes) {
            // A queue-freed node is still a child until the end of the frame; hovering one would key a tip
            // set to an owner about to vanish.
            if (GodotObject.IsInstanceValid(node) && !node.IsQueuedForDeletion())
                result.Add(node);
        }
        return result;
    }

    private static JsonObject Describe(Node node) {
        var described = new JsonObject {
            ["type"] = node.GetType().Name,
            ["name"] = node.Name.ToString(),
            // How this one would be driven — and "none" is the useful case: collecting by type name returns
            // every node of that type, most of which are layout containers nothing listens on. Without this
            // the caller can only find the hoverable ones by hovering each in turn and reading the failure.
            ["via"] = HasMethod(node, "OnFocus") ? "focus" : ListensForMouse(node) ? "signal" : "none",
        };

        // A map point's coordinates and room type are what a caller actually selects on, so surface them
        // rather than making them guess from scene order.
        if (node is NMapPoint point) {
            described["row"] = point.Point.coord.row;
            described["col"] = point.Point.coord.col;
            described["pointType"] = point.Point.PointType.ToString();
        }

        return described;
    }

    /// <summary>Releases everything currently hovered, innermost first, and names what it let go.</summary>
    private static JsonArray? UnfocusAll() {
        if (_focused.Count == 0)
            return null;

        var released = new JsonArray();
        for (var i = _focused.Count - 1; i >= 0; i--) {
            var (node, viaSignal) = _focused[i];
            released.Add(node.GetType().Name);
            if (!GodotObject.IsInstanceValid(node))
                continue;

            // Release it the same way it was taken: a node driven through its OnFocus override gets
            // OnUnfocus, one driven through the signal gets mouse_exited. Sending the wrong one leaves the
            // tip keyed to an owner nothing will ever clear, which is exactly the orphaned-tip leak.
            if (viaSignal)
                EmitMouseSignal(node, MouseExitedSignal);
            else
                Invoke(node, "OnUnfocus");
        }

        _focused.Clear();
        return released;
    }

    /// <summary>
    /// Whether this Control listens for the mouse itself rather than through an <c>OnFocus</c> override.
    /// </summary>
    /// <remarks>
    /// Connections are required, not just the signal: EVERY Control declares <c>mouse_entered</c>, so
    /// without this every Label on screen would report as hoverable and the list would say nothing.
    /// </remarks>
    private static bool ListensForMouse(Node node) =>
        node is Control && node.GetSignalConnectionList(MouseEnteredSignal).Count > 0;

    /// <summary>Fires one of the mouse signals, reporting whether anything was actually listening.</summary>
    private static bool EmitMouseSignal(Node node, StringName signal) {
        if (node is not Control || node.GetSignalConnectionList(signal).Count == 0)
            return false;

        node.EmitSignal(signal);
        return true;
    }

    // OnFocus/OnUnfocus are protected and declared on the concrete node type (or a base of it). GetMethod does
    // not return inherited NON-PUBLIC members, so walk the chain with DeclaredOnly instead of assuming a level.
    private static bool Invoke(Node node, string method) {
        const BindingFlags flags =
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;

        for (var type = node.GetType(); type != null; type = type.BaseType) {
            var found = type.GetMethod(method, flags, null, System.Type.EmptyTypes, null);
            if (found == null)
                continue;

            found.Invoke(node, null);
            return true;
        }

        return false;
    }
}
