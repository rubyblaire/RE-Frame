using System;
using System.Diagnostics;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Windowing;
using REFrameXIV.Models;
using REFrameXIV.Services;
using REFrameXIV.UI;

namespace REFrameXIV.Windows;


public sealed class LoginGreetingWindow : Window, IDisposable
{
    private const double FadeInEnd = 0.55;
    private const double BaseHoldEnd = 3.05;
    private const double OutroDuration = 1.15;
    private const double VoiceTailPadding = 0.15;

    private readonly Plugin plugin;
    private readonly Stopwatch clock = new();
    private string greeting = "Good Evening!";
    private double holdEnd = BaseHoldEnd;
    private double outroEnd = BaseHoldEnd + OutroDuration;
    private bool manualPreview;

    public bool IsPlaying => clock.IsRunning;
    public bool IsManualPreview => manualPreview && IsPlaying;

    public LoginGreetingWindow(Plugin plugin)
        : base("RE:Frame Greeting###REFrameLoginGreeting",
            ImGuiWindowFlags.NoDecoration |
            ImGuiWindowFlags.NoInputs |
            ImGuiWindowFlags.NoSavedSettings |
            ImGuiWindowFlags.NoBringToFrontOnFocus |
            ImGuiWindowFlags.NoNav |
            ImGuiWindowFlags.NoBackground)
    {
        this.plugin = plugin;


        IsOpen = true;
        RespectCloseHotkey = false;
    }

    public void Play(DateTime localTime)
        => Start(localTime, false);

    public void Preview(DateTime localTime)
        => Start(localTime, true);

    private void Start(DateTime localTime, bool isManualPreview)
    {
        GreetingVoiceService.Stop();
        manualPreview = isManualPreview;
        greeting = ResolveGreetingText(localTime.Hour);
        var voiceDuration = GreetingVoiceService.Play(localTime.Hour, plugin.Configuration, out var rotationAdvanced);
        if (rotationAdvanced)
            plugin.SaveConfiguration();

        holdEnd = Math.Max(BaseHoldEnd, voiceDuration + VoiceTailPadding);
        outroEnd = holdEnd + OutroDuration;
        clock.Restart();
        IsOpen = true;
    }

    public void Cancel()
    {
        clock.Reset();
        manualPreview = false;
        GreetingVoiceService.Stop();
    }

    public override bool DrawConditions()
        => (!IsManualPreview
                ? !plugin.HotbarEditing.IsEnabled &&
                  plugin.CurrentHudMode == UiMode.Leisure &&
                  plugin.IsHudElementVisible(HudElementIds.Greeting, UiMode.Leisure)
                : !plugin.HotbarEditing.IsEnabled) &&
           (IsPlaying || plugin.IsHudEditMode) &&
           Plugin.ClientState.IsLoggedIn &&
           !Plugin.GameGui.GameUiHidden && !Plugin.ClientState.IsGPosing;

    public override void PreDraw()
    {
        var canvas = HudCanvas.Current();
        var bounds = HudLayout.Resolve(plugin.Configuration, HudElementIds.Greeting, canvas.Origin, canvas.Size, UiMode.Leisure);
        ImGui.SetNextWindowPos(bounds.Position, ImGuiCond.Always);
        ImGui.SetNextWindowSize(bounds.Size, ImGuiCond.Always);
        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, Vector2.Zero);
        ImGui.PushStyleVar(ImGuiStyleVar.WindowBorderSize, 0f);
    }

    public override void PostDraw()
    {


        ImGui.PopStyleVar(2);
    }

    public override void Draw()
    {
        var editPreview = plugin.IsHudEditMode;
        var elapsed = clock.Elapsed.TotalSeconds;
        if (!editPreview && elapsed >= outroEnd)
        {
            Cancel();
            return;
        }

        if (editPreview && !IsPlaying)
            greeting = ResolveGreetingText(DateTime.Now.Hour);

        var alpha = editPreview ? 0.88f : ResolveAlpha(elapsed, holdEnd, outroEnd);
        var theme = plugin.CurrentTheme;
        var origin = ImGui.GetWindowPos();
        var canvas = ImGui.GetWindowSize();
        var interfaceScale = Math.Clamp(plugin.Configuration.InterfaceScale, 0.60f, 2.50f);
        var fitScale = Math.Clamp(MathF.Min(canvas.X / 450f, canvas.Y / 54f), 0.65f, 1.75f);
        var fontScale = 2.05f * interfaceScale * fitScale;

        ImGui.SetWindowFontScale(fontScale);
        var textSize = ImGui.CalcTextSize(greeting);
        var availableTextWidth = MathF.Max(1f, canvas.X - 12f * interfaceScale);
        if (textSize.X > availableTextWidth)
        {
            fontScale *= Math.Clamp(availableTextWidth / textSize.X, 0.55f, 1f);
            ImGui.SetWindowFontScale(fontScale);
            textSize = ImGui.CalcTextSize(greeting);
        }
        var slide = editPreview || plugin.Configuration.ReducedMotion
            ? 0f
            : (1f - ResolveEntrance(elapsed)) * 10f;
        var textPosition = new Vector2(
            origin.X + (canvas.X - textSize.X) * 0.5f,
            origin.Y + MathF.Max(0f, (canvas.Y - textSize.Y) * 0.38f) - slide);

        ImGui.SetCursorScreenPos(textPosition + new Vector2(2f, 3f));
        ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(0f, 0f, 0f, alpha * 0.82f));
        ImGui.TextUnformatted(greeting);
        ImGui.PopStyleColor();

        ImGui.SetCursorScreenPos(textPosition);
        ImGui.PushStyleColor(ImGuiCol.Text, UiStyles.WithAlpha(theme.Text, alpha));
        ImGui.TextUnformatted(greeting);
        ImGui.PopStyleColor();

        var draw = ImGui.GetWindowDrawList();
        var lineWidth = Math.Clamp(textSize.X * 0.62f, 110f * interfaceScale, canvas.X * 0.72f);
        var lineY = MathF.Min(origin.Y + canvas.Y - 4f, textPosition.Y + textSize.Y + 7f * interfaceScale);
        var lineMin = new Vector2(origin.X + (canvas.X - lineWidth) * 0.5f, lineY);
        var lineMax = lineMin + new Vector2(lineWidth, MathF.Max(1.2f, 1.7f * interfaceScale));
        var lineMid = (lineMin.X + lineMax.X) * 0.5f;
        var transparent = ImGui.GetColorU32(UiStyles.WithAlpha(theme.Accent, 0f));
        var bright = ImGui.GetColorU32(UiStyles.WithAlpha(theme.AccentStrong, alpha * 0.92f));
        draw.AddRectFilledMultiColor(lineMin, new Vector2(lineMid, lineMax.Y), transparent, bright, bright, transparent);
        draw.AddRectFilledMultiColor(new Vector2(lineMid, lineMin.Y), lineMax, bright, transparent, transparent, bright);

        ImGui.SetWindowFontScale(1f);
    }

    public void Dispose()
    {
        Cancel();
        IsOpen = false;
    }

    private string ResolveGreetingText(int localHour)
    {
        var salutation = ResolveGreeting(localHour);
        var playerName = Plugin.ObjectTable.LocalPlayer?.Name.ToString().Trim();
        return string.IsNullOrWhiteSpace(playerName)
            ? $"{salutation}!"
            : $"{salutation}, {playerName}!";
    }

    internal static string ResolveGreeting(int localHour)
    {
        localHour = ((localHour % 24) + 24) % 24;
        if (localHour >= 5 && localHour <= 11)
            return "Good Morning";
        if (localHour >= 12 && localHour <= 16)
            return "Good Afternoon";
        return "Good Evening";
    }

    internal static string ResolvePeriodKey(DateTime localTime)
    {
        var hour = ((localTime.Hour % 24) + 24) % 24;
        if (hour >= 5 && hour <= 11)
            return $"{localTime:yyyy-MM-dd}:morning";
        if (hour >= 12 && hour <= 16)
            return $"{localTime:yyyy-MM-dd}:afternoon";


        var eveningDate = hour < 5 ? localTime.Date.AddDays(-1) : localTime.Date;
        return $"{eveningDate:yyyy-MM-dd}:evening";
    }

    private static float ResolveAlpha(double elapsed, double holdEnd, double outroEnd)
    {
        if (elapsed < FadeInEnd)
        {
            var t = Math.Clamp((float)(elapsed / FadeInEnd), 0f, 1f);
            return 1f - MathF.Pow(1f - t, 3f);
        }
        if (elapsed <= holdEnd)
            return 1f;
        var outT = Math.Clamp((float)((elapsed - holdEnd) / Math.Max(0.01, outroEnd - holdEnd)), 0f, 1f);
        return 1f - outT * outT;
    }

    private static float ResolveEntrance(double elapsed)
    {
        if (elapsed >= FadeInEnd)
            return 1f;
        var t = Math.Clamp((float)(elapsed / FadeInEnd), 0f, 1f);
        return 1f - MathF.Pow(1f - t, 3f);
    }
}
