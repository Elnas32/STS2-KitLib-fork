using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using Godot;
using MegaCrit.Sts2.Core.Nodes.GodotExtensions;

namespace KitLib.Mcp.Tools;

/// <summary>
/// Lists and drives ORDINARY Godot controls — buttons, checkboxes, dropdowns, text fields — anywhere
/// in the scene tree.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="DevHoverTool"/> only reaches four hand-picked node types, because it exists to fire
/// hover-only behaviour. Everything else a human clicks — main-menu entries, dev panel tabs, the
/// pseudo co-op dialog with its checkboxes, character dropdowns and seed field — was unreachable, so
/// any scenario that started from a menu needed a person at the keyboard. This closes that gap: the
/// tool is deliberately generic and type-driven rather than a list of known screens, because the
/// screens change and <c>BaseButton</c> does not.
/// </para>
/// <para>
/// Pressing prefers the game's own <c>NClickableControl.ForceClick</c> where the control has one, so
/// the real handler and every Harmony patch on it run exactly as for a mouse. Plain Godot controls
/// fall back to the engine's own signals, which is what the engine itself emits on a click.
/// </para>
/// </remarks>
internal sealed class DevUiTool : IMcpTool {
    public string Name => "dev_ui";

    public string Description =>
        "List and drive on-screen UI controls (buttons, checkboxes, dropdowns, text fields) by index: "
        + "action 'list' to see what is on screen, then 'click', 'toggle', 'select' or 'set_text'. "
        + "Use this to open menus and fill dialogs -- dev_hover only covers hover-only game surfaces.";

    public string InputSchemaJson => """
    {
        "type": "object",
        "properties": {
            "action": {
                "type": "string",
                "description": "list, click, toggle, select, or set_text.",
                "default": "list"
            },
            "filter": {
                "type": "string",
                "description": "Case-insensitive substring on the control's label, node name or type. Narrows list, and indexes are relative to the FILTERED list."
            },
            "index": {
                "type": "integer",
                "description": "Which control, from the list this tool returns.",
                "default": -1
            },
            "text": {
                "type": "string",
                "description": "For set_text: the value to type into a LineEdit/TextEdit."
            },
            "option": {
                "type": "integer",
                "description": "For select: the item index inside an OptionButton (see its 'options').",
                "default": -1
            },
            "pressed": {
                "type": "boolean",
                "description": "For toggle: the desired state. Omit to flip."
            }
        },
        "required": []
    }
    """;

    public Task<JsonNode> ExecuteAsync(JsonObject args) => Task.FromResult(Run(args));

    static JsonNode Run(JsonObject args) {
        if (Engine.GetMainLoop() is not SceneTree tree)
            return DevCardMcpHelper.Fail("Scene tree unavailable.");

        var action = Str(args, "action")?.ToLowerInvariant() ?? "list";
        var filter = Str(args, "filter");

        var controls = Collect(tree.Root, filter);
        var listed = new JsonArray();
        for (int i = 0; i < controls.Count; i++)
            listed.Add(Describe(controls[i], i));

        if (action == "list")
            return new JsonObject { ["ok"] = true, ["count"] = listed.Count, ["controls"] = listed };

        var index = Int(args, "index", -1);
        if (index < 0 || index >= controls.Count)
            return DevCardMcpHelper.Fail($"index {index} out of range — {controls.Count} control(s) match.");

        var control = controls[index];
        return action switch {
            "click" => Click(control, index),
            "toggle" => Toggle(control, index, args),
            "select" => Select(control, index, Int(args, "option", -1)),
            "set_text" => SetText(control, index, Str(args, "text") ?? string.Empty),
            _ => DevCardMcpHelper.Fail($"Unknown action '{action}'. Use list, click, toggle, select, or set_text."),
        };
    }

    static JsonNode Click(Control control, int index) {
        switch (control) {
            // The game's own programmatic press: the real handler and its Harmony patches run.
            case NClickableControl clickable:
                clickable.ForceClick();
                break;

            case BaseButton { Disabled: true }:
                return DevCardMcpHelper.Fail($"{Label(control)} is disabled.");

            // A toggle's "click" is a state change; emitting Pressed alone leaves the box unticked.
            case BaseButton { ToggleMode: true } toggle:
                toggle.ButtonPressed = !toggle.ButtonPressed;
                toggle.EmitSignal(BaseButton.SignalName.Toggled, toggle.ButtonPressed);
                break;

            case BaseButton button:
                button.EmitSignal(BaseButton.SignalName.Pressed);
                break;

            default:
                return DevCardMcpHelper.Fail($"{control.GetType().Name} is not clickable.");
        }

        return Ok(control, index, "clicked");
    }

    static JsonNode Toggle(Control control, int index, JsonObject args) {
        if (control is not BaseButton { ToggleMode: true } toggle)
            return DevCardMcpHelper.Fail($"{Label(control)} is not a toggle — use click.");

        var desired = args.TryGetPropertyValue("pressed", out var node)
            && node?.GetValueKind() is JsonValueKind.True or JsonValueKind.False
                ? node.GetValue<bool>()
                : !toggle.ButtonPressed;

        if (toggle.ButtonPressed == desired)
            return Ok(control, index, "unchanged");

        toggle.ButtonPressed = desired;
        toggle.EmitSignal(BaseButton.SignalName.Toggled, desired);
        return Ok(control, index, desired ? "checked" : "unchecked");
    }

    static JsonNode Select(Control control, int index, int option) {
        if (control is not OptionButton picker)
            return DevCardMcpHelper.Fail($"{Label(control)} is not a dropdown.");
        if (option < 0 || option >= picker.ItemCount)
            return DevCardMcpHelper.Fail($"option {option} out of range — {picker.ItemCount} item(s).");

        picker.Select(option);
        // Select() alone does NOT notify: Godot only emits ItemSelected for a real user pick, so the
        // screen would keep its previous value while the dropdown showed the new one.
        picker.EmitSignal(OptionButton.SignalName.ItemSelected, option);
        return Ok(control, index, $"selected '{picker.GetItemText(option)}'");
    }

    static JsonNode SetText(Control control, int index, string text) {
        switch (control) {
            case LineEdit line:
                line.Text = text;
                line.EmitSignal(LineEdit.SignalName.TextChanged, text);
                break;
            case TextEdit edit:
                edit.Text = text;
                edit.EmitSignal(TextEdit.SignalName.TextChanged);
                break;
            default:
                return DevCardMcpHelper.Fail($"{Label(control)} is not a text field.");
        }

        return Ok(control, index, $"set to '{text}'");
    }

    static JsonObject Ok(Control control, int index, string result) => new() {
        ["ok"] = true,
        ["index"] = index,
        ["result"] = result,
        ["control"] = Describe(control, index),
    };

    static List<Control> Collect(Node root, string? filter) {
        var found = new List<Control>();
        Walk(root, found);

        if (string.IsNullOrWhiteSpace(filter))
            return found;

        var needle = filter.Trim();
        var matched = new List<(Control Control, int Rank)>();
        foreach (var control in found) {
            var rank = MatchRank(control, needle);
            if (rank >= 0)
                matched.Add((control, rank));
        }

        // Best match FIRST, so index 0 is the obvious one. Plain substring order is a trap: filtering
        // for "Relics" during an event matched a Neow option whose body text mentions relics before it
        // matched the rail button captioned exactly "Relics" -- and clicking index 0 took the blessing
        // instead of opening the browser. A wrong click here changes the run, so the ordering has to
        // put an exact caption ahead of prose that merely contains the word.
        return matched
            .OrderBy(m => m.Rank)
            .Select(m => m.Control)
            .ToList();
    }

    /// <summary>Lower is a better match; -1 means no match at all.</summary>
    static int MatchRank(Control control, string needle) {
        var label = Label(control);
        var name = control.Name.ToString();
        var type = control.GetType().Name;

        if (Equals(label, needle)) return 0;
        if (Equals(name, needle)) return 1;
        if (Equals(type, needle)) return 2;
        if (StartsWith(label, needle)) return 3;
        if (StartsWith(name, needle)) return 4;
        if (Contains(label, needle)) return 5;
        if (Contains(name, needle)) return 6;
        if (Contains(type, needle)) return 7;
        return -1;
    }

    static bool Equals(string value, string needle) =>
        string.Equals(value, needle, System.StringComparison.OrdinalIgnoreCase);

    static bool StartsWith(string value, string needle) =>
        !string.IsNullOrEmpty(value)
        && value.StartsWith(needle, System.StringComparison.OrdinalIgnoreCase);

    static void Walk(Node node, List<Control> found) {
        // Hidden subtrees are not clickable for a human either, and a menu that is merely stacked
        // behind another would otherwise offer indexes that do nothing.
        if (node is CanvasItem { Visible: false })
            return;

        if (node is Control control && IsInteractive(control) && control.IsVisibleInTree())
            found.Add(control);

        foreach (var child in node.GetChildren())
            Walk(child, found);
    }

    /// <remarks>
    /// The game's own UI is NOT built on Godot's <c>BaseButton</c>: <c>NClickableControl</c> derives
    /// straight from <c>Control</c> and <c>NButton</c> from that, so a BaseButton-only sweep returns
    /// zero controls on every vanilla screen. KitLib's own panels DO use Godot buttons, hence both.
    /// </remarks>
    static bool IsInteractive(Control control) =>
        control is BaseButton or LineEdit or TextEdit or NClickableControl;

    static JsonObject Describe(Control control, int index) {
        var described = new JsonObject {
            ["index"] = index,
            ["type"] = control.GetType().Name,
            ["label"] = Label(control),
            ["name"] = control.Name.ToString(),
        };

        if (control is BaseButton button) {
            described["disabled"] = button.Disabled;
            if (button.ToggleMode)
                described["pressed"] = button.ButtonPressed;
        }

        if (control is NClickableControl clickable)
            described["disabled"] = !clickable.IsEnabled;

        if (control is OptionButton picker) {
            var options = new JsonArray();
            for (int i = 0; i < picker.ItemCount; i++)
                options.Add(picker.GetItemText(i));
            described["selected"] = picker.Selected;
            described["options"] = options;
        }

        if (control is LineEdit line)
            described["text"] = line.Text;

        return described;
    }

    /// <summary>
    /// A control's visible caption. Buttons carry their own text, but plenty of them are icon-only
    /// wrappers around a child Label, which is the only human-readable handle they have.
    /// </summary>
    static string Label(Control control) {
        var text = control switch {
            Button button => button.Text,
            LineEdit line => line.PlaceholderText,
            _ => string.Empty,
        };

        if (!string.IsNullOrWhiteSpace(text))
            return text;

        var fromChildren = FindLabelText(control);
        if (!string.IsNullOrWhiteSpace(fromChildren))
            return fromChildren;

        // Icon-only controls carry their meaning in the tooltip and nowhere else. The Dev rail is 18
        // such buttons, so without this the panels are reachable only by blind index -- you cannot ask
        // for "Cards" or "Cheats", just guess a number and see what opens.
        if (!string.IsNullOrWhiteSpace(control.TooltipText))
            return control.TooltipText;

        // A row like HBox[CheckBox, Label] is the dominant shape in KitLib's own panels, and there the
        // caption is a SIBLING, not a child. Without this a settings page lists dozens of controls with
        // empty labels -- which is only navigable by blind index, so the tool is useless exactly where
        // it is needed most. Nearest-first from our own position, so a row with several controls gives
        // each the caption next to it rather than all of them the first one.
        var parent = control.GetParent();
        if (parent == null)
            return string.Empty;

        var siblings = parent.GetChildren();
        var self = siblings.IndexOf(control);
        for (int distance = 1; distance < siblings.Count; distance++) {
            foreach (var candidate in new[] { self + distance, self - distance }) {
                if (candidate < 0 || candidate >= siblings.Count)
                    continue;
                // Another interactive control ends the row: past it, any caption belongs to that one.
                if (siblings[candidate] is Control other && IsInteractive(other))
                    return string.Empty;

                var found = DirectLabelText(siblings[candidate]);
                if (!string.IsNullOrWhiteSpace(found))
                    return found;
            }
        }

        return string.Empty;
    }

    static string FindLabelText(Node node) {
        foreach (var child in node.GetChildren()) {
            var text = DirectLabelText(child);
            if (!string.IsNullOrWhiteSpace(text))
                return text;
        }
        return string.Empty;
    }

    static string DirectLabelText(Node node) => node switch {
        Label label => label.Text,
        RichTextLabel rich => rich.Text,
        // A caption is often wrapped one level down (a margin/centre container around the Label).
        Container container => FindLabelText(container),
        _ => string.Empty,
    };

    static bool Contains(string haystack, string needle) =>
        !string.IsNullOrEmpty(haystack)
        && haystack.Contains(needle, System.StringComparison.OrdinalIgnoreCase);

    static string? Str(JsonObject args, string key) =>
        args.TryGetPropertyValue(key, out var node) && node?.GetValueKind() == JsonValueKind.String
            ? node.GetValue<string>()
            : null;

    static int Int(JsonObject args, string key, int fallback) =>
        args.TryGetPropertyValue(key, out var node) && node?.GetValueKind() == JsonValueKind.Number
            ? node.GetValue<int>()
            : fallback;
}
