using System.Text.Json.Nodes;
using Godot;

namespace KitLib.Mcp.Tools;

/// <summary>
/// Where things are on screen, in the two coordinate spaces the bridge has to translate between.
/// </summary>
/// <remarks>
/// Every Control reports its rect in CANVAS coordinates — the game's design resolution, which is what
/// <c>dev_ui</c> and <c>dev_read_text</c> list and what a caller reasons about. The OS cursor and the
/// rendered frame live in WINDOW pixels, related by the root viewport's final (stretch) transform.
/// Mixing the two is silent: a click lands a scale factor away and hits nothing, so every conversion
/// goes through here and each response carries both, which is how a mismatch would show.
/// </remarks>
internal static class UiGeometry {
    public static JsonObject Rect(Control control) {
        var rect = control.GetGlobalRect();
        return new JsonObject {
            ["x"] = Round(rect.Position.X),
            ["y"] = Round(rect.Position.Y),
            ["w"] = Round(rect.Size.X),
            ["h"] = Round(rect.Size.Y),
        };
    }

    /// <summary>Canvas (design-resolution) coordinates to window pixels.</summary>
    public static Vector2 ToWindow(SceneTree tree, Vector2 canvas) => tree.Root.GetFinalTransform() * canvas;

    /// <summary>Window pixels to canvas (design-resolution) coordinates.</summary>
    public static Vector2 ToCanvas(SceneTree tree, Vector2 window) =>
        tree.Root.GetFinalTransform().AffineInverse() * window;

    public static JsonObject Point(Vector2 v) => new() { ["x"] = Round(v.X), ["y"] = Round(v.Y) };

    static float Round(float v) => Mathf.Round(v * 10f) / 10f;
}
