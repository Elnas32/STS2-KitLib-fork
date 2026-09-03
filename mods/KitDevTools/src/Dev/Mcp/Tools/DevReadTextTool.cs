using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Godot;

namespace KitLib.Mcp.Tools;

/// <summary>
/// Reads the text actually on screen — every visible <see cref="Label"/> and
/// <see cref="RichTextLabel"/>, in tree order.
/// </summary>
/// <remarks>
/// <para>
/// Without this an agent can build a scenario and fire a hover but cannot see the ANSWER. Every
/// prediction surface — a forecast panel, a tooltip, a preview — renders into plain text nodes, and
/// <see cref="DevUiTool"/> deliberately lists only interactive controls, so that output was invisible.
/// Verifying a claim like "the panel should list every option" or "that line should name no cards"
/// needed a human looking at the screen.
/// </para>
/// <para>
/// Each entry carries the raw string and a <c>plain</c> copy with BBCode stripped, because the game
/// marks its text up heavily (<c>[gold][b]Cursed Pearl[/b][/gold]</c>) and an assertion should not
/// have to model that. Presence is meaningful on its own too: a panel that should have closed is
/// detectable by its text still being here.
/// </para>
/// </remarks>
internal sealed class DevReadTextTool : IMcpTool {
    public string Name => "dev_read_text";

    public string Description =>
        "Read visible on-screen text (labels, tooltips, panels, forecasts) in tree order. "
        + "Use after dev_hover to read what a preview or tooltip actually says. "
        + "dev_ui lists only interactive controls; this reads the text they sit next to.";

    public string InputSchemaJson => """
    {
        "type": "object",
        "properties": {
            "filter": {
                "type": "string",
                "description": "Only return entries whose text (BBCode stripped) contains this, case-insensitive."
            },
            "limit": {
                "type": "integer",
                "description": "Maximum entries to return (default 120).",
                "default": 120
            },
            "min_length": {
                "type": "integer",
                "description": "Skip text shorter than this (default 1). Raise it to drop single-character labels.",
                "default": 1
            }
        },
        "required": []
    }
    """;

    private static readonly Regex BbCode = new(@"\[/?[^\]]{1,40}\]", RegexOptions.Compiled);

    public Task<JsonNode> ExecuteAsync(JsonObject args) => Task.FromResult(Run(args));

    static JsonNode Run(JsonObject args) {
        if (Engine.GetMainLoop() is not SceneTree tree)
            return DevCardMcpHelper.Fail("Scene tree unavailable.");

        var filter = Str(args, "filter");
        var limit = Int(args, "limit", 120);
        var minLength = Int(args, "min_length", 1);

        var found = new List<Control>();
        Walk(tree.Root, found);

        var entries = new JsonArray();
        var total = 0;
        foreach (var control in found) {
            var raw = TextOf(control);
            if (string.IsNullOrWhiteSpace(raw))
                continue;

            var plain = Strip(raw);
            if (plain.Length < minLength)
                continue;
            if (!string.IsNullOrWhiteSpace(filter)
                && plain.IndexOf(filter.Trim(), System.StringComparison.OrdinalIgnoreCase) < 0)
                continue;

            total++;
            if (entries.Count >= limit)
                continue;

            var entry = new JsonObject {
                ["index"] = entries.Count,
                ["type"] = control.GetType().Name,
                ["name"] = control.Name.ToString(),
                ["plain"] = plain,
                ["rect"] = UiGeometry.Rect(control),
            };
            // Only carry the raw string when the markup actually differs, so a response full of
            // unmarked labels stays readable.
            if (raw != plain)
                entry["raw"] = raw;
            entries.Add(entry);
        }

        return new JsonObject {
            ["ok"] = true,
            ["count"] = entries.Count,
            ["totalMatched"] = total,
            ["truncated"] = total > entries.Count,
            ["texts"] = entries,
        };
    }

    static void Walk(Node node, List<Control> found) {
        // Hidden text is not on screen, and a stacked-behind panel would otherwise look present --
        // which matters here, because "is this panel still up?" is one of the questions being asked.
        if (node is CanvasItem { Visible: false })
            return;

        if (node is Label or RichTextLabel && node is Control control && control.IsVisibleInTree())
            found.Add(control);

        foreach (var child in node.GetChildren())
            Walk(child, found);
    }

    static string TextOf(Control control) => control switch {
        Label label => label.Text,
        RichTextLabel rich => rich.Text,
        _ => string.Empty,
    };

    static string Strip(string text) => BbCode.Replace(text, string.Empty).Trim();

    static string? Str(JsonObject args, string key) =>
        args.TryGetPropertyValue(key, out var node) && node?.GetValueKind() == JsonValueKind.String
            ? node.GetValue<string>()
            : null;

    static int Int(JsonObject args, string key, int fallback) =>
        args.TryGetPropertyValue(key, out var node) && node?.GetValueKind() == JsonValueKind.Number
            ? node.GetValue<int>()
            : fallback;
}
