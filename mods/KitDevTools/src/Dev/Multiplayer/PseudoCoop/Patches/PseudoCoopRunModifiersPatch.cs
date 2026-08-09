using HarmonyLib;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Nodes;

namespace KitLib.Multiplayer.PseudoCoop.Patches;

/// <summary>Applies a pseudo co-op launch's requested modifiers, which multiplayer embark otherwise drops.</summary>
/// <remarks>
/// <c>NCharacterSelectScreen</c> embarks with <c>Array.Empty&lt;ModifierModel&gt;()</c> - standard
/// multiplayer has no modifier UI - so the only place a requested set can be applied is the call itself.
/// The prefix runs before the async state machine captures its arguments, so swapping the parameter here is
/// what the run is actually created with.
///
/// Two guards keep this to the launch that asked for it: the request is taken once and cleared, and a
/// pseudo co-op launch must be pending, which <see cref="PseudoCoopLobbyHost"/> sets immediately before
/// readying the lobby.
/// </remarks>
[HarmonyPatch(typeof(NGame), nameof(NGame.StartNewMultiplayerRun))]
internal static class PseudoCoopRunModifiersPatch {
    [HarmonyPrefix]
    static void Prefix(ref IReadOnlyList<ModifierModel> modifiers) {
        if (!KitLibState.PseudoCoopLaunchPending) return;

        var requested = PseudoCoopRunModifiers.Take();
        if (requested == null) return;

        modifiers = requested;
        KitLog.Info("PseudoCoop", $"Applied {requested.Count} run modifier(s) to the multiplayer embark.");
    }
}
