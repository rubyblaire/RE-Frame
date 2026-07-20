using System;
using System.Collections.Generic;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Windowing;
using REFrameXIV.Models;
using REFrameXIV.Services;
using REFrameXIV.Theme;
using REFrameXIV.UI;

namespace REFrameXIV.Windows;

public sealed class HudOverlayWindow : Window, IDisposable
{
    private readonly Plugin plugin;
    private List<ClipRegion> clipRegions = new(12);
    private List<ClipRegion> nextClipRegions = new(12);
    private readonly List<NativeUiCutout> uiCutouts = new(48);
    private float combatBlend;
    private HudCanvasInfo lastRenderedCanvas;
    private bool hasRenderedCanvas;


    public bool TryGetRenderedCanvas(out HudCanvasInfo canvas)
    {
        canvas = lastRenderedCanvas;
        return hasRenderedCanvas && canvas.Size.X > 1f && canvas.Size.Y > 1f;
    }

    public HudOverlayWindow(Plugin plugin)
        : base("RE:Frame HUD###REFrameHud",
            ImGuiWindowFlags.NoDecoration |
            ImGuiWindowFlags.NoMove |
            ImGuiWindowFlags.NoResize |
            ImGuiWindowFlags.NoSavedSettings |
            ImGuiWindowFlags.NoFocusOnAppearing |
            ImGuiWindowFlags.NoBringToFrontOnFocus |
            ImGuiWindowFlags.NoNav |
            ImGuiWindowFlags.NoScrollbar |
            ImGuiWindowFlags.NoScrollWithMouse |
            ImGuiWindowFlags.NoBackground)
    {
        this.plugin = plugin;
        IsClickthrough = true;
        ShowCloseButton = false;
        RespectCloseHotkey = false;
        DisableWindowSounds = true;
        BgAlpha = 0f;
        combatBlend = HudModeProfileService.IsCalmMode(plugin.AdaptiveState.EffectiveMode) ? 0f : 1f;
    }

    public override bool DrawConditions() => (plugin.Configuration.ShowHudOverlay || plugin.IsHudEditMode) && Plugin.ClientState.IsLoggedIn && !Plugin.GameGui.GameUiHidden && !Plugin.ClientState.IsGPosing;

    public override void PreDraw()
    {


        IsClickthrough = true;

        var canvas = HudCanvas.Current();
        Position = canvas.Origin;
        PositionCondition = ImGuiCond.Always;
        Size = canvas.Size;
        SizeCondition = ImGuiCond.Always;


        ImGui.SetNextWindowPos(canvas.Origin, ImGuiCond.Always);
        ImGui.SetNextWindowSize(canvas.Size, ImGuiCond.Always);
        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, Vector2.Zero);
        ImGui.PushStyleVar(ImGuiStyleVar.WindowBorderSize, 0f);
    }

    public override void PostDraw() => ImGui.PopStyleVar(2);

    public override void Draw()
    {
        try
        {


            ImGui.SetWindowFontScale(Math.Clamp(plugin.Configuration.TextScale, 0.75f, 1.75f));

            var target = HudModeProfileService.IsCalmMode(plugin.CurrentHudMode) ? 0f : 1f;
            if (plugin.Configuration.ReducedMotion)
                combatBlend = target;
            else
            {
                var speed = target > combatBlend ? 6.5f : 3.4f;
                var step = Math.Clamp(ImGui.GetIO().DeltaTime * speed, 0f, 1f);
                combatBlend += (target - combatBlend) * step;
                if (MathF.Abs(target - combatBlend) < 0.002f)
                    combatBlend = target;
            }

            var draw = ImGui.GetWindowDrawList();
            var canvas = HudCanvas.Current();
            var origin = canvas.Origin;
            var size = canvas.Size;


            lastRenderedCanvas = canvas;
            hasRenderedCanvas = true;

            draw.PushClipRect(canvas.Origin, canvas.Max, false);
            try
            {
                DrawHudRespectingNativeUi(draw, origin, size);
            }
            finally
            {
                draw.PopClipRect();
            }
        }
        catch (Exception ex)
        {

            Plugin.Log.Error(ex, "RE:Frame HUD rendering failed. Restoring the native FFXIV HUD.");
            plugin.SetHudEditMode(false);
            plugin.Configuration.ReplaceNativeHud = false;
            plugin.Configuration.ShowHudOverlay = false;
            plugin.SaveConfiguration();
            plugin.NativeHudVisibility.RestoreAll();
            Plugin.ChatGui.Print("RE:Frame encountered a HUD rendering error and restored the native FFXIV HUD.");
            IsOpen = false;
        }
    }

    private void DrawHudRespectingNativeUi(ImDrawListPtr draw, Vector2 origin, Vector2 size)
    {


        if (plugin.HotbarEditing.IsEnabled)
        {
            uiCutouts.Clear();
            return;
        }

        SyncNativeMinimap(origin, size);
        CollectNativeUiCutouts();
        var hasCutouts = !plugin.IsHudEditMode && uiCutouts.Count > 0;

        if (!hasCutouts)
        {
            HudRenderer.Draw(plugin, draw, origin, size, false, combatBlend, plugin.IsHudEditMode);
        }
        else
        {
            BuildVisibleClipRegions(origin, origin + size, uiCutouts);
            foreach (var region in clipRegions)
            {
                if (region.Max.X - region.Min.X < 1f || region.Max.Y - region.Min.Y < 1f)
                    continue;

                draw.PushClipRect(region.Min, region.Max, true);
                HudRenderer.Draw(plugin, draw, origin, size, false, combatBlend, false);
                draw.PopClipRect();
            }
        }

        if (!plugin.IsHudEditMode && plugin.Configuration.NativeWindowGlassEffect)
            DrawNativeWindowGlassBackdrops(draw, plugin.NativeWindows.VisibleWindowBounds, plugin.CurrentTheme, plugin.Configuration.NativeWindowGlassOpacity);

        if (!plugin.IsHudEditMode && plugin.Configuration.SkinNativeContextMenus)
            DrawNativeSubmenuSeparation(draw, plugin.NativeContextMenus.VisibleMenuBounds);

        if (!plugin.IsHudEditMode)
            HudActorInputRouter.Process(plugin, draw, origin, size, false);

    }

    private void SyncNativeMinimap(Vector2 origin, Vector2 size)
    {
        var mode = plugin.CurrentHudMode;
        if (!plugin.IsHudElementVisible(HudElementIds.Minimap, mode))
        {
            plugin.ForgeSquareMap.Restore();
            plugin.NativeHudVisibility.RestoreMinimapIntegration();
            return;
        }

        var bounds = HudLayout.Resolve(plugin.Configuration, HudElementIds.Minimap, origin, size, mode);
        var viewportBounds = new HudBounds(origin, size);
        var scale = Math.Clamp(plugin.Configuration.InterfaceScale, 0.60f, 2.50f);
        if (plugin.ShouldUseForgeSquareMinimap)
        {
            plugin.NativeHudVisibility.RestoreMinimapIntegration();
            plugin.ForgeSquareMap.SyncToFrame(bounds, viewportBounds, scale);
            return;
        }

        plugin.ForgeSquareMap.Restore();
        plugin.NativeHudVisibility.SyncMinimapToFrame(bounds, viewportBounds, scale);
    }

    private void CollectNativeUiCutouts()
    {
        uiCutouts.Clear();


        foreach (var windowBounds in plugin.NativeWindows.HudOcclusionWindowBounds)
        {
            var cutoutPadding = new Vector2(1f, 1f);
            uiCutouts.Add(new NativeUiCutout(
                windowBounds.Min - cutoutPadding,
                windowBounds.Max + cutoutPadding));
        }

        foreach (var menuBounds in plugin.NativeContextMenus.VisibleMenuBounds)
        {
            var cutoutPadding = new Vector2(2f, 2f);
            uiCutouts.Add(new NativeUiCutout(
                menuBounds.Min - cutoutPadding,
                menuBounds.Max + cutoutPadding));
        }


        if (plugin.ForgeSquareMap.IsFullMapMode &&
            plugin.ForgeSquareMap.TryGetMapVisualBounds(out var fullMapBounds))
        {
            var cutoutPadding = new Vector2(2f, 2f);
            uiCutouts.Add(new NativeUiCutout(
                fullMapBounds.Position - cutoutPadding,
                fullMapBounds.Position + fullMapBounds.Size + cutoutPadding));
        }

    }


    private void BuildVisibleClipRegions(Vector2 viewportMin, Vector2 viewportMax, IReadOnlyList<NativeUiCutout> cutouts)
    {
        clipRegions.Clear();
        clipRegions.Add(new ClipRegion(viewportMin, viewportMax));

        foreach (var cutout in cutouts)
        {
            nextClipRegions.Clear();
            foreach (var region in clipRegions)
                Subtract(region, cutout, nextClipRegions);

            (clipRegions, nextClipRegions) = (nextClipRegions, clipRegions);
        }
    }

    private static void Subtract(ClipRegion source, NativeUiCutout cutout, List<ClipRegion> output)
    {
        var intersectionMin = Vector2.Max(source.Min, cutout.Min);
        var intersectionMax = Vector2.Min(source.Max, cutout.Max);
        if (intersectionMax.X <= intersectionMin.X || intersectionMax.Y <= intersectionMin.Y)
        {
            output.Add(source);
            return;
        }

        AddIfValid(output, new ClipRegion(
            source.Min,
            new Vector2(source.Max.X, intersectionMin.Y)));

        AddIfValid(output, new ClipRegion(
            new Vector2(source.Min.X, intersectionMax.Y),
            source.Max));

        AddIfValid(output, new ClipRegion(
            new Vector2(source.Min.X, intersectionMin.Y),
            new Vector2(intersectionMin.X, intersectionMax.Y)));

        AddIfValid(output, new ClipRegion(
            new Vector2(intersectionMax.X, intersectionMin.Y),
            new Vector2(source.Max.X, intersectionMax.Y)));
    }

    private static void AddIfValid(List<ClipRegion> regions, ClipRegion region)
    {
        if (region.Max.X - region.Min.X >= 1f && region.Max.Y - region.Min.Y >= 1f)
            regions.Add(region);
    }

    private static void DrawNativeSubmenuSeparation(
        ImDrawListPtr draw,
        IReadOnlyList<NativeMenuBounds> menus)
    {
        if (menus.Count == 0)
            return;

        var shadow = new Vector4(0f, 0f, 0f, 0.62f);
        var edge = new Vector4(0.937f, 0.894f, 0.769f, 0.74f);
        var edgeSoft = new Vector4(0.937f, 0.894f, 0.769f, 0.20f);

        foreach (var menu in menus)
        {


            if (!menu.IsMenuPanel)
                continue;

            var min = menu.Min;
            var max = menu.Max;
            if (max.X - min.X < 20f || max.Y - min.Y < 20f)
                continue;


            var bottomY = max.Y - 0.5f;
            var rightX = max.X - 0.5f;
            draw.AddLine(
                new Vector2(min.X + 4f, bottomY),
                new Vector2(rightX, bottomY),
                ImGui.GetColorU32(shadow),
                4.5f);
            draw.AddLine(
                new Vector2(rightX, min.Y + 4f),
                new Vector2(rightX, bottomY),
                ImGui.GetColorU32(shadow),
                4.5f);

            draw.AddLine(
                new Vector2(min.X + 4f, bottomY),
                new Vector2(rightX, bottomY),
                ImGui.GetColorU32(edgeSoft),
                2.4f);
            draw.AddLine(
                new Vector2(rightX, min.Y + 4f),
                new Vector2(rightX, bottomY),
                ImGui.GetColorU32(edgeSoft),
                2.4f);
            draw.AddLine(
                new Vector2(min.X + 4f, bottomY),
                new Vector2(rightX, bottomY),
                ImGui.GetColorU32(edge),
                1.15f);
            draw.AddLine(
                new Vector2(rightX, min.Y + 4f),
                new Vector2(rightX, bottomY),
                ImGui.GetColorU32(edge),
                1.15f);
        }
    }

    private static void DrawNativeWindowGlassBackdrops(
        ImDrawListPtr draw,
        IReadOnlyList<NativeWindowBounds> windows,
        ThemePalette theme,
        float configuredOpacity)
    {
        if (windows.Count == 0)
            return;

        var opacity = Math.Clamp(configuredOpacity, 0.20f, 1f);
        var outerFill = UiStyles.WithAlpha(theme.Panel, 0.18f + 0.22f * opacity);
        var innerFill = UiStyles.WithAlpha(theme.PanelAlt, 0.12f + 0.16f * opacity);
        var border = UiStyles.WithAlpha(theme.AccentStrong, 0.18f + 0.24f * opacity);
        var sheenTop = UiStyles.WithAlpha(theme.Text, 0.05f + 0.08f * opacity);
        var sheenBottom = UiStyles.WithAlpha(theme.AccentStrong, 0.01f + 0.03f * opacity);
        var shadow = new Vector4(0f, 0f, 0f, 0.12f + 0.10f * opacity);

        foreach (var window in windows)
        {


            var outerMin = window.Min;
            var windowHeight = MathF.Max(1f, window.Max.Y - window.Min.Y);
            var nativeBottomPadding = Math.Clamp(windowHeight * 0.022f, 5f, 12f);
            var outerMax = new Vector2(window.Max.X, window.Max.Y - nativeBottomPadding);
            var innerMin = outerMin + new Vector2(5f, 5f);
            var innerMax = outerMax - new Vector2(5f, 5f);
            const float rounding = 10f;

            DrawBackdropBand(draw, outerMin, outerMax, new Vector2(outerMin.X, outerMin.Y), new Vector2(outerMax.X, innerMin.Y), outerFill, innerFill, rounding);
            DrawBackdropBand(draw, outerMin, outerMax, new Vector2(outerMin.X, innerMax.Y), new Vector2(outerMax.X, outerMax.Y), outerFill, innerFill, rounding);
            DrawBackdropBand(draw, outerMin, outerMax, new Vector2(outerMin.X, innerMin.Y), new Vector2(innerMin.X, innerMax.Y), outerFill, innerFill, rounding);
            DrawBackdropBand(draw, outerMin, outerMax, new Vector2(innerMax.X, innerMin.Y), new Vector2(outerMax.X, innerMax.Y), outerFill, innerFill, rounding);


            var strokeMin = outerMin + new Vector2(0.5f, 0.5f);
            var strokeMax = outerMax - new Vector2(0.5f, 0.5f);
            draw.AddRect(strokeMin, strokeMax, ImGui.GetColorU32(shadow), rounding, ImDrawFlags.None, 5f);
            draw.AddRect(strokeMin, strokeMax, ImGui.GetColorU32(border), rounding, ImDrawFlags.None, 1.3f);


            draw.PushClipRect(
                new Vector2(outerMin.X + 1f, outerMin.Y + 1f),
                new Vector2(outerMax.X - 1f, MathF.Min(outerMax.Y - 1f, outerMin.Y + 15f)),
                true);
            draw.AddRectFilledMultiColor(
                outerMin + new Vector2(1f, 1f),
                new Vector2(outerMax.X - 1f, MathF.Min(outerMax.Y - 1f, outerMin.Y + 15f)),
                ImGui.GetColorU32(sheenTop),
                ImGui.GetColorU32(UiStyles.WithAlpha(theme.Text, sheenTop.W * 0.65f)),
                ImGui.GetColorU32(sheenBottom),
                ImGui.GetColorU32(sheenBottom));
            draw.PopClipRect();
        }
    }

    private static void DrawBackdropBand(
        ImDrawListPtr draw,
        Vector2 outerMin,
        Vector2 outerMax,
        Vector2 clipMin,
        Vector2 clipMax,
        Vector4 outerFill,
        Vector4 innerFill,
        float rounding)
    {
        if (clipMax.X - clipMin.X < 1f || clipMax.Y - clipMin.Y < 1f)
            return;

        draw.PushClipRect(clipMin, clipMax, true);
        draw.AddRectFilled(outerMin, outerMax, ImGui.GetColorU32(outerFill), rounding);
        draw.AddRectFilled(outerMin + new Vector2(3f, 3f), outerMax - new Vector2(3f, 3f), ImGui.GetColorU32(innerFill), MathF.Max(0f, rounding - 3f));
        draw.PopClipRect();
    }

    public void Dispose() { }

    private readonly record struct ClipRegion(Vector2 Min, Vector2 Max);
    private readonly record struct NativeUiCutout(Vector2 Min, Vector2 Max);

}
