using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using Godot;

namespace KitLib.Mcp.Tools;

/// <summary>
/// Saves what is on screen to a PNG, so a GEOMETRY claim — overlap, wrap, clamping, a tile drawn
/// under the card it describes — can be checked by looking, not inferred.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="DevReadTextTool"/> answers "what does it say" and the rects on <see cref="DevUiTool"/>
/// answer "where is it", but a layout bug is usually two things drawn in the same place, and only the
/// rendered frame shows that. Until this existed the only way was a desktop screen grab of the game
/// window from outside the process, which needs the window focused and unobscured.
/// </para>
/// <para>
/// The frame is read from the root viewport's texture after the next <c>frame_post_draw</c>, so it
/// reflects every hover and click the bridge sent before this call. Coordinates in the response are
/// WINDOW pixels; the rects the other tools report are canvas units — the response carries the scale.
/// </para>
/// </remarks>
internal sealed class DevScreenshotTool : IMcpTool {
    public string Name => "dev_screenshot";

    public string Description =>
        "Save a PNG of the current game frame and return its path. Optionally crop to one node "
        + "(node_type + index, as dev_hover resolves them) with padding. Use it to check layout claims "
        + "(overlap, wrapping, clamping) that dev_read_text cannot express. Rects from dev_ui / dev_read_text "
        + "are canvas units; multiply by the returned scale to get pixel positions in the image.";

    public string InputSchemaJson => """
    {
        "type": "object",
        "properties": {
            "path": {
                "type": "string",
                "description": "Where to write the PNG. Default: a timestamped file under the system temp folder (kitlib-screens/)."
            },
            "node_type": {
                "type": "string",
                "description": "Crop to a node of this type name (e.g. NRelicInventoryHolder, PanelContainer)."
            },
            "index": {
                "type": "integer",
                "description": "Which node of that type, in tree order (visible only). Default 0.",
                "default": 0
            },
            "padding": {
                "type": "integer",
                "description": "Extra canvas units around the cropped node. Default 40.",
                "default": 40
            },
            "max_width": {
                "type": "integer",
                "description": "Downscale so the image is at most this wide (0 = keep full size). Default 0.",
                "default": 0
            }
        },
        "required": []
    }
    """;

    public async Task<JsonNode> ExecuteAsync(JsonObject args) {
        if (Engine.GetMainLoop() is not SceneTree tree)
            return DevCardMcpHelper.Fail("Scene tree unavailable.");

        // The texture holds the LAST drawn frame; wait for one more so anything a previous call changed
        // (a hover, a click, a panel that opened) has actually been rendered before it is read back.
        await tree.ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);

        var image = tree.Root.GetTexture().GetImage();
        if (image == null || image.IsEmpty())
            return DevCardMcpHelper.Fail("Viewport texture is empty.");

        var scale = tree.Root.GetFinalTransform().Scale;
        var response = new JsonObject {
            ["ok"] = true,
            ["frame"] = new JsonObject { ["w"] = image.GetWidth(), ["h"] = image.GetHeight() },
            ["canvas"] = UiGeometry.Point(tree.Root.GetVisibleRect().Size),
            ["scale"] = UiGeometry.Point(scale),
        };

        var nodeType = Str(args, "node_type");
        if (!string.IsNullOrWhiteSpace(nodeType)) {
            var found = new List<Control>();
            CollectByTypeName(tree.Root, nodeType.Trim(), found);
            var index = Int(args, "index", 0);
            if (index < 0 || index >= found.Count)
                return DevCardMcpHelper.Fail($"index {index} out of range — {found.Count} visible '{nodeType}' on screen.");

            var control = found[index];
            var pad = Int(args, "padding", 40);
            var canvasRect = control.GetGlobalRect().Grow(pad);
            var topLeft = UiGeometry.ToWindow(tree, canvasRect.Position);
            var bottomRight = UiGeometry.ToWindow(tree, canvasRect.End);
            var region = new Rect2I(
                (int)Mathf.Floor(topLeft.X), (int)Mathf.Floor(topLeft.Y),
                (int)Mathf.Ceil(bottomRight.X - topLeft.X), (int)Mathf.Ceil(bottomRight.Y - topLeft.Y))
                .Intersection(new Rect2I(0, 0, image.GetWidth(), image.GetHeight()));
            if (region.Size.X <= 0 || region.Size.Y <= 0)
                return DevCardMcpHelper.Fail($"{nodeType}[{index}] lies entirely off screen.");

            image = image.GetRegion(region);
            response["crop"] = new JsonObject {
                ["node"] = new JsonObject { ["type"] = control.GetType().Name, ["name"] = control.Name.ToString() },
                ["rect"] = UiGeometry.Rect(control),
                ["region"] = new JsonObject { ["x"] = region.Position.X, ["y"] = region.Position.Y, ["w"] = region.Size.X, ["h"] = region.Size.Y },
            };
        }

        var maxWidth = Int(args, "max_width", 0);
        if (maxWidth > 0 && image.GetWidth() > maxWidth) {
            var factor = (float)maxWidth / image.GetWidth();
            image.Resize(maxWidth, (int)(image.GetHeight() * factor), Image.Interpolation.Lanczos);
            response["resized"] = new JsonObject { ["w"] = image.GetWidth(), ["h"] = image.GetHeight(), ["factor"] = factor };
        }

        var path = Str(args, "path");
        if (string.IsNullOrWhiteSpace(path)) {
            var dir = Path.Combine(Path.GetTempPath(), "kitlib-screens");
            Directory.CreateDirectory(dir);
            path = Path.Combine(dir, $"shot-{System.DateTime.Now:yyyyMMdd-HHmmss-fff}.png");
        }
        else {
            var dir = Path.GetDirectoryName(Path.GetFullPath(path));
            if (!string.IsNullOrEmpty(dir))
                Directory.CreateDirectory(dir);
        }

        var err = image.SavePng(path);
        if (err != Error.Ok)
            return DevCardMcpHelper.Fail($"SavePng failed: {err}");

        response["path"] = Path.GetFullPath(path);
        response["image"] = new JsonObject { ["w"] = image.GetWidth(), ["h"] = image.GetHeight() };
        return response;
    }

    static void CollectByTypeName(Node node, string typeName, List<Control> found) {
        if (node is CanvasItem { Visible: false })
            return;

        if (node is Control control
            && string.Equals(node.GetType().Name, typeName, System.StringComparison.OrdinalIgnoreCase))
            found.Add(control);

        foreach (var child in node.GetChildren())
            CollectByTypeName(child, typeName, found);
    }

    static string? Str(JsonObject args, string key) =>
        args.TryGetPropertyValue(key, out var node) && node?.GetValueKind() == JsonValueKind.String
            ? node.GetValue<string>()
            : null;

    static int Int(JsonObject args, string key, int fallback) =>
        args.TryGetPropertyValue(key, out var node) && node?.GetValueKind() == JsonValueKind.Number
            ? node.GetValue<int>()
            : fallback;
}
