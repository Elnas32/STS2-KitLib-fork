using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using Godot;

namespace KitLib.Mcp.Tools;

/// <summary>
/// Drives the REAL cursor: moves it, presses and releases buttons, scrolls — through the engine's own
/// input pipeline, so everything a person's mouse reaches, this reaches, by geometry.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="DevHoverTool"/> and <see cref="DevUiTool"/> address a node and call its handler directly,
/// which is exact but skips the thing a layout bug lives in: WHERE the node is. A control that is drawn
/// under another one still answers a direct call. It does not answer a cursor. Nor does a direct call
/// satisfy code that polls the cursor's position — a panel that hides itself when the mouse leaves its
/// corridor closes on a direct hover no matter what the hover did.
/// </para>
/// <para>
/// Two things happen per move, and both are needed: the OS cursor is warped (what position-polling code
/// reads back) and an <c>InputEventMouseMotion</c> is parsed as if the OS had delivered it (what fires
/// <c>mouse_entered</c>/<c>mouse_exited</c> and <c>gui_input</c>, after Godot's own hit test). Godot
/// dispatches parsed input at the start of the next frame, so the tool waits a frame after each step
/// and reports which Control the engine then considers hovered — the self-check that the coordinates
/// landed.
/// </para>
/// <para>
/// Coordinates are CANVAS units, the same space <see cref="DevUiTool"/> and <see cref="DevReadTextTool"/>
/// report rects in; the conversion to window pixels is <see cref="UiGeometry"/>'s.
/// </para>
/// </remarks>
internal sealed class DevMouseTool : IMcpTool {
    public string Name => "dev_mouse";

    public string Description =>
        "Drive the REAL mouse through the engine's input pipeline: action move / click / press / release / "
        + "scroll at canvas coordinates (x, y — the units dev_ui and dev_read_text rects use) or at the "
        + "centre of a node (node_type + index, resolved like dev_hover). Fires mouse_entered/exited and "
        + "gui_input by geometry and moves the OS cursor, so cursor-polling panels behave as for a person. "
        + "Returns which Control the engine considers hovered afterwards. Use dev_hover / dev_ui when you "
        + "want to address a node directly; use this when WHERE it is drawn matters.";

    public string InputSchemaJson => """
    {
        "type": "object",
        "properties": {
            "action": {
                "type": "string",
                "description": "move, click, press, release, scroll, or where (report the cursor and hovered control without moving). Default move.",
                "default": "move"
            },
            "x": { "type": "number", "description": "Canvas X. Ignored when node_type is given." },
            "y": { "type": "number", "description": "Canvas Y. Ignored when node_type is given." },
            "node_type": {
                "type": "string",
                "description": "Target the centre of a visible node of this type name instead of x/y."
            },
            "index": {
                "type": "integer",
                "description": "Which node of that type, in tree order (visible only). Default 0.",
                "default": 0
            },
            "dx": { "type": "number", "description": "Offset from the node centre, canvas units. Default 0." },
            "dy": { "type": "number", "description": "Offset from the node centre, canvas units. Default 0." },
            "button": {
                "type": "string",
                "description": "left, right or middle for click/press/release. Default left.",
                "default": "left"
            },
            "amount": {
                "type": "integer",
                "description": "For scroll: wheel notches, positive = up, negative = down. Default 1.",
                "default": 1
            },
            "focus_window": {
                "type": "boolean",
                "description": "Bring the game window to the front before warping the OS cursor (default true). The warp is relative to the FOCUSED window, so without this the cursor lands in whatever app is in front.",
                "default": true
            }
        },
        "required": []
    }
    """;

    public async Task<JsonNode> ExecuteAsync(JsonObject args) {
        if (Engine.GetMainLoop() is not SceneTree tree)
            return DevCardMcpHelper.Fail("Scene tree unavailable.");

        var action = Str(args, "action")?.ToLowerInvariant() ?? "move";
        if (action == "where")
            return Report(tree, new JsonObject { ["ok"] = true, ["action"] = action });

        if (!TryResolveTarget(tree, args, action, out var canvas, out var targetInfo, out var error))
            return DevCardMcpHelper.Fail(error);

        var window = UiGeometry.ToWindow(tree, canvas);
        var button = ParseButton(Str(args, "button"));
        if (button == MouseButton.None)
            return DevCardMcpHelper.Fail("button must be left, right or middle.");

        if (!args.TryGetPropertyValue("focus_window", out var focusNode) || focusNode?.GetValueKind() != JsonValueKind.False)
            tree.Root.GrabFocus();

        // Always move first: a press without a preceding motion lands on whatever the engine last
        // thought was under the cursor, and a real mouse never presses without having moved there.
        var previous = UiGeometry.ToWindow(tree, tree.Root.GetMousePosition());
        Input.WarpMouse(window);
        Input.ParseInputEvent(new InputEventMouseMotion {
            Position = window,
            GlobalPosition = window,
            Relative = window - previous,
            ButtonMask = Input.GetMouseButtonMask(),
        });
        await tree.ToSignal(tree, SceneTree.SignalName.ProcessFrame);

        switch (action) {
            case "move":
                break;

            case "press":
                Input.ParseInputEvent(Button(button, true, window));
                await tree.ToSignal(tree, SceneTree.SignalName.ProcessFrame);
                break;

            case "release":
                Input.ParseInputEvent(Button(button, false, window));
                await tree.ToSignal(tree, SceneTree.SignalName.ProcessFrame);
                break;

            case "click":
                // Press and release on separate frames: that is the shape a real click has, and a handler
                // that arms on press and completes on release sees both edges with a frame between.
                Input.ParseInputEvent(Button(button, true, window));
                await tree.ToSignal(tree, SceneTree.SignalName.ProcessFrame);
                Input.ParseInputEvent(Button(button, false, window));
                await tree.ToSignal(tree, SceneTree.SignalName.ProcessFrame);
                break;

            case "scroll": {
                var amount = Int(args, "amount", 1);
                var wheel = amount >= 0 ? MouseButton.WheelUp : MouseButton.WheelDown;
                for (var i = 0; i < System.Math.Abs(amount); i++) {
                    Input.ParseInputEvent(Button(wheel, true, window));
                    Input.ParseInputEvent(Button(wheel, false, window));
                }
                await tree.ToSignal(tree, SceneTree.SignalName.ProcessFrame);
                break;
            }

            default:
                return DevCardMcpHelper.Fail($"Unknown action '{action}'. Use move, click, press, release, scroll, or where.");
        }

        var response = new JsonObject {
            ["ok"] = true,
            ["action"] = action,
            ["target"] = targetInfo,
            ["canvas"] = UiGeometry.Point(canvas),
            ["window"] = UiGeometry.Point(window),
        };
        return Report(tree, response);
    }

    /// <summary>Where the engine thinks the cursor is and what it is over — the proof a move landed.</summary>
    static JsonObject Report(SceneTree tree, JsonObject response) {
        response["cursor"] = UiGeometry.Point(tree.Root.GetMousePosition());
        var hovered = tree.Root.GuiGetHoveredControl();
        response["hovered"] = hovered == null
            ? null
            : new JsonObject {
                ["type"] = hovered.GetType().Name,
                ["name"] = hovered.Name.ToString(),
                ["path"] = hovered.GetPath().ToString(),
                ["rect"] = UiGeometry.Rect(hovered),
            };
        return response;
    }

    static bool TryResolveTarget(
        SceneTree tree, JsonObject args, string action,
        out Vector2 canvas, out JsonObject? info, out string error) {
        canvas = default;
        info = null;
        error = string.Empty;

        var nodeType = Str(args, "node_type");
        if (!string.IsNullOrWhiteSpace(nodeType)) {
            var found = new List<Control>();
            CollectByTypeName(tree.Root, nodeType.Trim(), found);
            var index = Int(args, "index", 0);
            if (index < 0 || index >= found.Count) {
                error = $"index {index} out of range — {found.Count} visible '{nodeType}' on screen.";
                return false;
            }

            var control = found[index];
            var rect = control.GetGlobalRect();
            canvas = rect.GetCenter() + new Vector2(Num(args, "dx", 0f), Num(args, "dy", 0f));
            info = new JsonObject {
                ["type"] = control.GetType().Name,
                ["name"] = control.Name.ToString(),
                ["rect"] = UiGeometry.Rect(control),
            };
            return true;
        }

        var hasX = args.TryGetPropertyValue("x", out var xNode) && xNode?.GetValueKind() == JsonValueKind.Number;
        var hasY = args.TryGetPropertyValue("y", out var yNode) && yNode?.GetValueKind() == JsonValueKind.Number;
        if (hasX && hasY) {
            canvas = new Vector2(xNode!.GetValue<float>(), yNode!.GetValue<float>());
            return true;
        }

        // A press/release/scroll with no target acts where the cursor already is; a move needs one.
        if (action != "move") {
            canvas = tree.Root.GetMousePosition();
            return true;
        }

        error = "Give x and y (canvas units) or node_type (+ index).";
        return false;
    }

    static InputEventMouseButton Button(MouseButton button, bool pressed, Vector2 window) => new() {
        ButtonIndex = button,
        Pressed = pressed,
        Position = window,
        GlobalPosition = window,
        ButtonMask = pressed ? ToMask(button) : 0,
    };

    static MouseButtonMask ToMask(MouseButton button) => button switch {
        MouseButton.Left => MouseButtonMask.Left,
        MouseButton.Right => MouseButtonMask.Right,
        MouseButton.Middle => MouseButtonMask.Middle,
        _ => 0,
    };

    static MouseButton ParseButton(string? raw) => (raw ?? "left").Trim().ToLowerInvariant() switch {
        "left" or "" => MouseButton.Left,
        "right" => MouseButton.Right,
        "middle" => MouseButton.Middle,
        _ => MouseButton.None,
    };

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

    static float Num(JsonObject args, string key, float fallback) =>
        args.TryGetPropertyValue(key, out var node) && node?.GetValueKind() == JsonValueKind.Number
            ? node.GetValue<float>()
            : fallback;
}
