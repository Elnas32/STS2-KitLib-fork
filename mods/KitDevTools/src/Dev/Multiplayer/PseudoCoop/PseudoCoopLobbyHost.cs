using System;
using System.Linq;
using System.Threading.Tasks;
using Godot;
using KitLib.Host;
using KitLib.Multiplayer.Play;
using KitLib.Multiplayer.SyncBot;
using KitLib.Settings;
using MegaCrit.Sts2.Core.Entities.Multiplayer;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Multiplayer;
using MegaCrit.Sts2.Core.Multiplayer.Game;
using MegaCrit.Sts2.Core.Multiplayer.Game.Lobby;
using MegaCrit.Sts2.Core.Nodes;
using MegaCrit.Sts2.Core.Nodes.Screens.CharacterSelect;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.Saves;
using MegaCrit.Sts2.Core.Unlocks;

namespace KitLib.Multiplayer.PseudoCoop;

/// <summary>Hosts ENet then uses the official character-select MP embark path (same as main-menu multiplayer).</summary>
internal static class PseudoCoopLobbyHost {
    const ushort HostPort = 33771;
    const int MaxPlayers = 4;

    /// <summary>
    /// The ENet host THIS class opened, so <see cref="ReleaseHost"/> can close it again. Only ever set
    /// for a pseudo co-op session, so a real multiplayer game is never touched.
    /// </summary>
    static NetHostGameService? _hostedService;

    public sealed class LaunchOptions {
        public CharacterModel Character { get; init; } = null!;
        public CharacterModel? PhantomCharacter { get; init; }
        public string? Seed { get; init; }
        public bool SyncBotEnabled { get; init; } = true;
        public bool SpawnPhantomPlayer { get; init; } = true;
        public bool SyncBotAutoEndTurn { get; init; } = true;
        public bool MpAiTeammateEnabled { get; init; } = true;
        public bool AutoPresetOnLaunch { get; init; }
    }

    public static Task<(bool ok, string error)> TryStartAsync(LaunchOptions options) {
        options ??= new LaunchOptions();
        if (options.Character == null)
            return Task.FromResult((false, "No character selected."));

        try {
            ApplySettings(options);

            if (RunManager.Instance.IsInProgress)
                RunManager.Instance.CleanUp(graceful: true);

            var game = NGame.Instance;
            if (game == null)
                return Task.FromResult((false, "NGame is not loaded."));

            var mainMenu = game.MainMenu;
            if (mainMenu == null)
                return Task.FromResult((false, "Main menu is not active. Return to the title screen and try again."));

            // Close OUR previous host before binding the same port again. The run-end release below
            // is not enough on its own: a session that never reached a clean run end -- a wedged run
            // the player walked away from, or one abandoned without the official quit path -- leaves
            // the old host bound, and StartHost then fails with CantCreate for the rest of the
            // process' life. This is the case actually observed.
            ReleaseHost();

#if STS2_STABLE_PROFILE
            var netService = new NetHostGameService();
#else
            var netService = new NetHostGameService(PeerVersionInfo.LocalDefault());
#endif
            var hostError = netService.StartENetHost(HostPort, MaxPlayers);
            if (hostError.HasValue)
                return Task.FromResult((false, $"ENet host failed: {hostError.Value}"));

            // Remember it so the run end can close it again -- see ReleaseHost.
            _hostedService = netService;

            var charSelect = mainMenu.SubmenuStack.GetSubmenuType<NCharacterSelectScreen>();
            // AfterInitialized (inside InitializeMultiplayerAsHost) wires RemoteCursor etc.
            // Do not call RemoteCursor.Initialize again — it Disposes the lobby InputSynchronizer.
            charSelect.InitializeMultiplayerAsHost(netService, MaxPlayers);

            var lobby = charSelect.Lobby;
            lobby.SetLocalCharacter(options.Character);
            TrySetSeed(lobby, options.Seed);

            // Do not Push char select onto the main-menu stack: SetCurrentScene frees the menu
            // while StartNewMultiplayerRun is still running, which can tear down the embark flow.
            KitLibPseudoCoopOps.EnsureMultiplayerDevActive?.Invoke("pseudo_coop_host");
            KitLibState.PseudoCoopLaunchPending = true;
            KitLibState.PseudoCoopDeferHeavyUi = true;
            KitLibState.PseudoCoopDeferMpCheatPublish = true;
            KitLibState.PseudoCoopAwaitingMapFinish = true;
            lobby.SetReady(ready: true);

            KitLog.Info("PseudoCoop", $"Host lobby ready; embarked without pushing character select UI.");
            return Task.FromResult((true, string.Empty));
        }
        catch (Exception ex) {
            KitLog.Warn("PseudoCoop", $"Host start failed: {ex}");
            return Task.FromResult((false, ex.Message));
        }
    }

    /// <summary>
    /// Closes the ENet host opened by <see cref="TryStartAsync"/>. Leaving a pseudo co-op run does not
    /// go through the official host-quit path, so nothing else released port 33771 and the NEXT
    /// "Start pseudo co-op" in the same session died on <c>StartHost</c> with <c>CantCreate</c> --
    /// only a game restart cleared it. Safe to call when no host was opened, and it never touches a
    /// real multiplayer session because <see cref="_hostedService"/> is only set by this class.
    /// </summary>
    internal static void ReleaseHost() {
        var service = _hostedService;
        _hostedService = null;
        if (service == null) return;

        try {
            service.Disconnect(NetError.Quit);
            KitLog.Info("PseudoCoop", $"Released ENet host on port {HostPort}.");
        }
        catch (Exception ex) {
            KitLog.Warn("PseudoCoop", $"Releasing ENet host failed: {ex.Message}");
        }
    }

    /// <summary>
    /// Applies the requested seed, tolerating the lobby listener's refusal to acknowledge it.
    /// </summary>
    /// <remarks>
    /// <c>StartRunLobby.SetSeed</c> assigns <c>Seed</c> and broadcasts <c>LobbySeedChangedMessage</c>
    /// BEFORE it notifies the listener, and the listener here is the standard
    /// <c>NCharacterSelectScreen</c>, whose <c>SeedChanged()</c> is a stub that unconditionally throws
    /// <c>NotImplementedException("Seed should not be changed in standard mode!")</c> — seeds belong to
    /// the custom-run screen. So the seed IS applied and synced; only the UI callback fails, and that
    /// exception used to escape all the way out of TryStartAsync and abort the whole host start. Any
    /// non-empty seed therefore made "Start pseudo co-op" fail outright.
    /// </remarks>
    static void TrySetSeed(StartRunLobby lobby, string? seed) {
        if (string.IsNullOrWhiteSpace(seed)) return;

        var trimmed = seed.Trim();
        try {
            lobby.SetSeed(trimmed);
        }
        catch (NotImplementedException) {
            KitLog.Debug("PseudoCoop",
                $"Seed '{trimmed}' applied; the standard character-select screen refused the UI notification.");
        }

        if (lobby.Seed != trimmed)
            KitLog.Warn("PseudoCoop", $"Seed '{trimmed}' was not applied (lobby seed is '{lobby.Seed}').");
    }

    static void ApplySettings(LaunchOptions options) {
        AiSessionSettings.AutoPlayEnabled = false;
        AiSessionSettings.SyncBotEnabled = options.SyncBotEnabled;
        AiSessionSettings.SyncBotSpawnPhantomPlayer = options.SpawnPhantomPlayer;
        AiSessionSettings.PhantomCharacter = options.SpawnPhantomPlayer
            ? options.PhantomCharacter ?? options.Character
            : null;
        SettingsStore.Current.SyncBotAutoEndTurn = options.SyncBotAutoEndTurn;
        AiSessionSettings.MpAiTeammateEnabled = options.MpAiTeammateEnabled;
        AiSessionSettings.PseudoCoopAutoPresetOnLaunch = options.AutoPresetOnLaunch;
        PseudoCoopBootstrap.TryAutoPresetOnLaunch();
        HostDrivenPeers.Refresh();
        MpCheatSyncBot.RefreshSimulatedPeers();
    }
}
