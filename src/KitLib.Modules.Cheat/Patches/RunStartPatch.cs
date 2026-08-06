using System;
using System.Reflection;
using HarmonyLib;
using KitLib.Cheat;
using KitLib.Presets;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Multiplayer.Game.Lobby;
using MegaCrit.Sts2.Core.Nodes;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.Saves;

namespace KitLib.Patches;

[HarmonyPatch(typeof(RunManager))]
public static class RunStartPatch {
    /// <summary>
    /// Disable save persistence for dev-mode runs.
    /// </summary>
    [HarmonyTargetMethod]
    static MethodBase? DisableSaveTarget() =>
        AccessTools.Method(typeof(RunManager), "SetUpNewSingleplayer")
        ?? AccessTools.Method(typeof(RunManager), "SetUpNewSinglePlayer");

    [HarmonyPrefix]
    public static void DisableSaveForDevRun(ref bool shouldSave) {
        if (KitLibState.InDevRun) {
            shouldSave = false;
            MainFile.Logger.Info("KitLib: Save disabled for dev run.");
        }
    }

    [HarmonyPostfix]
    [HarmonyPatch(nameof(RunManager.Launch))]
    public static void InjectDevContent(RunState __result) {
        if (!KitLibState.InDevRun)
            return;

        MainFile.Logger.Info("KitLib: Injecting dev mode content into run...");

        foreach (var player in __result.Players) {
            InjectForPlayer(player);
        }

        ApplyPendingRestart(__result);
        MainFile.Logger.Info("KitLib: Dev mode content injected successfully.");
    }

    private static void ApplyPendingRestart(RunState runState) {
        // Apply carried-over gold (direct, synchronous)
        if (KitLibState.PendingRestartGold.HasValue) {
            var gold = KitLibState.PendingRestartGold.Value;
            foreach (var player in runState.Players)
                player.Gold = gold;
            KitLog.Info($"Restart: applied gold {gold}.");
        }

        // Apply carried-over cards / relics (async via game command queue)
        if (CheatRestartState.PendingRestartPreset != null) {
            var preset = CheatRestartState.PendingRestartPreset;
            var scope = CheatRestartState.PendingRestartScope;
            KitLog.Info($"Restart: scheduling preset apply (scope: {scope}).");
            TaskHelper.RunSafely(PresetManager.ApplyToRunAsync(preset, scope));
        }

        CheatRestartState.ClearPresetRestart();
        KitLibState.ClearPendingRestart();
    }

    public static void OnRunEnded() {
        KitLibState.OnRunEnded();
    }

    private static void InjectForPlayer(Player player) {
        if (KitLibState.MaxEnergy > 0) {
            player.MaxEnergy = KitLibState.MaxEnergy;
            MainFile.Logger.Info($"KitLib: Set max energy to {KitLibState.MaxEnergy}");
        }
    }
}

/// <summary>
/// Dev and pseudo-coop runs may include mod epochs that fail combat replay serialization on exit.
/// </summary>
[HarmonyPatch(typeof(RunManager), nameof(RunManager.WriteReplay))]
internal static class DevCombatReplaySkipPatch {
    public static bool Prefix(RunManager __instance, bool stopRecording) {
        if (!KitLibState.InDevRun && !KitLibState.IsPseudoCoopSession)
            return true;

        if (stopRecording && __instance.CombatReplayWriter.IsRecordingReplay)
            __instance.CombatReplayWriter.StopRecording();

        return false;
    }
}

[HarmonyPatch(typeof(SaveManager), nameof(SaveManager.SaveProgressFile))]
public static class SaveProgressPatch {
    public static bool Prefix() {
        if (KitLibState.InDevRun) {
            MainFile.Logger.Info("KitLib: Skipping progress save for dev run.");
            return false;
        }
        return true;
    }
}

/// <summary>
/// Intercepts NGame.StartNewSingleplayerRun to inject PendingRestartSeed.
/// NGame.DebugSeedOverride cannot be used here because NCharacterSelectScreen.BeginRun
/// overwrites it from its own settings (and clears it) before the run launches.
/// This is the final authority on the RunRngSet seed and the single point that
/// CONSUMES PendingRestartSeed; SeedInjectLobbyPatch below runs earlier so the
/// pre-run lobby rolls (act selection, random characters) use the same seed.
/// </summary>
[HarmonyPatch(typeof(NGame), nameof(NGame.StartNewSingleplayerRun))]
public static class SeedInjectPatch {
    public static void Prefix(ref string seed) {
        if (KitLibState.PendingRestartSeed == null) return;

        var canonicalized = SeedHelper.CanonicalizeSeed(KitLibState.PendingRestartSeed);
        KitLog.Info($"SeedInject: overriding seed '{seed}' → '{canonicalized}'.");
        seed = canonicalized;

        // Consumed — clear so a subsequent normal run is not affected.
        KitLibState.PendingRestartSeed = null;
    }
}

/// <summary>
/// Injects PendingRestartSeed one step EARLIER than SeedInjectPatch. The lobby's
/// BeginRunForAllPlayersIfAllReady computes the embark seed — reading
/// NGame.DebugSeedOverride FIRST — and BeginRunLocally then rolls ACT SELECTION and
/// random-character resolution from that seed's "act_selection" stream, all before
/// StartNewSingleplayerRun ever runs. Without this, a dev run's acts were rolled from
/// the session's throwaway random seed: the same injected seed landed in Overgrowth on
/// one run and Underdocks on the next (found 2026-08-06 chasing a bimodal fixed-seed
/// regression walk — identical map shape, two encounter universes). Going through
/// DebugSeedOverride also keeps multiplayer consistent: the host broadcasts the seed it
/// computed here. The override is restored in the Postfix — the method consumes it
/// synchronously — so an aborted embark can never leak the dev seed into a normal run.
/// </summary>
[HarmonyPatch(typeof(StartRunLobby), "BeginRunForAllPlayersIfAllReady")]
public static class SeedInjectLobbyPatch {
    public static void Prefix(out (bool Set, string? Prior) __state) {
        __state = (false, null);
        if (KitLibState.PendingRestartSeed == null || NGame.Instance == null)
            return;

        __state = (true, NGame.Instance.DebugSeedOverride);
        var canonicalized = SeedHelper.CanonicalizeSeed(KitLibState.PendingRestartSeed);
        NGame.Instance.DebugSeedOverride = canonicalized;
        KitLog.Info($"SeedInject(lobby): DebugSeedOverride → '{canonicalized}' so act selection rolls on the dev seed.");
    }

    public static void Postfix((bool Set, string? Prior) __state) {
        if (!__state.Set || NGame.Instance == null)
            return;
        NGame.Instance.DebugSeedOverride = __state.Prior;
    }
}
