using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using HarmonyLib;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.Unlocks;

namespace KitLib.Mcp.Tools;

/// <summary>
/// Overrides the run's idea of how many runs the save has finished — the "first-ever run" switch.
/// </summary>
/// <remarks>
/// <para>
/// A brand-new save plays a different game for a few rooms: <c>RewardsSet.TryGenerateTutorialRewards</c>
/// replaces the first fights' rewards with hard-coded ones, the first "?" rolls are forced to Event, Event,
/// Monster (<c>UnknownMapPointOdds.Roll</c>), and the first treasure chest is Gorget
/// (<c>TreasureRoomRelicSynchronizer.TryGetRelicForTutorial</c>). Every one of those keys on
/// <c>UnlockState.NumberOfRuns == 0</c>, and on a save that has played once it can never be reached again —
/// so a mod that models those paths could only be checked by wiping a profile.
/// </para>
/// <para>
/// The value written here is the RUN's copy (<c>RunState.UnlockState</c>, plus each player's, which is
/// normally the same object). The global progress figure (<c>ProgressState.NumberOfRuns</c>) is DERIVED —
/// wins plus losses — and is never written from this copy, so a real profile's count is not at risk.
/// The run's own save does carry the copy, which is why the response returns the previous value: put it
/// back before saving a slot you care about.
/// </para>
/// </remarks>
internal sealed class DevSetUnlockRunsTool : IMcpTool {
    static readonly FieldInfo? RunsField = AccessTools.Field(typeof(UnlockState), "<NumberOfRuns>k__BackingField");

    public string Name => "dev_set_unlock_runs";

    public string Description =>
        "Override the run's UnlockState.NumberOfRuns (0 = the first-ever-run tutorial paths: scripted "
        + "first rewards, forced Event/Event/Monster '?' rolls, Gorget from the first chest). Run-local; "
        + "the global progress count is derived and untouched. Returns the previous value so it can be restored.";

    static readonly FieldInfo? EpochsField = AccessTools.Field(typeof(UnlockState), "_unlockedEpochIds");

    public string InputSchemaJson => """
    {
        "type": "object",
        "properties": {
            "runs": {
                "type": "integer",
                "description": "NumberOfRuns to write, 0 or more."
            },
            "clear_epochs": {
                "type": "boolean",
                "description": "Also empty the run's unlocked-epoch set (the tutorial reward script needs BOTH zero runs and zero epochs). The removed ids are returned as 'epochs'."
            },
            "epoch_ids": {
                "type": "array",
                "items": { "type": "string" },
                "description": "Epoch ids to put back into the run's unlocked set (what a previous clear_epochs returned)."
            }
        }
    }
    """;

    public Task<JsonNode> ExecuteAsync(JsonObject args) {
        var state = RunManager.Instance?.DebugOnlyGetState();
        if (state == null)
            return Task.FromResult<JsonNode>(DevCardMcpHelper.Fail("No run is active. Start or load a run first."));

        int? runs = null;
        if (args.TryGetPropertyValue("runs", out var runsNode) && runsNode?.GetValueKind() == JsonValueKind.Number) {
            runs = runsNode.GetValue<int>();
            if (runs < 0)
                return Task.FromResult<JsonNode>(DevCardMcpHelper.Fail("runs must be 0 or more."));
            if (RunsField == null)
                return Task.FromResult<JsonNode>(DevCardMcpHelper.Fail("UnlockState.NumberOfRuns has no reachable backing field — renamed by a game update?"));
        }

        bool clearEpochs = args.TryGetPropertyValue("clear_epochs", out var clearNode) && clearNode?.GetValueKind() == JsonValueKind.True;
        var restore = new List<string>();
        if (args.TryGetPropertyValue("epoch_ids", out var idsNode) && idsNode is JsonArray ids) {
            foreach (JsonNode? id in ids) {
                if (id?.GetValueKind() == JsonValueKind.String)
                    restore.Add(id.GetValue<string>());
            }
        }

        if (runs == null && !clearEpochs && restore.Count == 0)
            return Task.FromResult<JsonNode>(DevCardMcpHelper.Fail("Nothing to do — pass runs, clear_epochs, or epoch_ids."));

        if ((clearEpochs || restore.Count > 0) && EpochsField == null)
            return Task.FromResult<JsonNode>(DevCardMcpHelper.Fail("UnlockState._unlockedEpochIds is unreachable — renamed by a game update?"));

        int previous = state.UnlockState.NumberOfRuns;
        var removed = new SortedSet<string>();

        // The run state and each player usually share one UnlockState; write every distinct instance so a
        // reader on either side sees the same numbers.
        var written = new HashSet<UnlockState>(ReferenceEqualityComparer.Instance);
        foreach (UnlockState unlock in state.Players.Select(p => p.UnlockState).Append(state.UnlockState)) {
            if (!written.Add(unlock))
                continue;

            if (runs is { } value)
                RunsField!.SetValue(unlock, value);

            if (EpochsField?.GetValue(unlock) is HashSet<string> epochs) {
                if (clearEpochs) {
                    removed.UnionWith(epochs);
                    epochs.Clear();
                }

                epochs.UnionWith(restore);
            }
        }

        return Task.FromResult<JsonNode>(new JsonObject {
            ["ok"] = true,
            ["previous"] = previous,
            ["runs"] = state.UnlockState.NumberOfRuns,
            ["epochCount"] = state.UnlockState.EpochUnlockCount(),
            ["epochs"] = new JsonArray(removed.Select(e => (JsonNode)e).ToArray()),
            ["instances"] = written.Count,
        });
    }
}
