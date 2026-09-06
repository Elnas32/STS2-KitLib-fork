using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using Godot;
using KitLib.Host;
using KitLib.Multiplayer.PseudoCoop;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Nodes;
using MegaCrit.Sts2.Core.Nodes.Screens.CharacterSelect;
using MegaCrit.Sts2.Core.Nodes.Screens.MainMenu;

namespace KitLib.Mcp.Tools;

internal sealed class DevStartTestRunTool : IMcpTool {
    public string Name => "dev_start_test_run";
    public string Description =>
        "Start a new DevMode test run from the main menu (opens character select). Optional seed, run modifiers and ascension.";
    public string InputSchemaJson => """
    {
        "type": "object",
        "properties": {
            "seed": {
                "type": "string",
                "description": "Optional run seed override."
            },
            "modifiers": {
                "type": "array",
                "items": { "type": "string" },
                "description": "Optional run modifier ids to start the run with (e.g. VINTAGE, MIDAS, DRAFT, CURSED_RUN), case-insensitive. An unknown id fails and lists the valid ones."
            },
            "ascension": {
                "type": "integer",
                "description": "Optional ascension level (0 or more); omitted keeps the lobby's current setting."
            }
        },
        "required": []
    }
    """;

    public Task<JsonNode> ExecuteAsync(JsonObject args) {
        var mainMenu = NGame.Instance?.MainMenu;
        if (mainMenu == null || !GodotObject.IsInstanceValid(mainMenu)) {
            return Task.FromResult<JsonNode>(new JsonObject {
                ["ok"] = false,
                ["error"] = "Main menu is not active. Return to the title screen first.",
            });
        }

        if (args.TryGetPropertyValue("seed", out var seedNode)
            && seedNode?.GetValueKind() == JsonValueKind.String) {
            var seed = seedNode.GetValue<string>()?.Trim();
            KitLibState.PendingRestartSeed = string.IsNullOrEmpty(seed) ? null : seed;
        }
        else {
            KitLibState.PendingRestartSeed = null;
        }

        // Run modifiers: the custom-run screen's set, resolved by id. Mutable copies, as the custom-run screen
        // and the save loader hand over (CharacterCards writes its character onto the instance).
        var modifiers = new List<ModifierModel>();
        if (args.TryGetPropertyValue("modifiers", out var modsNode) && modsNode is JsonArray ids) {
            var known = ModelDb.GoodModifiers.Concat(ModelDb.BadModifiers).ToList();
            foreach (JsonNode? id in ids) {
                if (id?.GetValueKind() != JsonValueKind.String) continue;
                var wanted = id.GetValue<string>().Trim();
                var match = known.FirstOrDefault(m => string.Equals(m.Id.Entry, wanted, StringComparison.OrdinalIgnoreCase));
                if (match == null) {
                    return Task.FromResult<JsonNode>(DevCardMcpHelper.Fail(
                        $"Unknown modifier '{wanted}'. Valid: {string.Join(", ", known.Select(m => m.Id.Entry).OrderBy(x => x))}"));
                }
                modifiers.Add(match.ToMutable());
            }
        }
        PseudoCoopRunModifiers.Request(modifiers);

        int? ascension = null;
        if (args.TryGetPropertyValue("ascension", out var ascNode) && ascNode?.GetValueKind() == JsonValueKind.Number) {
            ascension = ascNode.GetValue<int>();
            if (ascension < 0)
                return Task.FromResult<JsonNode>(DevCardMcpHelper.Fail("ascension must be 0 or more."));
        }
        KitLibState.PendingRestartAscension = ascension;

        KitLibState.InDevRun = true;

        KitLibPanelOps.HideDevMainMenuIfVisible?.Invoke();

        var charSelect = mainMenu.SubmenuStack.GetSubmenuType<NCharacterSelectScreen>();
        charSelect.InitializeSingleplayer();
        mainMenu.SubmenuStack.Push(charSelect);

        return Task.FromResult<JsonNode>(new JsonObject {
            ["ok"] = true,
            ["status"] = "character_select",
            ["modifiers"] = new JsonArray(modifiers.Select(m => (JsonNode)m.Id.Entry).ToArray()),
            ["ascension"] = ascension,
        });
    }
}
