using System.Text.Json;
using System.Text.Json.Nodes;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Context;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Runs;

namespace KitLib.Mcp.Tools;

/// <summary>
/// Melts one of the local player's relics, the way Toy Box's wax does.
/// </summary>
/// <remarks>
/// A melted relic stays in the inventory but is skipped by every hook walk (<c>RelicModel.IsMelted</c>), so
/// "does this mirror still count a melted relic" is a real class of bug — and nothing in the dev tools could
/// set the flag: the only path in the game is Toy Box's own <c>RelicCmd.Melt</c>, behind a specific pull.
/// This runs that same command on a chosen owned relic, so the state is the game's, not a fake.
/// </remarks>
internal sealed class DevMeltRelicTool : IMcpTool {
    public string Name => "dev_melt_relic";

    public string Description =>
        "Melt one of the local player's relics by model id (the game's own RelicCmd.Melt, as Toy Box does): "
        + "it stays in the inventory but no hook fires for it. Use with a run active.";

    public string InputSchemaJson => """
    {
        "type": "object",
        "properties": {
            "relic_id": {
                "type": "string",
                "description": "Model id of an OWNED relic, e.g. PRAYER_WHEEL (case-insensitive)."
            }
        },
        "required": ["relic_id"]
    }
    """;

    public async Task<JsonNode> ExecuteAsync(JsonObject args) {
        var state = RunManager.Instance?.DebugOnlyGetState();
        if (state == null)
            return DevCardMcpHelper.Fail("No run is active. Start or load a run first.");

        if (!args.TryGetPropertyValue("relic_id", out var idNode) || idNode?.GetValueKind() != JsonValueKind.String
            || string.IsNullOrWhiteSpace(idNode.GetValue<string>())) {
            return DevCardMcpHelper.Fail("Missing or invalid relic_id.");
        }

        var me = LocalContext.GetMe(state);
        if (me == null)
            return DevCardMcpHelper.Fail("No local player in this run.");

        string wanted = idNode.GetValue<string>()!.Trim();
        RelicModel? relic = me.Relics.FirstOrDefault(r => string.Equals(r.Id.Entry, wanted, StringComparison.OrdinalIgnoreCase));
        if (relic == null)
            return DevCardMcpHelper.Fail($"The local player does not own {wanted}.");

        if (relic.IsMelted)
            return DevCardMcpHelper.Fail($"{relic.Id.Entry} is already melted.");

        await RelicCmd.Melt(relic);
        return new JsonObject {
            ["ok"] = true,
            ["relicId"] = relic.Id.Entry,
            ["melted"] = relic.IsMelted,
        };
    }
}
