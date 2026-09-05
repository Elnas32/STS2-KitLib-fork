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

    public string InputSchemaJson => """
    {
        "type": "object",
        "properties": {
            "runs": {
                "type": "integer",
                "description": "The value to write, 0 or more."
            }
        },
        "required": ["runs"]
    }
    """;

    public Task<JsonNode> ExecuteAsync(JsonObject args) {
        var state = RunManager.Instance?.DebugOnlyGetState();
        if (state == null)
            return Task.FromResult<JsonNode>(DevCardMcpHelper.Fail("No run is active. Start or load a run first."));

        if (!args.TryGetPropertyValue("runs", out var runsNode) || runsNode?.GetValueKind() != JsonValueKind.Number)
            return Task.FromResult<JsonNode>(DevCardMcpHelper.Fail("Missing or invalid runs (an integer)."));

        int runs = runsNode.GetValue<int>();
        if (runs < 0)
            return Task.FromResult<JsonNode>(DevCardMcpHelper.Fail("runs must be 0 or more."));

        if (RunsField == null)
            return Task.FromResult<JsonNode>(DevCardMcpHelper.Fail("UnlockState.NumberOfRuns has no reachable backing field — renamed by a game update?"));

        int previous = state.UnlockState.NumberOfRuns;

        // The run state and each player usually share one UnlockState; write every distinct instance so a
        // reader on either side sees the same number.
        var written = new HashSet<UnlockState>(ReferenceEqualityComparer.Instance);
        foreach (UnlockState unlock in state.Players.Select(p => p.UnlockState).Append(state.UnlockState)) {
            if (written.Add(unlock))
                RunsField.SetValue(unlock, runs);
        }

        return Task.FromResult<JsonNode>(new JsonObject {
            ["ok"] = true,
            ["previous"] = previous,
            ["runs"] = state.UnlockState.NumberOfRuns,
            ["instances"] = written.Count,
        });
    }
}
