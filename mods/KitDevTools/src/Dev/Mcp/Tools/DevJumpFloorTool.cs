using System.Text.Json;
using System.Text.Json.Nodes;
using HarmonyLib;
using MegaCrit.Sts2.Core.Map;
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
///   nobody entered. Nothing is drawn, no reward is generated and no stream moves — the run simply
///   believes it is deeper. That is the point for a floor-gated check, and it is a lie for anything that
///   reads what those rooms CONTAINED.</item>
/// </list>
/// <para>
/// The padding entries are typed <c>MapPointType.Monster</c> because that is what most of a real act is,
/// and are marked in the response as <c>syntheticFloors</c> so a caller can say so in a bug report.
/// </para>
/// </remarks>
internal sealed class DevJumpFloorTool : IMcpTool {
    public string Name => "dev_jump_floor";

    public string Description =>
        "Jump a run forward by ACTS (the game's own act transition) and/or FLOORS (synthetic — pads the "
        + "floor counter without playing rooms, for testing floor-gated behaviour like act-3 relic gates). "
        + "Use with a run active.";

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
                "description": "Target TotalFloor. Synthetic: pads the floor history, entering no rooms and drawing no RNG. Applied after any act jump."
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
                    return Fail($"Act transition did not advance past {before} — the run may be in a room that blocks it.")
                        .Also(o => o["actsEntered"] = actsEntered);
                }
            }
        }

        int floorsAdded = 0;
        string? floorNote = null;
        if (targetFloor is { } floor) {
            if (floor < state.TotalFloor) {
                return Fail($"Floor {floor} is behind the current floor {state.TotalFloor}; this only moves forward.")
                    .Also(o => o["actsEntered"] = actsEntered);
            }

            if (History(state) is not { } history) {
                return Fail("Could not reach the run's floor history — the field may have been renamed by a game update.")
                    .Also(o => o["actsEntered"] = actsEntered);
            }

            // The CURRENT act's list, created if the act has not recorded a room yet.
            while (history.Count <= state.CurrentActIndex) {
                history.Add(new List<MapPointHistoryEntry>());
            }

            var current = history[state.CurrentActIndex];
            while (state.TotalFloor < floor) {
                current.Add(new MapPointHistoryEntry(MapPointType.Monster, state));
                floorsAdded++;
            }

            state.ActFloor += floorsAdded;
            floorNote = floorsAdded > 0
                ? "Floors are synthetic: no rooms were entered, no rewards generated and no RNG stream moved."
                : null;
        }

        return new JsonObject {
            ["ok"] = true,
            ["actIndex"] = state.CurrentActIndex,
            ["actsEntered"] = actsEntered,
            ["totalFloor"] = state.TotalFloor,
            ["actFloor"] = state.ActFloor,
            ["syntheticFloors"] = floorsAdded,
            ["players"] = state.Players.Count,
            ["note"] = floorNote,
        };
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
}

internal static class JsonObjectExtensions {
    /// <summary>Attach extra context to an error without breaking the single-expression return.</summary>
    public static JsonObject Also(this JsonObject obj, System.Action<JsonObject> configure) {
        configure(obj);
        return obj;
    }
}
