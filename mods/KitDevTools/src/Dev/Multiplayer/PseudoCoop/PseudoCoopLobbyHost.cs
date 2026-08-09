using System;
using System.Linq;
using System.Threading.Tasks;
using Godot;
using KitLib.Host;
using KitLib.Multiplayer.Play;
using KitLib.Multiplayer.SyncBot;
using KitLib.Settings;
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

    public sealed class LaunchOptions {
        public CharacterModel Character { get; init; } = null!;
        public CharacterModel? PhantomCharacter { get; init; }
        public string? Seed { get; init; }
        public bool SyncBotEnabled { get; init; } = true;
        public bool SpawnPhantomPlayer { get; init; } = true;
        public bool SyncBotAutoEndTurn { get; init; } = true;
        public bool MpAiTeammateEnabled { get; init; } = true;
        public bool AutoPresetOnLaunch { get; init; }

        /// <summary>Ascension for the run, or null to keep whatever character select last used. Clamped to
        /// what the lobby allows.</summary>
        public int? Ascension { get; init; }

        /// <summary>Run modifiers, or null for none. Standard multiplayer embark has no modifier UI, so
        /// these are applied by <see cref="Patches.PseudoCoopRunModifiersPatch"/>.</summary>
        public IReadOnlyList<ModifierModel>? Modifiers { get; init; }
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

#if STS2_STABLE_PROFILE
            var netService = new NetHostGameService();
#else
            var netService = new NetHostGameService(PeerVersionInfo.LocalDefault());
#endif
            var hostError = netService.StartENetHost(HostPort, MaxPlayers);
            if (hostError.HasValue)
                return Task.FromResult((false, $"ENet host failed: {hostError.Value}"));

            var charSelect = mainMenu.SubmenuStack.GetSubmenuType<NCharacterSelectScreen>();
            // AfterInitialized (inside InitializeMultiplayerAsHost) wires RemoteCursor etc.
            // Do not call RemoteCursor.Initialize again — it Disposes the lobby InputSynchronizer.
            charSelect.InitializeMultiplayerAsHost(netService, MaxPlayers);

            var lobby = charSelect.Lobby;
            lobby.SetLocalCharacter(options.Character);
            if (!string.IsNullOrWhiteSpace(options.Seed))
                lobby.SetSeed(options.Seed.Trim());
            TrySetAscension(lobby, options.Ascension);
            PseudoCoopRunModifiers.Request(options.Modifiers);

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
    /// Applies the requested ascension through the lobby's own host-side setter, clamped to what the lobby
    /// allows.
    /// </summary>
    /// <remarks>
    /// Ascension is NOT carried over from anything: standard multiplayer embark reads
    /// <c>StartRunLobby.Ascension</c>, which for a host lobby is whatever the character-select ascension
    /// panel last had. Null therefore keeps that behaviour, which is what the launcher UI wants; a caller
    /// restarting an existing run passes the level it was playing at.
    ///
    /// <c>MaxAscension</c> is the minimum unlocked across the lobby's players, so asking for more than that
    /// is clamped rather than refused - the run still starts, and the log says what happened. Unlike
    /// <c>SetSeed</c> and <c>SetModifiers</c>, the listener callback here is real (it just moves the panel),
    /// so this needs no swallowed <c>NotImplementedException</c>.
    /// </remarks>
    static void TrySetAscension(StartRunLobby lobby, int? ascension) {
        if (ascension is not int requested) return;

        var applied = Math.Clamp(requested, 0, lobby.MaxAscension);
        if (applied != requested)
            KitLog.Warn("PseudoCoop",
                $"Ascension {requested} is above the lobby maximum; starting at {applied} instead.");

        if (applied == lobby.Ascension) return;

        lobby.SyncAscensionChange(applied);
        KitLog.Info("PseudoCoop", $"Ascension set to {applied}.");
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
