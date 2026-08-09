using MegaCrit.Sts2.Core.Models;

namespace KitLib.Multiplayer.PseudoCoop;

/// <summary>
/// The modifiers a pending pseudo co-op launch asked for, handed to
/// <see cref="Patches.PseudoCoopRunModifiersPatch"/> at embark.
/// </summary>
/// <remarks>
/// They cannot travel with the lobby. <c>StartRunLobby.SetModifiers</c> exists, but standard multiplayer
/// embark ignores it - <c>NCharacterSelectScreen</c> passes <c>Array.Empty&lt;ModifierModel&gt;()</c> to
/// <c>NGame.StartNewMultiplayerRun</c>, because modifiers belong to the custom-run screen. Calling it would
/// also throw: the character-select listener's <c>ModifiersChanged()</c> is a stub that raises
/// <c>NotImplementedException</c>, exactly like <c>SeedChanged()</c>.
///
/// So the request is parked here and swapped in at the one call that consumes it. It is consumed once and
/// cleared, and the patch additionally requires a pseudo co-op launch to be pending, so a real multiplayer
/// game started later can never pick up a leftover.
/// </remarks>
internal static class PseudoCoopRunModifiers {
    static IReadOnlyList<ModifierModel>? _pending;

    /// <summary>Records what the next embark should use. Null or empty clears the request.</summary>
    public static void Request(IReadOnlyList<ModifierModel>? modifiers) =>
        _pending = modifiers is { Count: > 0 } ? modifiers : null;

    /// <summary>Returns the pending modifiers and forgets them, or null when nothing was requested.</summary>
    public static IReadOnlyList<ModifierModel>? Take() {
        var pending = _pending;
        _pending = null;
        return pending;
    }
}
