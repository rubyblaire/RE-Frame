using System;
using System.Numerics;
using Dalamud.Bindings.ImGui;

namespace REFrameXIV.UI;


public readonly record struct HudCanvasInfo(Vector2 Origin, Vector2 Size)
{
    public Vector2 Max => Origin + Size;
}

public static class HudCanvas
{
    public static HudCanvasInfo Current()
    {
        var viewport = ImGui.GetMainViewport();
        var io = ImGui.GetIO();


        var origin = IsUsableOrigin(viewport.Pos) ? viewport.Pos : Vector2.Zero;
        var size = IsUsableSize(viewport.Size) ? viewport.Size : io.DisplaySize;

        if (!IsUsableSize(size))
            size = Vector2.One;

        return new HudCanvasInfo(origin, Vector2.Max(size, Vector2.One));
    }


    public static float ReferenceDisplayScale(Vector2 viewportSize)
    {
        if (!IsUsableSize(viewportSize))
            return 1f;

        var scale = MathF.Min(viewportSize.X / 1920f, viewportSize.Y / 1080f);
        return Math.Clamp(scale, 0.55f, 2.50f);
    }

    public static string DiagnosticText()
    {
        var viewport = ImGui.GetMainViewport();
        var io = ImGui.GetIO();
        var resolved = Current();
        return $"DisplaySize={Format(io.DisplaySize)} | MainViewport.Pos={Format(viewport.Pos)} | MainViewport.Size={Format(viewport.Size)} | FramebufferScale={Format(io.DisplayFramebufferScale)} | ResolvedCanvas.Origin={Format(resolved.Origin)} | ResolvedCanvas.Size={Format(resolved.Size)} | MousePos={Format(io.MousePos)}";
    }

    private static string Format(Vector2 value)
        => $"{value.X:0.##},{value.Y:0.##}";

    private static bool IsUsableSize(Vector2 value)
        => float.IsFinite(value.X) &&
           float.IsFinite(value.Y) &&
           value.X > 1f &&
           value.Y > 1f;

    private static bool IsUsableOrigin(Vector2 value)
        => float.IsFinite(value.X) && float.IsFinite(value.Y);
}
