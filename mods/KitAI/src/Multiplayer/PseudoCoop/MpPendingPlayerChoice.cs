using System.Collections.Generic;
using MegaCrit.Sts2.Core.Entities.Multiplayer;
using MegaCrit.Sts2.Core.GameActions;

namespace KitLib.Multiplayer.PseudoCoop;

/// <summary>Tracks expected remote choice shape between ReserveChoiceId and WaitForRemoteChoice.</summary>
internal static class MpPendingPlayerChoice {
    readonly record struct Pending(PlayerChoiceOptions Options, GameAction? Action);

    static readonly Dictionary<(ulong NetId, uint ChoiceId), Pending> _pending = [];

    internal static void Register(ulong netId, uint choiceId, PlayerChoiceOptions options, GameAction? action = null) {
        if (netId == 0) return;
        _pending[(netId, choiceId)] = new Pending(options, action);
    }

    internal static bool TryConsume(ulong netId, uint choiceId, out PlayerChoiceOptions options) {
        options = PlayerChoiceOptions.None;
        if (!_pending.Remove((netId, choiceId), out var pending))
            return false;

        options = pending.Options;
        return true;
    }

    /// <summary>
    /// Reads the paused action WITHOUT consuming the entry, so <see cref="MpChoiceBot"/> can still
    /// consume it for its own decision. Returns false when no action was recorded.
    /// </summary>
    internal static bool TryPeekAction(ulong netId, uint choiceId, out GameAction? action) {
        action = null;
        if (!_pending.TryGetValue((netId, choiceId), out var pending))
            return false;

        action = pending.Action;
        return action != null;
    }

    internal static void Clear() => _pending.Clear();
}
