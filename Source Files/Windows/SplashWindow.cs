using System;
using System.Diagnostics;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Textures;
using Dalamud.Interface.Textures.TextureWraps;
using Dalamud.Interface.Windowing;
using REFrameXIV.UI;

namespace REFrameXIV.Windows;


public sealed class SplashWindow : Window, IDisposable
{
    private const double IntroEnd = 0.34;
    private const double HoldEnd = 1.10;
    private const double OutroEnd = 1.52;

    private readonly ISharedImmediateTexture logoTexture;
    private readonly Stopwatch clock = new();
    private Action? completed;

    public bool IsPlaying => IsOpen && clock.IsRunning;

    public SplashWindow(Plugin plugin)
        : base("RE:Frame Opening###REFrameSplash",
            ImGuiWindowFlags.NoDecoration |
            ImGuiWindowFlags.NoInputs |
            ImGuiWindowFlags.NoSavedSettings |
            ImGuiWindowFlags.NoNav |
            ImGuiWindowFlags.NoBackground)
    {
        var logoPath = System.IO.Path.Combine(
            Plugin.PluginInterface.AssemblyLocation.DirectoryName!,
            "Assets",
            "REFrameLogo.png");

        logoTexture = Plugin.TextureProvider.GetFromFile(logoPath);
        IsOpen = false;
        RespectCloseHotkey = false;
    }

    public void Play(Action onCompleted)
    {
        completed = onCompleted;
        clock.Restart();
        IsOpen = true;
    }

    public void Cancel()
    {
        clock.Reset();
        completed = null;
        IsOpen = false;
    }

    public void CompleteImmediately()
    {
        if (!IsPlaying && completed is null)
        {
            Cancel();
            return;
        }

        Finish();
    }

    public override void PreDraw()
    {
        var canvas = HudCanvas.Current();
        ImGui.SetNextWindowPos(canvas.Origin, ImGuiCond.Always);
        ImGui.SetNextWindowSize(canvas.Size, ImGuiCond.Always);


        ImGui.SetNextWindowFocus();

        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, Vector2.Zero);
        ImGui.PushStyleVar(ImGuiStyleVar.WindowBorderSize, 0f);
    }

    public override void PostDraw()
    {
        ImGui.PopStyleVar(2);
    }

    public override void Draw()
    {
        var elapsed = clock.Elapsed.TotalSeconds;
        if (elapsed >= OutroEnd)
        {
            Finish();
            return;
        }

        var draw = ImGui.GetWindowDrawList();
        var origin = ImGui.GetWindowPos();
        var canvas = ImGui.GetWindowSize();


        draw.AddRectFilled(origin, origin + canvas, 0xFF000000);

        IDalamudTextureWrap? wrap = logoTexture.GetWrapOrDefault();
        if (wrap is null)
            return;

        var phase = GetPhase(elapsed);
        var alpha = GetAlpha(elapsed);
        var logoSize = GetLogoSize(canvas, phase.Scale);
        var center = origin + canvas * 0.5f;
        var topLeft = center - logoSize * 0.5f;

        DrawGlitchedLogo(draw, wrap.Handle, topLeft, logoSize, elapsed, phase.Glitch, alpha);
        DrawScanLines(draw, origin, canvas, elapsed, phase.Glitch, alpha);
    }

    public void Dispose()
    {
        Cancel();
    }

    private void Finish()
    {
        clock.Reset();
        IsOpen = false;
        var callback = completed;
        completed = null;
        callback?.Invoke();
    }

    private static (float Glitch, float Scale) GetPhase(double elapsed)
    {
        if (elapsed < IntroEnd)
        {
            var t = (float)(elapsed / IntroEnd);
            var eased = 1f - MathF.Pow(1f - t, 3f);
            return (1f - eased, 0.90f + eased * 0.10f);
        }

        if (elapsed < HoldEnd)
            return (0.02f, 1f);

        var outT = (float)((elapsed - HoldEnd) / (OutroEnd - HoldEnd));
        return (Math.Clamp(outT, 0f, 1f), 1f + outT * 0.025f);
    }

    private static float GetAlpha(double elapsed)
    {
        if (elapsed < 0.12)
            return Math.Clamp((float)(elapsed / 0.12), 0f, 1f);

        if (elapsed <= HoldEnd)
            return 1f;

        var t = (float)((elapsed - HoldEnd) / (OutroEnd - HoldEnd));
        return 1f - Math.Clamp(t * t, 0f, 1f);
    }

    private static Vector2 GetLogoSize(Vector2 canvas, float scale)
    {
        var side = MathF.Min(canvas.X * 0.56f, canvas.Y * 0.74f) * scale;
        side = Math.Clamp(side, 340f, 820f);
        return new Vector2(side, side);
    }

    private static void DrawGlitchedLogo(
        ImDrawListPtr draw,
        ImTextureID texture,
        Vector2 topLeft,
        Vector2 size,
        double elapsed,
        float glitch,
        float alpha)
    {
        const int slices = 22;
        var seed = (int)(elapsed * 52.0);
        var baseColor = ImGui.ColorConvertFloat4ToU32(new Vector4(1f, 1f, 1f, alpha));

        for (var i = 0; i < slices; i++)
        {
            var uvY0 = i / (float)slices;
            var uvY1 = (i + 1) / (float)slices;
            var y0 = topLeft.Y + size.Y * uvY0;
            var y1 = topLeft.Y + size.Y * uvY1 + 0.75f;

            var noise = Hash(seed + i * 31);
            var offset = (noise - 0.5f) * 96f * glitch;
            if (glitch < 0.08f && i % 7 != seed % 7)
                offset *= 0.08f;

            draw.AddImage(
                texture,
                new Vector2(topLeft.X + offset, y0),
                new Vector2(topLeft.X + size.X + offset, y1),
                new Vector2(0f, uvY0),
                new Vector2(1f, uvY1),
                baseColor);
        }


        if (glitch > 0.14f)
        {
            var chromaAlpha = alpha * glitch * 0.28f;
            var red = ImGui.ColorConvertFloat4ToU32(new Vector4(0.75f, 0.08f, 0.10f, chromaAlpha));
            var silver = ImGui.ColorConvertFloat4ToU32(new Vector4(0.72f, 0.86f, 1f, chromaAlpha * 0.7f));
            var shift = 7f + glitch * 13f;
            draw.AddImage(texture, topLeft + new Vector2(-shift, 0), topLeft + size + new Vector2(-shift, 0), Vector2.Zero, Vector2.One, red);
            draw.AddImage(texture, topLeft + new Vector2(shift, 0), topLeft + size + new Vector2(shift, 0), Vector2.Zero, Vector2.One, silver);
        }
    }

    private static void DrawScanLines(ImDrawListPtr draw, Vector2 origin, Vector2 canvas, double elapsed, float glitch, float alpha)
    {
        var lineAlpha = (byte)Math.Clamp((int)(18f * alpha), 0, 255);
        var color = (uint)(lineAlpha << 24) | 0x00FFFFFFu;
        for (var y = origin.Y + 1f; y < origin.Y + canvas.Y; y += 4f)
            draw.AddLine(new Vector2(origin.X, y), new Vector2(origin.X + canvas.X, y), color, 1f);

        if (glitch <= 0.08f)
            return;

        var sweep = origin.Y + (float)((elapsed * 680.0) % Math.Max(1f, canvas.Y));
        var accentAlpha = (byte)Math.Clamp((int)(120f * glitch * alpha), 0, 255);
        var accent = (uint)(accentAlpha << 24) | 0x006458B8u; 
        draw.AddRectFilled(
            new Vector2(origin.X, sweep),
            new Vector2(origin.X + canvas.X, sweep + 2f + glitch * 4f),
            accent);
    }

    private static float Hash(int value)
    {
        unchecked
        {
            var x = (uint)value;
            x ^= x >> 16;
            x *= 0x7feb352d;
            x ^= x >> 15;
            x *= 0x846ca68b;
            x ^= x >> 16;
            return (x & 0x00FFFFFF) / 16777215f;
        }
    }
}
