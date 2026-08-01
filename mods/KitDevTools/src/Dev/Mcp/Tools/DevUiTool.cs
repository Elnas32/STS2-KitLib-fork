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
        var matched = new List<Control>();
        foreach (var control in found) {
            if (Contains(Label(control), needle)
                || Contains(control.Name.ToString(), needle)
                || Contains(control.GetType().Name, needle)) {
                matched.Add(control);
            }
        }
        return matched;
    }

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

    static bool IsInteractive(Control control) =>
        control is BaseButton or LineEdit or TextEdit;

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

        foreach (var child in control.GetChildren()) {
            if (child is Label label && !string.IsNullOrWhiteSpace(label.Text))
                return label.Text;
            if (child is RichTextLabel rich && !string.IsNullOrWhiteSpace(rich.Text))
                return rich.Text;
        }

        return string.Empty;
    }

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
