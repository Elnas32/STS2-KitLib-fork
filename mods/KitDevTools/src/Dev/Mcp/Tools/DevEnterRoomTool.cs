using System.Text.Json;
using System.Text.Json.Nodes;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Events;
using MegaCrit.Sts2.Core.Rooms;
using MegaCrit.Sts2.Core.Runs;

namespace KitLib.Mcp.Tools;

/// <summary>
/// Enters a specific room — most usefully a named EVENT — without walking the map to find one.
/// </summary>
/// <remarks>
/// <para>
/// Written because a UI bug reported against the Crystal Sphere event could not be checked on the surface
/// it was reported on: the event has to come up in a run, nothing could force it, and the fix ended up
/// verified on two other screens that shared the mechanism. Confirming a fix somewhere adjacent is not
/// confirming it where it was seen.
/// </para>
/// <para>
/// Wraps the game's own <c>RunManager.EnterRoomDebug</c>, which is documented "should only be used in tests
/// or dev commands". Handing it an <c>EventModel</c> makes it pick <c>RoomType.Event</c> and the matching
/// map-point type by itself, so the room is built exactly as the real flow builds it. It DOES append to the
/// map-point history, so the floor advances — this enters a real room, it does not preview one.
/// </para>
/// </remarks>
internal sealed class DevEnterRoomTool : IMcpTool {
    public string Name => "dev_enter_room";

    public string Description =>
        "Enter a room directly, by event id (e.g. CRYSTAL_SPHERE) or by room type, instead of walking the "
        + "map to find one. Pass 'search' to list matching event ids. Enters a REAL room: the floor advances.";

    public string InputSchemaJson => """
    {
        "type": "object",
        "properties": {
            "event_id": {
                "type": "string",
                "description": "Event model id to enter, e.g. CRYSTAL_SPHERE. Picks RoomType.Event automatically."
            },
            "room": {
                "type": "string",
                "description": "Room type when no event is named: Monster, Elite, Boss, Treasure, Shop, RestSite, Event."
            },
            "search": {
                "type": "string",
                "description": "List event ids containing this text instead of entering anything. Empty string lists all."
            }
        },
        "required": []
    }
    """;

    public async Task<JsonNode> ExecuteAsync(JsonObject args) {
        if (ReadString(args, "search") is { } search) {
            return ListEvents(search);
        }

        var manager = RunManager.Instance;
        if (manager?.DebugOnlyGetState() == null) {
            return Fail("No run is active. Start or load a run first.");
        }

        if (ReadString(args, "event_id") is { } eventId) {
            if (FindEvent(eventId) is not { } model) {
                return Fail($"No event model matches '{eventId}'. Use search to list ids.");
            }

            await manager.EnterRoomDebug(RoomType.Event, model: model);
            return new JsonObject {
                ["ok"] = true,
                ["entered"] = "event",
                ["eventId"] = model.Id.Entry,
            };
        }

        if (ReadString(args, "room") is not { } roomName) {
            return Fail("Nothing to do — pass event_id, room, or search.");
        }

        if (!Enum.TryParse(roomName, ignoreCase: true, out RoomType roomType)) {
            return Fail($"Unknown room type '{roomName}'. Try Monster, Elite, Boss, Treasure, Shop, RestSite, Event.");
        }

        await manager.EnterRoomDebug(roomType);
        return new JsonObject {
            ["ok"] = true,
            ["entered"] = roomType.ToString(),
        };
    }

    // Exact id first, then a contains-match, so a full id can never be shadowed by a longer one.
    static EventModel? FindEvent(string id) {
        var wanted = id.Trim();
        EventModel? loose = null;
        foreach (var model in ModelDb.All) {
            if (model is not EventModel ev)
                continue;
            if (string.Equals(ev.Id.Entry, wanted, StringComparison.OrdinalIgnoreCase))
                return ev;
            if (loose == null && ev.Id.Entry.Contains(wanted, StringComparison.OrdinalIgnoreCase))
                loose = ev;
        }

        return loose;
    }

    static JsonNode ListEvents(string filter) {
        var ids = new JsonArray();
        foreach (var model in ModelDb.All) {
            if (model is EventModel ev
                && (filter.Length == 0 || ev.Id.Entry.Contains(filter, StringComparison.OrdinalIgnoreCase))) {
                ids.Add(ev.Id.Entry);
            }
        }

        return new JsonObject { ["ok"] = true, ["count"] = ids.Count, ["events"] = ids };
    }

    static string? ReadString(JsonObject args, string key) =>
        args.TryGetPropertyValue(key, out var node) && node?.GetValueKind() == JsonValueKind.String
            ? node.GetValue<string>()
            : null;

    static JsonObject Fail(string error) => new() { ["ok"] = false, ["error"] = error };
}
