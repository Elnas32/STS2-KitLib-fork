using System.Text.Json;
using System.Text.Json.Nodes;
using KitLib.Multiplayer.Cheat;
using KitLib.Settings;

namespace KitLib.Mcp.Tools;

/// <summary>
/// Arms KitLib's multiplayer cheat session from the bridge, and routes the one combat cheat a co-op fight
/// needs (kill all) through host combat sync.
/// </summary>
/// <remarks>
/// <para>
/// In a pseudo co-op run every cheat silently does nothing: <c>KitLibState.CheatsInRun</c> is false (it is
/// not a dev test run), <c>MpCheatSession</c> is never armed because the local opt-in setting defaults off,
/// and <c>CombatEnemyActions.KillAllEnemies</c> refuses a local kill in multiplayer outright — the
/// sidebar's button goes through <c>MpCheatCombatEnemyCoordinator</c> instead. So a co-op scenario could be
/// STARTED over the bridge but not driven: the first fight had to be played out by hand.
/// </para>
/// <para>
/// <c>enable</c> flips the two persisted settings the sidebar reads (multiplayer cheat opt-in, and
/// <c>NormalRunMode.Cheat</c> so the cheat patches are active outside a dev run), then arms the session
/// for the run in progress the way <c>PseudoCoopDeferredInit</c> does at map finish, applies the optimistic
/// config and — as host — publishes it. Both settings persist in KitLib's settings file, which is why the
/// previous values are returned and <c>disable</c> restores them. <c>kill_all</c> is the coordinator's
/// host path, so it is what the sidebar would have done.
/// </para>
/// </remarks>
internal sealed class DevMpCheatsTool : IMcpTool {
    static bool? _previousOptIn;
    static NormalRunMode? _previousMode;

    public string Name => "dev_mp_cheats";

    public string Description =>
        "Multiplayer (pseudo co-op) cheats over the bridge. action=status reports opt-in / armed / active / "
        + "host / run mode; enable turns the multiplayer cheat opt-in and NormalRunMode=Cheat on (persisted "
        + "KitLib settings, previous values returned) and arms the session for the current run; disable "
        + "restores them; kill_all kills every enemy through host combat sync, which is the only path that "
        + "works in a multiplayer fight.";

    public string InputSchemaJson => """
    {
        "type": "object",
        "properties": {
            "action": {
                "type": "string",
                "enum": ["status", "enable", "disable", "kill_all"],
                "description": "status (default), enable, disable or kill_all."
            }
        }
    }
    """;

    public async Task<JsonNode> ExecuteAsync(JsonObject args) {
        string action = args.TryGetPropertyValue("action", out var node) && node?.GetValueKind() == JsonValueKind.String
            ? node.GetValue<string>().Trim().ToLowerInvariant()
            : "status";

        switch (action) {
            case "status":
                return Status("status");

            case "enable": {
                _previousOptIn ??= SettingsStore.Current.MultiplayerCheatOptIn;
                _previousMode ??= KitLibState.NormalRunMode;
                SettingsStore.SetMultiplayerCheatOptIn(true);
                SettingsStore.SetNormalRunMode(NormalRunMode.Cheat);
                if (MpCheatSession.InMultiplayerRun) {
                    MpCheatSession.TryArmSession("mcp_enable", allowWhileDeferredUi: true);
                    if (MpCheatSession.SessionArmed) {
                        MpCheatState.ApplyOptimisticFromKitLibState();
                        MpCheatSync.HostPublishFromKitLibState("mcp_enable");
                    }
                }

                var report = Status("enabled");
                report["previousOptIn"] = _previousOptIn;
                report["previousRunMode"] = _previousMode?.ToString();
                return report;
            }

            case "disable": {
                SettingsStore.SetMultiplayerCheatOptIn(_previousOptIn ?? false);
                SettingsStore.SetNormalRunMode(_previousMode ?? NormalRunMode.Disabled);
                _previousOptIn = null;
                _previousMode = null;
                return Status("disabled");
            }

            case "kill_all": {
                if (!MpCheatSession.InMultiplayerRun)
                    return DevCardMcpHelper.Fail("Not a multiplayer run — use dev_set_cheat kill_all.");
                if (!MpCheatSession.CanUseMultiplayerCheats)
                    return DevCardMcpHelper.Fail($"Multiplayer cheats are not active (armed={MpCheatSession.SessionArmed}, "
                        + $"active={MpCheatState.IsActive}, block={MpCheatSession.LastBlockReason ?? "-"}) — run enable first.");
                string message = MpCheatSession.IsHost
                    ? await MpCheatCombatEnemyCoordinator.TryHostKillAllAsync()
                    : await MpCheatCombatEnemyCoordinator.TryClientRequestKillAllAsync();
                var report = Status("kill_all");
                report["message"] = message;
                return report;
            }

            default:
                return DevCardMcpHelper.Fail("Unknown action; use status, enable, disable or kill_all.");
        }
    }

    private static JsonObject Status(string applied) => new() {
        ["ok"] = true,
        ["applied"] = applied,
        ["inMultiplayerRun"] = MpCheatSession.InMultiplayerRun,
        ["isHost"] = MpCheatSession.IsHost,
        ["localNetId"] = MpCheatSession.ResolveLocalPlayerNetId(),
        ["optIn"] = MpCheatSession.LocalOptIn,
        ["armed"] = MpCheatSession.SessionArmed,
        ["active"] = MpCheatState.IsActive,
        ["canUse"] = MpCheatSession.CanUseMultiplayerCheats,
        ["lastBlockReason"] = MpCheatSession.LastBlockReason,
        ["cheatsInRun"] = KitLibState.CheatsInRun,
        ["normalRunMode"] = KitLibState.NormalRunMode.ToString(),
    };
}
