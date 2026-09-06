using System.Collections.Generic;
using HarmonyLib;
using KitLib.Multiplayer.PseudoCoop;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Nodes;

namespace KitLib.Mcp.Patches;

/// <summary>
/// Applies the run modifiers and ascension a <c>dev_start_test_run</c> asked for to the single-player embark.
/// </summary>
/// <remarks>
/// The character-select embark passes <c>Array.Empty&lt;ModifierModel&gt;()</c> and the lobby's ascension, and
/// neither can be set through the lobby (its listener stubs throw, see <see cref="PseudoCoopRunModifiers"/>), so
/// both are swapped into <c>NGame.StartNewSingleplayerRun</c> itself - the prefix runs before the async state
/// machine captures its arguments. Guarded on a dev run being in progress and consumed once, so a normal run
/// started later can never pick a leftover up. The modifier store is shared with the pseudo co-op launch; that
/// patch sits on the multiplayer embark and guards on its own launch flag, so the two never take each other's
/// request.
/// </remarks>
[HarmonyPatch(typeof(NGame), nameof(NGame.StartNewSingleplayerRun))]
internal static class DevTestRunLaunchPatch {
    [HarmonyPrefix]
    static void Prefix(ref IReadOnlyList<ModifierModel> modifiers, ref int ascensionLevel) {
        if (!KitLibState.InDevRun) return;

        var requested = PseudoCoopRunModifiers.Take();
        if (requested != null) {
            modifiers = requested;
            KitLog.Info("Mcp", $"Applied {requested.Count} run modifier(s) to the dev test run.");
        }

        if (KitLibState.PendingRestartAscension is { } ascension) {
            ascensionLevel = ascension;
            KitLibState.PendingRestartAscension = null;
            KitLog.Info("Mcp", $"Applied ascension {ascension} to the dev test run.");
        }
    }
}
