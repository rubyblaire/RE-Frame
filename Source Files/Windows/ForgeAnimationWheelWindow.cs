using System;
using System.Linq;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Windowing;
using REFrameXIV.Models;
using REFrameXIV.UI;

namespace REFrameXIV.Windows;

public sealed class ForgeAnimationWheelWindow : Window, IDisposable
{
    private readonly Plugin plugin;

    public ForgeAnimationWheelWindow(Plugin plugin)
        : base("RE:Forge+ Animation Wheel###REForgeAnimationWheel",
            ImGuiWindowFlags.NoTitleBar |
            ImGuiWindowFlags.NoResize |
            ImGuiWindowFlags.NoMove |
            ImGuiWindowFlags.NoCollapse |
            ImGuiWindowFlags.NoSavedSettings |
            ImGuiWindowFlags.NoScrollbar |
            ImGuiWindowFlags.NoScrollWithMouse |
            ImGuiWindowFlags.NoNav)
    {
        this.plugin = plugin;
        IsOpen = false;
        ShowCloseButton = false;
        RespectCloseHotkey = true;
        AllowBackgroundBlur = true;
    }

    public void ToggleOpenState()
    {
        IsOpen = !IsOpen;
        if (IsOpen) BringToFront();
    }

    public override bool DrawConditions()
    {
        if (!IsOpen) return false;
        var valid = plugin.ForgeAccess.HasPlusAccess &&
                    Plugin.ClientState.IsLoggedIn &&
                    !Plugin.GameGui.GameUiHidden &&
                    !plugin.IsHudEditMode &&
                    !plugin.AfkScreen.IsActive;
        if (!valid) IsOpen = false;
        return valid;
    }

    public override void PreDraw()
    {
        var canvas = HudCanvas.Current();
        var scale = Math.Clamp(plugin.Configuration.InterfaceScale, 0.60f, 2.50f);
        var side = Math.Clamp(470f * scale, 400f, MathF.Min(620f, MathF.Min(canvas.Size.X, canvas.Size.Y) - 32f));
        var size = new Vector2(side, side);
        var position = canvas.Origin + (canvas.Size - size) * 0.5f;
        ImGui.SetNextWindowPos(position, ImGuiCond.Always);
        ImGui.SetNextWindowSize(size, ImGuiCond.Always);
        UiStyles.PushWindowStyle(plugin.CurrentTheme, plugin.CurrentThemeStyle);
    }

    public override void PostDraw()
    {
        UiStyles.PopWindowStyle();
    }

    public override void Draw()
    {
        var settings = plugin.Configuration.ForgePremium;
        settings.EnsureValid();
        var wheel = settings.AnimationWheel;
        var library = settings.AnimationLibraryEntries;
        var size = ImGui.GetWindowSize();
        var center = size * 0.5f;
        var radius = size.X * 0.33f;
        var buttonSize = new Vector2(Math.Clamp(size.X * 0.25f, 112f, 154f), 54f);

        ImGui.SetCursorPos(new Vector2(center.X - 105f, center.Y - 36f));
        if (ImGui.BeginChild("##wheel-center", new Vector2(210f, 72f), true, ImGuiWindowFlags.NoScrollbar))
        {
            ImGui.SetWindowFontScale(1.12f);
            ImGui.TextUnformatted("ANIMATION WHEEL");
            ImGui.SetWindowFontScale(1f);
            ImGui.TextDisabled($"{VirtualKeyLabel(wheel.VirtualKey)} or /ref wheel");
        }
        ImGui.EndChild();

        for (var index = 0; index < 8; index++)
        {
            var angle = -MathF.PI / 2f + index * (MathF.PI * 2f / 8f);
            var point = center + new Vector2(MathF.Cos(angle), MathF.Sin(angle)) * radius - buttonSize * 0.5f;
            ImGui.SetCursorPos(point);
            var id = wheel.Slots[index];
            var animation = library.FirstOrDefault(item => string.Equals(item.Id, id, StringComparison.OrdinalIgnoreCase));
            var label = animation is null ? $"EMPTY {index + 1}" : animation.Name;
            ImGui.PushID($"forge-wheel-{index}");
            if (ImGui.Button(label, buttonSize) && animation is not null)
            {
                plugin.ForgePlus.PlayAnimation(animation.Id);
                IsOpen = false;
            }
            if (ImGui.IsItemHovered())
                ImGui.SetTooltip(animation is null ? "Assign this slot in RE:Forge+ > Animation Wheel." : animation.TriggerCommand);
            ImGui.PopID();
        }

        ImGui.SetCursorPos(new Vector2(size.X - 78f, 12f));
        if (ImGui.SmallButton("CLOSE")) IsOpen = false;
    }

    private static string VirtualKeyLabel(int key)
        => key is >= 0x70 and <= 0x87 ? $"F{key - 0x6F}" : $"Key {key}";

    public void Dispose() { }
}
