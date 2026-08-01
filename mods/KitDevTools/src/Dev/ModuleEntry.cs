using KitLib;
using KitLib.Abstractions.Host;
using KitLib.EnemyIntent;
using KitLib.Host;
using KitLib.Interop;
using KitLib.Multiplayer.PseudoCoop;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Runs;

namespace KitLib.Dev;

public static class ModuleEntry {
    public static void Initialize() {
        if (KitLibHost.IsModuleLoaded(KitLibModuleIds.Dev))
            return;

        KitLibHost.AnnounceModule(KitLibModuleIds.Dev);
        KitLibPanelUiOps.QueryModHarmonyPatchStats = ModHarmonyOwnerMatcher.TryGetStats;
        KitLibPanelUiOps.BuildModHarmonyDetailReport = ModHarmonyOwnerMatcher.BuildDetailReport;
        KitLibPseudoCoopOps.RefreshUiAfterPlayerJoined = state => {
            if (state is RunState run)
                PseudoCoopMultiplayerUiRefresh.TryRefreshAfterPlayerJoined(run);
        };
        // The pseudo co-op lobby host binds an ENet port. A run that ends without releasing it
        // leaves the port bound for the life of the process, so every later host start fails with
        // CantCreate. Core fires this on run end (MpCheatLifecyclePatch); KitAI owns the sibling
        // KitLibNetPlayOps.OnRunEnded, so the lobby host takes the SyncBot one.
        KitLibSyncBotOps.OnRunEnded += PseudoCoopLobbyHost.ReleaseHost;
        WireEnemyIntentHost();
    }

    static void WireEnemyIntentHost() {
        KitLibHost.IsMonsterIntentOverlayReady = state =>
            MonsterIntentReader.IsOverlayCombatReady((CombatState?)state);
        KitLibHost.CaptureMonsterIntentCurrent = state =>
            MonsterIntentReader.CaptureCurrent((CombatState?)state);
        KitLibHost.CaptureMonsterIntentNextTurn = state =>
            MonsterIntentReader.CaptureNextTurn((CombatState?)state);
    }
}
