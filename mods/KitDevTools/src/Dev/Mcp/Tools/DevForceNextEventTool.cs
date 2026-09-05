using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using HarmonyLib;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Rooms;
using MegaCrit.Sts2.Core.Runs;

namespace KitLib.Mcp.Tools;

/// <summary>
/// Makes a chosen event the NEXT one the act's event ring hands out.
/// </summary>
/// <remarks>
/// <para>
/// An act's events are a shuffled ring (<c>RoomSet.events</c>) walked by a pointer (<c>eventsVisited</c>);
/// every "?" that resolves to Event and every Event node takes <c>NextEvent</c>, after
/// <c>EnsureNextEventIsValid</c> skips entries that are disallowed or already visited. Reaching a SPECIFIC
/// event — to test what it grants, or what a mod predicts for it — was therefore a seed hunt. This moves
/// the pointer forward to the entry holding the event, leaving the ring's order untouched, so the game's
/// own pull then hands it out.
/// </para>
/// <para>
/// The pointer only ever moves FORWARD (a ring index behind the current one is reached by going round),
/// and the result says whether the game will actually keep it: an event that fails <c>IsAllowed</c> or is
/// already in <c>VisitedEventIds</c> is skipped by the validity walk at the next pull, and the response
/// carries both flags so a caller is not left guessing why something else appeared.
/// </para>
/// </remarks>
internal sealed class DevForceNextEventTool : IMcpTool {
    static readonly FieldInfo? RoomsField = AccessTools.Field(typeof(ActModel), "_rooms");

    public string Name => "dev_force_next_event";

    public string Description =>
        "Make a chosen event the next one the current act hands out (moves the act's event-ring pointer "
        + "forward to it). Pass 'list' true to see the ring, its pointer and each entry's allowed/visited "
        + "state instead. Use with a run active; the event must be in this act's ring.";

    public string InputSchemaJson => """
    {
        "type": "object",
        "properties": {
            "event_id": {
                "type": "string",
                "description": "Event model id, e.g. TRASH_HEAP, WELCOME_TO_WONGOS, TABLET_OF_TRUTH (case-insensitive)."
            },
            "list": {
                "type": "boolean",
                "description": "List the act's event ring (index, id, allowed, visited) and the current pointer instead of forcing."
            }
        }
    }
    """;

    public Task<JsonNode> ExecuteAsync(JsonObject args) {
        var state = RunManager.Instance?.DebugOnlyGetState();
        if (state == null)
            return Task.FromResult<JsonNode>(DevCardMcpHelper.Fail("No run is active. Start or load a run first."));

        if (RoomsField?.GetValue(state.Act) is not RoomSet rooms)
            return Task.FromResult<JsonNode>(DevCardMcpHelper.Fail("Could not reach the act's room set (ActModel._rooms) — renamed by a game update?"));

        if (rooms.events.Count == 0)
            return Task.FromResult<JsonNode>(DevCardMcpHelper.Fail("This act has no event ring yet."));

        if (args.TryGetPropertyValue("list", out var listNode) && listNode?.GetValueKind() == JsonValueKind.True)
            return Task.FromResult<JsonNode>(Report(rooms, state, "listed"));

        if (!args.TryGetPropertyValue("event_id", out var idNode) || idNode?.GetValueKind() != JsonValueKind.String
            || string.IsNullOrWhiteSpace(idNode.GetValue<string>())) {
            return Task.FromResult<JsonNode>(DevCardMcpHelper.Fail("Missing or invalid event_id (pass list=true to see the ring)."));
        }

        string wanted = idNode.GetValue<string>()!.Trim();
        int index = rooms.events.FindIndex(e => string.Equals(e.Id.Entry, wanted, StringComparison.OrdinalIgnoreCase));
        if (index < 0)
            return Task.FromResult<JsonNode>(DevCardMcpHelper.Fail($"{wanted} is not in this act's event ring (pass list=true to see it)."));

        // Smallest pointer value at or past the current one whose ring position is the wanted entry.
        int count = rooms.events.Count;
        int current = rooms.eventsVisited;
        int target = current + ((index - current % count) % count + count) % count;
        rooms.eventsVisited = target;

        return Task.FromResult<JsonNode>(Report(rooms, state, $"pointer {current} -> {target}"));
    }

    private static JsonNode Report(RoomSet rooms, RunState state, string applied) {
        var ring = new JsonArray();
        for (int i = 0; i < rooms.events.Count; i++) {
            EventModel e = rooms.events[i];
            ring.Add(new JsonObject {
                ["index"] = i,
                ["id"] = e.Id.Entry,
                ["allowed"] = SafeAllowed(e, state),
                ["visited"] = state.VisitedEventIds.Contains(e.Id),
            });
        }

        EventModel next = rooms.NextEvent;
        return new JsonObject {
            ["ok"] = true,
            ["applied"] = applied,
            ["pointer"] = rooms.eventsVisited,
            ["next"] = next.Id.Entry,
            ["nextAllowed"] = SafeAllowed(next, state),
            ["nextVisited"] = state.VisitedEventIds.Contains(next.Id),
            ["ring"] = ring,
        };
    }

    // IsAllowed is game code that can throw on an odd state; a listing must not.
    private static bool SafeAllowed(EventModel e, RunState state) {
        try { return e.IsAllowed(state); } catch { return false; }
    }
}
