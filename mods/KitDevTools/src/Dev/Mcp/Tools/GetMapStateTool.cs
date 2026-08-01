using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using MegaCrit.Sts2.Core.Map;
using MegaCrit.Sts2.Core.Runs;

namespace KitLib.Mcp.Tools;

/// <summary>
/// Returns the WHOLE act map — every node, its type and its edges — so a route can be planned.
/// </summary>
/// <remarks>
/// <para>
/// <c>get_game_state</c> only ever exposes <c>mapNodes</c>, and that list is
/// <c>GetAvailableMapPoints</c>: the children of the last visited node, i.e. the one row you could
/// step onto next. Nothing revealed what lay beyond it, so an agent could not see where the rest
/// sites, elites, shops or dead ends were, and every map decision was a blind local pick.
/// </para>
/// <para>
/// Read from <see cref="RunState.Map"/> rather than from <c>NMapScreen</c>'s nodes, so it answers
/// with the map screen closed — mid-combat, in an event, anywhere. The screen only has nodes while
/// it is instantiated, which is exactly when an agent least needs to plan.
/// </para>
/// </remarks>
internal sealed class GetMapStateTool : IMcpTool {
    public string Name => "get_map_state";

    public string Description =>
        "Full act map for route planning: every node with row/col, type and children (edges), plus "
        + "visited path, current position, the nodes reachable next, and the boss. Works with the map screen closed.";

    public string InputSchemaJson => """
    {
        "type": "object",
        "properties": {},
        "required": []
    }
    """;

    public Task<JsonNode> ExecuteAsync(JsonObject args) => Task.FromResult(Run());

    static JsonNode Run() {
        var state = RunManager.Instance?.DebugOnlyGetState();
        if (state == null)
            return new JsonObject { ["error"] = "No active run." };

        var map = state.Map;
        var points = map?.GetAllMapPoints()?.ToList();
        if (map == null || points == null || points.Count == 0)
            return new JsonObject { ["error"] = "No map for the current act." };

        var visited = state.VisitedMapCoords ?? [];
        var visitedSet = new HashSet<MapCoord>(visited);
        var reachable = new HashSet<MapCoord>(NextReachable(state, points).Select(p => p.coord));

        var nodes = new JsonArray();
        foreach (var point in points.OrderBy(p => p.coord.row).ThenBy(p => p.coord.col)) {
            var children = new JsonArray();
            foreach (var child in point.Children ?? [])
                children.Add(Coord(child.coord));

            nodes.Add(new JsonObject {
                ["row"] = point.coord.row,
                ["col"] = point.coord.col,
                ["pointType"] = point.PointType.ToString(),
                ["isCombat"] = IsCombat(point.PointType),
                ["visited"] = visitedSet.Contains(point.coord),
                ["reachable"] = reachable.Contains(point.coord),
                ["children"] = children,
            });
        }

        var visitedArr = new JsonArray();
        foreach (var coord in visited)
            visitedArr.Add(Coord(coord));

        return new JsonObject {
            ["ok"] = true,
            ["actIndex"] = state.CurrentActIndex,
            ["rows"] = map.GetRowCount(),
            ["cols"] = map.GetColumnCount(),
            ["currentCoord"] = state.CurrentMapCoord is { } current ? Coord(current) : null,
            ["boss"] = map.BossMapPoint != null ? Coord(map.BossMapPoint.coord) : null,
            ["secondBoss"] = map.SecondBossMapPoint != null ? Coord(map.SecondBossMapPoint.coord) : null,
            ["visitedPath"] = visitedArr,
            ["nodeCount"] = nodes.Count,
            ["nodes"] = nodes,
        };
    }

    /// <summary>
    /// The nodes that may be stepped onto next. Mirrors the AI snapshot's own rule: before anything
    /// is visited that is row 0, afterwards the children of the last visited point.
    /// </summary>
    static List<MapPoint> NextReachable(RunState state, List<MapPoint> points) {
        var visited = state.VisitedMapCoords;
        if (visited == null || visited.Count == 0)
            return points.Where(p => p.coord.row == 0).ToList();

        var last = visited[visited.Count - 1];
        var lastPoint = points.FirstOrDefault(p => p.coord.Equals(last));
        if (lastPoint?.Children == null)
            return [];

        var childCoords = new HashSet<MapCoord>(lastPoint.Children.Select(c => c.coord));
        return points.Where(p => childCoords.Contains(p.coord)).ToList();
    }

    static bool IsCombat(MapPointType type) =>
        type is MapPointType.Monster or MapPointType.Elite or MapPointType.Boss;

    static JsonObject Coord(MapCoord coord) => new() {
        ["row"] = coord.row,
        ["col"] = coord.col,
    };
}
