using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using KitLib.Actions;
using KitLib.Multiplayer.Cheat;
using MegaCrit.Sts2.Core.Models;

namespace KitLib.Mcp.Tools;

/// <summary>
/// Grants a potion to the local player — the potion counterpart of <see cref="DevAddCardTool"/> and
/// <see cref="DevAddRelicTool"/>.
/// </summary>
/// <remarks>
/// Potions were the last item type with no tool: cards and monsters had one, relics gained one, and a
/// potion could still only be obtained by playing until one dropped. Anything that keys off carrying a
/// specific potion — a mod that reads the belt, an event that offers one, the reward screen's
/// "potion slots full" branch — was therefore unreachable without a human at the keyboard.
/// </remarks>
internal sealed class DevAddPotionTool : IMcpTool {
    public string Name => "dev_add_potion";

    public string Description =>
        "Grant a potion to the local player by model id (same API as the DevMode potion browser). "
        + "Pass 'search' instead to look an id up.";

    public string InputSchemaJson => """
    {
        "type": "object",
        "properties": {
            "potion_id": {
                "type": "string",
                "description": "Potion model ID, e.g. FIRE_POTION, BLOCK_POTION."
            },
            "search": {
                "type": "string",
                "description": "List potion ids containing this text (case-insensitive) instead of granting. Empty string lists every potion."
            }
        }
    }
    """;

    public async Task<JsonNode> ExecuteAsync(JsonObject args) {
        // Lookup reads the model database only, so an id resolves from the main menu with no run.
        if (args.TryGetPropertyValue("search", out var searchNode)
            && searchNode?.GetValueKind() == JsonValueKind.String) {
            return Search(searchNode.GetValue<string>() ?? string.Empty);
        }

        if (!DevCardMcpHelper.TryRequireRun(out _, out var player, out var runError))
            return runError!;

        if (!args.TryGetPropertyValue("potion_id", out var idNode)
            || idNode?.GetValueKind() != JsonValueKind.String
            || string.IsNullOrWhiteSpace(idNode.GetValue<string>())) {
            return DevCardMcpHelper.Fail("Missing or invalid potion_id (pass 'search' to look one up).");
        }

        var potionId = idNode.GetValue<string>()!.Trim();
        var potion = PotionActions.GetAllPotions()
            .FirstOrDefault(p => string.Equals(((AbstractModel)p).Id.Entry, potionId, System.StringComparison.OrdinalIgnoreCase));
        if (potion == null)
            return DevCardMcpHelper.Fail($"Potion not found: '{potionId}'. Pass 'search' to list ids.");

        // Mirrors DevAddRelicTool. PotionActions.AddPotion only WARNS in a multiplayer run and grants
        // nothing, so refuse loudly rather than reporting a success that did not happen.
        if (MpCheatSession.InMultiplayerRun)
            return DevCardMcpHelper.Fail("Multiplayer potion add is not supported via MCP.");

        // A full belt silently drops the potion, which would read as a success here.
        if (!player.HasOpenPotionSlots)
            return DevCardMcpHelper.Fail("No open potion slots — discard one first.");

        await PotionActions.AddPotion(player, potion);

        return new JsonObject {
            ["ok"] = true,
            ["potionId"] = ((AbstractModel)potion).Id.Entry,
            ["name"] = PotionActions.GetPotionDisplayName(potion),
        };
    }

    private static JsonNode Search(string needle) {
        var matches = new JsonArray();
        foreach (var potion in PotionActions.GetAllPotions()) {
            var entry = ((AbstractModel)potion).Id.Entry;
            if (needle.Length > 0 && entry.IndexOf(needle, System.StringComparison.OrdinalIgnoreCase) < 0)
                continue;

            matches.Add(new JsonObject {
                ["id"] = entry,
                ["name"] = PotionActions.GetPotionDisplayName(potion),
            });
        }

        return new JsonObject {
            ["ok"] = true,
            ["count"] = matches.Count,
            ["matches"] = matches,
        };
    }
}
