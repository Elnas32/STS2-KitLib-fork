using System.Text.Json;
using System.Text.Json.Nodes;
using HarmonyLib;
using MegaCrit.Sts2.Core.Map;
using MegaCrit.Sts2.Core.Nodes.Screens.Map;
using MegaCrit.Sts2.Core.Rooms;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.Runs.History;

namespace KitLib.Mcp.Tools;

/// <summary>
/// Moves a run forward in FLOORS and ACTS without playing the rooms in between.
/// </summary>
/// <remarks>
/// <para>
/// Anything gated on how deep the run is was untestable before this. The clearest example: sixteen
/// relics gate on <c>RelicModel.IsBeforeAct3TreasureChest</c> — <c>TotalFloor &lt; (Players.Count &gt; 1 ? 38 : 41)</c>
/// — so checking what happens at that boundary meant playing thirty-odd rooms by hand, every time, and
/// nothing in the dev tools could shorten it.
/// </para>
/// <para>
/// The two halves are not equally honest, and the result says which one ran:
/// </para>
/// <list type="bullet">
///   <item><b>act</b> is the GAME's own transition — <c>RunManager.EnterNextAct</c>, once per act — so the
///   next act's map, ancient and boss are generated exactly as they would be in play.</item>
///   <item><b>floor</b> is SYNTHETIC. <c>RunState.TotalFloor</c> is derived
///   (<c>MapPointHistory.Sum(c =&gt; c.Count)</c>), so raising it means appending history entries for rooms
///   nobody entered. Nothing is drawn, no reward is generated and the run's own RNG stream does not move —
///   the run simply believes it is deeper. That is the point for a floor-gated check, and it is a lie for
///   anything that reads what those rooms CONTAINED.</item>
/// </list>
/// <para>
/// So this is a tool for reaching a DEPTH, not for reaching a depth by a particular route: use it where the
/// path taken does not matter to what is being tested. Two consequences follow from that and are contained
/// rather than hidden:
/// </para>
/// <list type="bullet">
///   <item>The padding goes into a FINISHED act's history whenever the run is past act 0 — see
///   <see cref="PaddingActIndex"/> — because history is indexed by map ROW and the game only ever looks it
///   up for the CURRENT act. Only an act-0 jump can make the live act's own history report rows nobody
///   visited, and the response note says when that happened.</item>
///   <item><c>ActFloor</c> moves with the padding, which is deliberate (it gates the rewards screen at
///   <c>ActFloor &gt; 4</c>) but is also the seed offset for the map and event split-vote animations
///   (<c>Rng.Seed + ActFloor</c>), so those two draw differently afterwards.</item>
/// </list>
/// <para>
/// The padding entries are typed <c>MapPointType.Monster</c> because that is what most of a real act is, and
/// carry no <c>Rooms</c> entry at all — which is what distinguishes them from a real one, since
/// <c>AppendToMapPointHistory</c> always records at least one. The response reports them as
/// <c>syntheticFloors</c> and <c>syntheticFloorAct</c> so a caller can say so in a bug report.
/// </para>
/// </remarks>
internal sealed class DevJumpFloorTool : IMcpTool {
    /// <summary>Ceiling on one call's padding. Act 3's boss sits around floor 50; nothing legitimate needs more.</summary>
    const int MaxSyntheticFloors = 500;

    public string Name => "dev_jump_floor";

    public string Description =>
        "Jump a run forward by ACTS (the game's own act transition) and/or FLOORS (synthetic — pads the "
        + "floor counter without playing rooms, for testing floor-gated behaviour like act-3 relic gates). "
        + "Reaches a DEPTH, not a depth by a particular route: use it where the path taken does not matter "
        + "to what is being tested. Use with a run active.";

    public string InputSchemaJson => """
    {
        "type": "object",
        "properties": {
            "act": {
                "type": "integer",
                "description": "Target act INDEX (0-based). Must be at or ahead of the current act; each step runs the game's real act transition."
            },
            "floor": {
                "type": "integer",
                "description": "Target TotalFloor. Synthetic: pads the floor history, entering no rooms and drawing no RNG. Padding goes into a finished act's history when the run is past act 0, so the current act's history stays exact; ActFloor moves with it. Applied after any act jump."
            }
        },
        "required": []
    }
    """;

    public async Task<JsonNode> ExecuteAsync(JsonObject args) {
        var manager = RunManager.Instance;
        var state = manager?.DebugOnlyGetState();
        if (manager == null || state == null) {
            return Fail("No run is active. Start or load a run first.");
        }

        int? targetAct = ReadInt(args, "act");
        int? targetFloor = ReadInt(args, "floor");
        if (targetAct == null && targetFloor == null) {
            return Fail("Nothing to do — pass act, floor, or both.");
        }

        int actsEntered = 0;
        if (targetAct is { } act) {
            if (act < state.CurrentActIndex) {
                return Fail($"Act {act} is behind the current act {state.CurrentActIndex}; this only moves forward.");
            }

            if (act >= state.Acts.Count) {
                return Fail($"Act {act} does not exist — this run has {state.Acts.Count} act(s), indexes 0..{state.Acts.Count - 1}.");
            }

            while (state.CurrentActIndex < act) {
                int before = state.CurrentActIndex;
                await manager.EnterNextAct();
                actsEntered++;

                // EnterNextAct is the game's own path and can decline (a victory room, a pending vote in
                // co-op). Stop on a no-op rather than spinning until the request looks satisfied.
                if (state.CurrentActIndex == before) {
                    return Fail($"Act transition did not advance past {before} — the run may be in a room that blocks it.", actsEntered);
                }
            }
        }

        int floorsAdded = 0;
        int paddedAct = -1;
        string? floorNote = null;
        if (targetFloor is { } floor) {
            if (floor < state.TotalFloor) {
                return Fail($"Floor {floor} is behind the current floor {state.TotalFloor}; this only moves forward.", actsEntered);
            }

            // A typo in the target would otherwise append entries one at a time on the game thread until
            // it matched, and no real run is anywhere near this deep.
            if (floor - state.TotalFloor > MaxSyntheticFloors) {
                return Fail(
                    $"Refusing to append {floor - state.TotalFloor} synthetic floors (limit {MaxSyntheticFloors}); "
                    + $"the run is at floor {state.TotalFloor}.", actsEntered);
            }

            if (History(state) is not { } history) {
                return Fail("Could not reach the run's floor history — the field may have been renamed by a game update.", actsEntered);
            }

            // The CURRENT act's list, created if the act has not recorded a room yet. Done before choosing
            // where to pad, so the current act's list is guaranteed to be the LAST one -- which is what
            // keeps RunState.CurrentMapPointHistoryEntry (MapPointHistory.Last().Last(), where combat stats
            // are recorded) pointing at a real entry rather than at padding.
            while (history.Count <= state.CurrentActIndex) {
                history.Add(new List<MapPointHistoryEntry>());
            }

            paddedAct = PaddingActIndex(state);
            var target = history[paddedAct];
            while (state.TotalFloor < floor) {
                target.Add(new MapPointHistoryEntry(MapPointType.Monster, state));
                floorsAdded++;
            }

            state.ActFloor += floorsAdded;
            floorNote = floorsAdded > 0 ? BuildFloorNote(paddedAct, state.CurrentActIndex) : null;
        }

        // Land somewhere usable. EnterAct ends in a MapRoom, but the map screen it leaves behind is the one
        // the act-change VOTE would have re-enabled travel on, and jumping skipped the vote — so without this
        // the run sits on a map you cannot hover or travel, which is worse than not having jumped.
        if (actsEntered > 0) {
            await RunManager.Instance!.EnterRoom(new MapRoom());
            NMapScreen.Instance?.SetTravelEnabled(enabled: true);
        }

        return new JsonObject {
            ["ok"] = true,
            ["actIndex"] = state.CurrentActIndex,
            ["actsEntered"] = actsEntered,
            ["totalFloor"] = state.TotalFloor,
            ["actFloor"] = state.ActFloor,
            ["syntheticFloors"] = floorsAdded,
            ["syntheticFloorAct"] = floorsAdded > 0 ? paddedAct : null,
            ["players"] = state.Players.Count,
            ["note"] = floorNote,
        };
    }

    /// <summary>
    /// Which act's history the padding entries go into.
    /// </summary>
    /// <remarks>
    /// A COMPLETED act whenever the run is past act 0, because <c>RunState.GetHistoryEntryFor</c> indexes an
    /// act's list by map ROW (<c>_mapPointHistory[actIndex][coord.row]</c>) and both game paths that use it —
    /// <c>NMapPoint</c>'s node tooltip and <c>RewardSynchronizer</c>'s reward claim — only ever ask about the
    /// CURRENT act. Padding the live act therefore makes rows the player never reached resolve to synthetic
    /// entries instead of to nothing; padding act 0 after the run has left it is never read that way.
    /// In act 0 there is nowhere else to put them, and the note says so.
    /// </remarks>
    static int PaddingActIndex(RunState state) => state.CurrentActIndex > 0 ? 0 : state.CurrentActIndex;

    static string BuildFloorNote(int paddedAct, int currentAct) {
        var where = paddedAct == currentAct
            ? $"act {paddedAct}, the act being played, so history-by-location there now resolves rows nobody visited"
            : $"act {paddedAct}, a finished act, leaving history-by-location in the current act exact";

        return "Floors are synthetic: no rooms were entered and no rewards were generated. Appended to "
            + where + ". The run's own RNG stream did not advance, but ActFloor moved with them and the map "
            + "and event split-vote animations seed off Rng.Seed + ActFloor.";
    }

    // RunState keeps the history in a private backing list; the public property is read-only by design.
    static List<List<MapPointHistoryEntry>>? History(RunState state) =>
        AccessTools.Field(typeof(RunState), "_mapPointHistory")?.GetValue(state)
            as List<List<MapPointHistoryEntry>>;

    static int? ReadInt(JsonObject args, string key) =>
        args.TryGetPropertyValue(key, out var node) && node?.GetValueKind() == JsonValueKind.Number
            ? node.GetValue<int>()
            : null;

    static JsonObject Fail(string error) => new() { ["ok"] = false, ["error"] = error };

    /// <summary>Fails after an act jump already ran, so the caller learns the run moved anyway.</summary>
    static JsonObject Fail(string error, int actsEntered) =>
        new() { ["ok"] = false, ["error"] = error, ["actsEntered"] = actsEntered };
}
