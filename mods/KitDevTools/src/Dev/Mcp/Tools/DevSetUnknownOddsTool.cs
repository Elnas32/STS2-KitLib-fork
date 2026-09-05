using System.Text.Json;
using System.Text.Json.Nodes;
using MegaCrit.Sts2.Core.Odds;
using MegaCrit.Sts2.Core.Rooms;
using MegaCrit.Sts2.Core.Runs;

namespace KitLib.Mcp.Tools;

/// <summary>
/// Sets the CURRENT odds of one room type for the run's "?" (unknown map point) roll.
/// </summary>
/// <remarks>
/// <para>
/// A "?" node resolves through <c>UnknownMapPointOdds.Roll</c>: one draw over the non-event odds in
/// dictionary order (Monster, Elite, Treasure, Shop), Event otherwise. Treasure starts at 2% and Shop at
/// 3%, so a scenario that needs "the next ? is a treasure chest" was a seed hunt of dozens of restarts.
/// Writing the current odds — the same dictionary the roll reads — makes the next resolution a choice:
/// set Treasure to 1 and Monster to 0 and the next "?" IS a chest, with the game's own roll still spending
/// its draw. Base odds (what a rolled type resets to) are left alone; pass <c>reset: true</c> to put every
/// current value back to base.
/// </para>
/// <para>
/// A mod that FORECASTS the "?" queue from these odds (Augury) sees the same numbers, which is the point:
/// forecast and outcome are compared under one input rather than under a coincidence.
/// </para>
/// </remarks>
internal sealed class DevSetUnknownOddsTool : IMcpTool {
    public string Name => "dev_set_unknown_odds";

    public string Description =>
        "Set the CURRENT odds of one room type for the run's '?' map-point roll (Monster, Elite, Treasure, "
        + "Shop; Event is the remainder). Treasure=1 with Monster=0 makes the next '?' a chest. "
        + "Pass reset=true to restore every current value to its base. Returns all four current odds.";

    public string InputSchemaJson => """
    {
        "type": "object",
        "properties": {
            "room_type": {
                "type": "string",
                "description": "Monster, Elite, Treasure or Shop."
            },
            "odds": {
                "type": "number",
                "description": "New current odds for that type, 0..1 (negative disables the type, as the game does for Elite in act 1)."
            },
            "reset": {
                "type": "boolean",
                "description": "Restore every current value to its base (UnknownMapPointOdds.ResetToBase) instead of setting one."
            }
        }
    }
    """;

    public Task<JsonNode> ExecuteAsync(JsonObject args) {
        var state = RunManager.Instance?.DebugOnlyGetState();
        if (state == null)
            return Task.FromResult<JsonNode>(DevCardMcpHelper.Fail("No run is active. Start or load a run first."));

        UnknownMapPointOdds odds = state.Odds.UnknownMapPoint;

        if (args.TryGetPropertyValue("reset", out var resetNode) && resetNode?.GetValueKind() == JsonValueKind.True) {
            odds.ResetToBase();
            return Task.FromResult<JsonNode>(Report(odds, "reset to base"));
        }

        if (!args.TryGetPropertyValue("room_type", out var typeNode) || typeNode?.GetValueKind() != JsonValueKind.String
            || !Enum.TryParse(typeNode.GetValue<string>(), ignoreCase: true, out RoomType roomType)) {
            return Task.FromResult<JsonNode>(DevCardMcpHelper.Fail("Missing or invalid room_type (Monster, Elite, Treasure, Shop)."));
        }

        if (!args.TryGetPropertyValue("odds", out var oddsNode) || oddsNode?.GetValueKind() != JsonValueKind.Number)
            return Task.FromResult<JsonNode>(DevCardMcpHelper.Fail("Missing or invalid odds (a number)."));

        float value = (float)oddsNode.GetValue<double>();
        switch (roomType) {
            case RoomType.Monster: odds.MonsterOdds = value; break;
            case RoomType.Elite: odds.EliteOdds = value; break;
            case RoomType.Treasure: odds.TreasureOdds = value; break;
            case RoomType.Shop: odds.ShopOdds = value; break;
            default:
                return Task.FromResult<JsonNode>(DevCardMcpHelper.Fail($"{roomType} is not a '?' roll outcome; use Monster, Elite, Treasure or Shop."));
        }

        return Task.FromResult<JsonNode>(Report(odds, $"{roomType} = {value}"));
    }

    private static JsonNode Report(UnknownMapPointOdds odds, string applied) => new JsonObject {
        ["ok"] = true,
        ["applied"] = applied,
        ["monster"] = odds.MonsterOdds,
        ["elite"] = odds.EliteOdds,
        ["treasure"] = odds.TreasureOdds,
        ["shop"] = odds.ShopOdds,
        ["event"] = odds.EventOdds,
    };
}
