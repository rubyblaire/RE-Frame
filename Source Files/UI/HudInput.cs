using System.Numerics;
using Dalamud.Bindings.ImGui;

namespace REFrameXIV.UI;


internal static class HudInput
{
    public static bool HitTest(Vector2 localPosition, Vector2 size, out Vector2 min, out Vector2 max)
    {
        min = ImGui.GetWindowPos() + localPosition;
        max = min + size;
        var mouse = ImGui.GetMousePos();
        return mouse.X >= min.X && mouse.X < max.X && mouse.Y >= min.Y && mouse.Y < max.Y;
    }

    public static bool LeftClicked(bool hovered)
        => hovered && ImGui.IsMouseClicked(ImGuiMouseButton.Left);

    public static bool RightClicked(bool hovered)
        => hovered && ImGui.IsMouseClicked(ImGuiMouseButton.Right);

    public static bool LeftHeld(bool hovered)
        => hovered && ImGui.IsMouseDown(ImGuiMouseButton.Left);
}
