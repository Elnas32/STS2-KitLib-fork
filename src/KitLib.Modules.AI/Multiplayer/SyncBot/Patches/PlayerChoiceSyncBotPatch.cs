using HarmonyLib;
using KitLib.Multiplayer.PseudoCoop;
using KitLib.Settings;
using KitLib.Singleplayer.Companion;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Runs;

namespace KitLib.Multiplayer.SyncBot.Patches;

[HarmonyPatch(typeof(PlayerChoiceSynchronizer), nameof(PlayerChoiceSynchronizer.WaitForRemoteChoice))]
internal static class PlayerChoiceSyncBotPatch {
    [HarmonyPrefix]
    static void Prefix(Player player, uint choiceId) {
        var isSpCompanion = SpvCompanionChoiceRouting.ShouldAutoChoose(player);
        if (!MpCheatSyncBot.ShouldSimulatePlayer(player) && !isSpCompanion) return;

        var sync = RunManager.Instance?.PlayerChoiceSynchronizer;
        if (sync == null) return;

        // Peek before deciding -- MpChoiceBot.Decide CONSUMES the pending entry to read its options.
        MpPendingPlayerChoice.TryPeekAction(player.NetId, choiceId, out var pausedAction);

        var choice = MpCheatSyncBot.ShouldSimulatePlayer(player) && !isSpCompanion
            ? (AiSessionSettings.MpAiTeammateEnabled
                ? MpChoiceBot.Decide(player, choiceId)
                : MpCheatSyncBot.DefaultIndexChoice())
            : MpChoiceBot.Decide(player, choiceId);

        sync.ReceiveReplayChoice(player, choiceId, choice);
        KitLog.Debug("SyncBot", $"Pre-filled choice id={choiceId} for player {player.NetId} type={choice.type}.");

        // Answering the choice is only half of what a real client does. The action then awaits
        // GameAction.WaitForActionToResumeExecutingAfterPlayerChoice(), which completes ONLY via
        // ActionQueueSynchronizer.RequestResumeActionAfterPlayerChoice -- normally sent by the machine
        // that owns the player. A simulated peer has no machine, so the action sat in
        // GatheringPlayerChoice forever: that peer's queue never drained, it played no further cards and
        // never ended its turn, and combat stalled with the choice UI still on screen. Reproduced with
        // Silent's Survivor, whose play requires an upgrade choice.
        if (pausedAction == null) return;

        var queues = RunManager.Instance?.ActionQueueSynchronizer;
        if (queues == null) return;

        queues.RequestResumeActionAfterPlayerChoice(pausedAction);
        KitLog.Debug("SyncBot", $"Requested resume of {pausedAction} for player {player.NetId} after choice id={choiceId}.");
    }
}
