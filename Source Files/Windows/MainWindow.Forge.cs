using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Numerics;
using System.Threading.Tasks;
using Dalamud.Bindings.ImGui;
using REFrameXIV.Theme;
using REFrameXIV.UI;

namespace REFrameXIV.Windows;

public sealed partial class MainWindow
{
    private string forgeSelectedThemeId = string.Empty;
    private string forgeNewThemeName = "My Forge";
    private string forgeImportCode = string.Empty;
    private string forgeExportCode = string.Empty;
    private string forgeStatus = string.Empty;
    private string forgeDeleteThemeId = string.Empty;
    private readonly Dictionary<string, string> forgeColorHex = new(StringComparer.OrdinalIgnoreCase);
    private bool forgeMembershipActionRunning;
    private ForgePage forgePage = ForgePage.Overview;

    private void DrawStandaloneForgeWindow()
    {
        DrawForgeSidebar();
        ImGui.SameLine();
        if (ImGui.BeginChild("##reforge-window-content", Vector2.Zero, false))
        {
            DrawForgeWindowHeader();
            ImGui.Spacing();
            UiStyles.Divider(plugin.CurrentTheme);
            ImGui.Spacing();
            DrawForge();
        }
        ImGui.EndChild();
    }

    private void DrawForgeSidebar()
    {
        var theme = plugin.CurrentTheme;
        ImGui.PushStyleColor(ImGuiCol.ChildBg, UiStyles.WithAlpha(theme.PanelAlt, 0.82f));
        if (ImGui.BeginChild("##reforge-sidebar", new Vector2(218f, 0f), true))
        {
            ImGui.PushStyleColor(ImGuiCol.Text, theme.AccentStrong);
            ImGui.SetWindowFontScale(1.22f);
            ImGui.TextUnformatted("RE:FORGE");
            ImGui.SetWindowFontScale(1f);
            ImGui.PopStyleColor();
            ImGui.TextDisabled("Premium Creative Suite");
            ImGui.Spacing();
            UiStyles.Divider(theme);
            ImGui.Spacing();

            DrawForgeSidebarNav(ForgePage.Overview, "Overview");
            DrawForgeSidebarNav(ForgePage.Create, "Create");
            DrawForgeSidebarNav(ForgePage.Immersion, "Immersion");
            DrawForgeSidebarNav(ForgePage.ForgePlus, "Forge+");
            DrawForgeSidebarNav(ForgePage.Automation, "Automate");
            DrawForgeSidebarNav(ForgePage.Share, "Share");
            DrawForgeSidebarNav(ForgePage.Vault, "Vault");
            DrawForgeSidebarNav(ForgePage.Membership, "Member");

            ImGui.SetCursorPosY(MathF.Max(ImGui.GetCursorPosY(), ImGui.GetWindowHeight() - 78f));
            UiStyles.Divider(theme);
            ImGui.Spacing();
            if (ImGui.Button("BACK TO RE:FRAME", new Vector2(-1f, 38f)))
            {
                IsOpen = false;
                plugin.OpenMainUi();
            }
        }
        ImGui.EndChild();
        ImGui.PopStyleColor();
    }

    private void DrawForgeSidebarNav(ForgePage target, string label)
    {
        var theme = plugin.CurrentTheme;
        var selected = forgePage == target;
        ImGui.PushID($"reforge-sidebar-{target}");
        ImGui.PushStyleVar(ImGuiStyleVar.FrameRounding, 8f);
        ImGui.PushStyleVar(ImGuiStyleVar.FramePadding, new Vector2(12f, 8f));
        ImGui.PushStyleColor(ImGuiCol.Button, selected
            ? UiStyles.WithAlpha(theme.ResolvedNavigationSelected, 0.96f)
            : UiStyles.WithAlpha(theme.ResolvedNavigation, 0.18f));
        ImGui.PushStyleColor(ImGuiCol.ButtonHovered, UiStyles.WithAlpha(theme.ResolvedNavigationHovered, 0.88f));
        ImGui.PushStyleColor(ImGuiCol.ButtonActive, theme.ResolvedNavigationActive);
        ImGui.PushStyleColor(ImGuiCol.Text, selected ? theme.AccentStrong : theme.Text);
        if (ImGui.Button(label, new Vector2(-1f, 42f)))
            forgePage = target;
        if (selected)
        {
            var min = ImGui.GetItemRectMin();
            var max = ImGui.GetItemRectMax();
            ImGui.GetWindowDrawList().AddRectFilled(
                new Vector2(min.X, min.Y + 6f),
                new Vector2(min.X + 3f, max.Y - 6f),
                ImGui.GetColorU32(theme.AccentStrong),
                2f);
        }
        ImGui.PopStyleColor(4);
        ImGui.PopStyleVar(2);
        ImGui.PopID();
    }

    private void DrawForgeWindowHeader()
    {
        var title = forgePage switch
        {
            ForgePage.Create => "Create",
            ForgePage.Immersion => "Immersion",
            ForgePage.Automation => "Automation",
            ForgePage.ForgePlus => "RE:Forge+",
            ForgePage.Share => "Share",
            ForgePage.Vault => "Cloud Vault",
            ForgePage.Membership => "Member Studio",
            _ => "RE:Forge Overview",
        };
        var subtitle = forgePage switch
        {
            ForgePage.Create => "Themes, square-map presentation, and dock identity",
            ForgePage.Immersion => "AFK scenes and directed voice presentation",
            ForgePage.Automation => "Context-aware scenes and display environments",
            ForgePage.ForgePlus => "Motion, scenes, music-reactive dancing, presets, venues, scheduling, wheel, and roulette",
            ForgePage.Share => "Workshop collections, selective fusion, and private links",
            ForgePage.Vault => "Protected local and account-linked restore points",
            ForgePage.Membership => "Preview Lab, voting, identity, and community perks",
            _ => "Your complete premium creative-service suite",
        };

        ImGui.SetWindowFontScale(1.34f);
        ImGui.TextUnformatted(title);
        ImGui.SetWindowFontScale(1f);
        ImGui.TextDisabled(subtitle);
    }

    private void DrawForge()
    {
        var configuration = plugin.Configuration;
        ForgeThemeLibrary.Ensure(configuration);

        if (forgePage == ForgePage.Overview)
        {
            DrawForgeHero();
            ImGui.Spacing();
        }

        if (!DrawForgeMembershipGate())
            return;

        ImGui.Spacing();

        switch (forgePage)
        {
            case ForgePage.Create:
                DrawForgeCreate(configuration);
                break;
            case ForgePage.Immersion:
                DrawForgeImmersionWorkspace(configuration);
                break;
            case ForgePage.Automation:
                DrawForgeAutomationWorkspace(configuration);
                break;
            case ForgePage.ForgePlus:
                DrawForgePlusWorkspace(configuration);
                break;
            case ForgePage.Share:
                DrawForgeShareWorkspace(configuration);
                break;
            case ForgePage.Vault:
                DrawForgeVaultWorkspace(configuration);
                break;
            case ForgePage.Membership:
                DrawForgeMembershipWorkspace(configuration);
                break;
            case ForgePage.Overview:
            default:
                DrawForgeOverview(configuration);
                break;
        }
    }

    private void DrawForgePaletteWorkspace(Configuration configuration)
    {
        var available = MathF.Max(1f, ImGui.GetContentRegionAvail().X);
        var listWidth = available >= 820f ? 260f : MathF.Min(230f, available * 0.30f);

        if (ImGui.BeginChild("##forge-library", new Vector2(listWidth, 0f), true))
            DrawForgeLibrary(configuration);
        ImGui.EndChild();

        ImGui.SameLine();
        if (ImGui.BeginChild("##forge-workbench", Vector2.Zero, true))
        {
            var selected = ResolveForgeSelection(configuration);
            if (selected is null)
                DrawForgeEmptyState(configuration);
            else
                DrawForgeEditor(configuration, selected);
        }
        ImGui.EndChild();
    }

    private void DrawForgeAtmosphere()
    {
        var theme = plugin.CurrentTheme;
        UiStyles.SectionLabel("Atmosphere", theme);
        ImGui.TextWrapped("The living front page of RE:Frame: development news, patch announcements, creator notes, and important service updates.");
        ImGui.Spacing();

        DrawForgeFeatureCard(
            "THE FORGE IS OPEN",
            "Discord membership verification is live. RE:Forge can now recognize the Ko-fi subscriber role and carry your premium identity into the HUD.",
            theme.Success);
        ImGui.Spacing();
        DrawForgeFeatureCard(
            "PATCH SIGNAL",
            "This hub is ready for release notes and announcements. Future builds can pull the latest post from the RE:Frame service without crowding the rest of the editor.",
            theme.AccentStrong);
        ImGui.Spacing();
        DrawForgeFeatureCard(
            "FROM RUBY",
            "Your frame. Your way. RE:Frame it. Then RE:Forge it.",
            theme.AccentMid);

    }

    private void DrawForgeAfkPersonalization(Configuration configuration)
    {
        var theme = plugin.CurrentTheme;
        UiStyles.SectionLabel("AFK screen personalization", theme);
        ImGui.TextWrapped("Control the automatic AFK presentation, its timing, and its audio without leaving The Forge.");
        ImGui.Spacing();

        var changed = false;
        var enabled = configuration.EnableAfkScreen;
        if (ImGui.Checkbox("Show the AFK screen after inactivity##forge-afk-enabled", ref enabled))
        {
            configuration.EnableAfkScreen = enabled;
            changed = true;
        }

        if (configuration.EnableAfkScreen)
        {
            var timeout = Math.Clamp(configuration.AfkTimeoutMinutes, 1, 120);
            ImGui.SetNextItemWidth(MathF.Min(420f, ImGui.GetContentRegionAvail().X));
            if (ImGui.SliderInt("Inactivity delay##forge-afk-timeout", ref timeout, 1, 120, "%d minutes"))
            {
                configuration.AfkTimeoutMinutes = timeout;
                changed = true;
            }

            var audioEnabled = configuration.AfkScreenAudioEnabled;
            if (ImGui.Checkbox("Play AFK audio##forge-afk-audio", ref audioEnabled))
            {
                configuration.AfkScreenAudioEnabled = audioEnabled;
                changed = true;
            }

            if (configuration.AfkScreenAudioEnabled)
            {
                var volume = configuration.AfkScreenVolume;
                ImGui.SetNextItemWidth(MathF.Min(420f, ImGui.GetContentRegionAvail().X));
                if (ImGui.SliderFloat("AFK screen volume##forge-afk-volume", ref volume, 0f, 1f, "%.2f"))
                {
                    configuration.AfkScreenVolume = volume;
                    changed = true;
                }
            }
        }

        if (changed)
        {
            plugin.SaveConfiguration();
            plugin.AfkScreen.ApplyConfigurationChange();
        }

        var previewLabel = plugin.AfkScreen.IsActive ? "STOP AFK PREVIEW" : "PREVIEW AFK SCREEN";
        if (ImGui.Button($"{previewLabel}##forge-afk-preview", new Vector2(230f, 36f)))
            plugin.ToggleAfkScreenPreview();
    }

    private void DrawForgeFeatureCard(string title, string text, Vector4 accent)
    {
        ImGui.PushStyleColor(ImGuiCol.ChildBg, UiStyles.WithAlpha(plugin.CurrentTheme.PanelAlt, 0.48f));
        if (ImGui.BeginChild($"##forge-feature-{title}", new Vector2(0f, 104f), true, ImGuiWindowFlags.NoScrollbar))
        {
            ImGui.PushStyleColor(ImGuiCol.Text, accent);
            ImGui.TextUnformatted(title);
            ImGui.PopStyleColor();
            ImGui.Spacing();
            ImGui.TextWrapped(text);
        }
        ImGui.EndChild();
        ImGui.PopStyleColor();
    }

    private void DrawForgeHero()
    {
        var theme = plugin.CurrentTheme;
        ImGui.PushStyleColor(ImGuiCol.ChildBg, UiStyles.WithAlpha(theme.PanelAlt, 0.52f));
        if (ImGui.BeginChild("##forge-hero", new Vector2(0f, 104f), true, ImGuiWindowFlags.NoScrollbar))
        {
            ImGui.PushStyleColor(ImGuiCol.Text, theme.AccentStrong);
            ImGui.SetWindowFontScale(1.28f);
            ImGui.TextUnformatted("RE:FORGE · PREMIUM CREATIVE SERVICES");
            ImGui.SetWindowFontScale(1f);
            ImGui.PopStyleColor();
            ImGui.TextWrapped("Create a signature HUD, direct your presentation, protect it in the Cloud Vault, and step into Forge+ for motion, scenes, music-reactive dancing, characters, venues, and live automation.");
            ImGui.TextDisabled("Base: Creative Studio · Cloud Vault · Sync   |   Forge+: Motion · Scenes · Dancer · Characters · Venues · Schedule · Wheel · Roulette");
        }
        ImGui.EndChild();
        ImGui.PopStyleColor();
    }

    private bool DrawForgeMembershipGate()
    {
        var access = plugin.ForgeAccess;
        var theme = plugin.CurrentTheme;
        var active = access.HasAccess;


        var controlsDisabled = forgeMembershipActionRunning || access.IsBusy;
        var height = active ? 104f : access.HasPendingAuthorization ? 340f : 260f;

        ImGui.PushStyleColor(ImGuiCol.ChildBg, UiStyles.WithAlpha(active ? theme.Success : theme.PanelAlt, active ? 0.10f : 0.64f));
        if (ImGui.BeginChild("##reforge-membership-gate", new Vector2(0f, height), true, ImGuiWindowFlags.NoScrollbar))
        {
            ImGui.PushStyleColor(ImGuiCol.Text, active ? theme.Success : theme.AccentStrong);
            ImGui.SetWindowFontScale(active ? 1.10f : 1.24f);
            ImGui.TextUnformatted(active
                ? access.HasPlusAccess ? "RE:FORGE+ MEMBERSHIP ACTIVE" : "RE:FORGE MEMBERSHIP ACTIVE"
                : "UNLOCK THE FORGE");
            ImGui.SetWindowFontScale(1f);
            ImGui.PopStyleColor();

            if (active)
            {
                var identity = string.IsNullOrWhiteSpace(access.DiscordDisplayName)
                    ? "Discord member verified"
                    : $"Discord: {access.DiscordDisplayName}";
                ImGui.TextUnformatted(identity);
                ImGui.SameLine();
                ImGui.TextDisabled(access.HasPlusAccess ? "· Forge+ role verified" : "· Base subscriber role verified");
                ImGui.Spacing();
                if (controlsDisabled)
                    ImGui.BeginDisabled();
                if (ImGui.SmallButton("CHECK MEMBERSHIP##reforge-refresh"))
                    _ = RefreshForgeMembershipAsync();
                ImGui.SameLine();
                if (ImGui.SmallButton("DISCONNECT##reforge-disconnect"))
                    _ = DisconnectForgeMembershipAsync();
                ImGui.SameLine();
                ImGui.PushStyleColor(ImGuiCol.Button, UiStyles.WithAlpha(theme.Danger, 0.30f));
                ImGui.PushStyleColor(ImGuiCol.ButtonHovered, UiStyles.WithAlpha(theme.Danger, 0.58f));
                ImGui.PushStyleColor(ImGuiCol.Text, theme.Text);
                if (ImGui.SmallButton("CANCEL MEMBERSHIP##reforge-cancel-membership"))
                {
                    plugin.OpenExternalResource(access.ManageMembershipUrl, "Ko-fi membership management");
                    forgeStatus = "Ko-fi membership management opened. Under Account & Billing > Subscriptions, choose Don't Renew.";
                }
                if (ImGui.IsItemHovered())
                    ImGui.SetTooltip("Open Ko-fi Account & Billing to stop the membership from renewing. Current access continues until Ko-fi ends the paid period.");
                ImGui.PopStyleColor(3);
                if (controlsDisabled)
                    ImGui.EndDisabled();
            }
            else
            {
                ImGui.TextWrapped("RE:Forge is RE:Frame XIV's complete premium creative suite. A $2.99 monthly Ko-fi membership unlocks Theme Studio, Map Studio, AFK and Voice Directors, automation, Workshop sharing, Profile Fusion, Display Lab, and the Cloud Vault.");
                ImGui.Spacing();
                ImGui.TextWrapped("Subscribe on Ko-fi, connect the same Discord account used for your membership, and RE:Frame will verify the RE:Forge subscriber role before unlocking this page.");
                ImGui.Spacing();

                var hasToken = !string.IsNullOrWhiteSpace(plugin.Configuration.ForgeMembershipToken);
                var hasDiscordIdentity = !string.IsNullOrWhiteSpace(access.DiscordUserId);
                var hasPendingAuthorization = access.HasPendingAuthorization;
                var buttonWidth = MathF.Min(310f, MathF.Max(190f, (ImGui.GetContentRegionAvail().X - 12f) * 0.5f));
                if (ImGui.Button("SUBSCRIBE — $2.99 / MONTH", new Vector2(buttonWidth, 38f)))
                    plugin.OpenExternalResource(access.MembershipUrl, "RE:Forge Ko-fi membership");
                ImGui.SameLine();

                if (controlsDisabled)
                    ImGui.BeginDisabled();
                var connectLabel = !hasToken
                    ? "CONNECT DISCORD"
                    : hasPendingAuthorization
                        ? "OPEN DISCORD AUTHORIZATION"
                        : hasDiscordIdentity
                            ? "CHECK DISCORD ROLE"
                            : "RESTART DISCORD CONNECTION";
                if (ImGui.Button(connectLabel, new Vector2(buttonWidth, 38f)))
                {
                    if (!hasToken || (!hasPendingAuthorization && !hasDiscordIdentity))
                        _ = BeginForgeMembershipConnectionAsync();
                    else if (hasPendingAuthorization)
                        _ = OpenPendingForgeAuthorizationAsync();
                    else
                        _ = RefreshForgeMembershipAsync();
                }
                if (controlsDisabled)
                    ImGui.EndDisabled();

                if (hasPendingAuthorization)
                {
                    ImGui.Spacing();
                    ImGui.TextWrapped("Discord authorization is waiting in your browser. If Windows did not open it automatically, use either button below.");
                    if (controlsDisabled)
                        ImGui.BeginDisabled();
                    if (ImGui.SmallButton("OPEN AUTHORIZATION##reforge-open-pending"))
                        _ = OpenPendingForgeAuthorizationAsync();
                    ImGui.SameLine();
                    if (ImGui.SmallButton("COPY AUTHORIZATION LINK##reforge-copy-pending"))
                    {
                        ImGui.SetClipboardText(access.PendingConnectUrl);
                        forgeStatus = "Discord authorization link copied to the clipboard.";
                    }
                    ImGui.SameLine();
                    if (ImGui.SmallButton("START OVER##reforge-restart-pending"))
                        _ = RestartForgeMembershipConnectionAsync();
                    if (controlsDisabled)
                        ImGui.EndDisabled();
                }

                ImGui.Spacing();
                var accessStatus = access.Status?.Trim() ?? string.Empty;
                var actionStatus = forgeStatus?.Trim() ?? string.Empty;
                if (!string.IsNullOrWhiteSpace(accessStatus))
                    ImGui.TextWrapped(accessStatus);
                if (!string.IsNullOrWhiteSpace(actionStatus) &&
                    !string.Equals(actionStatus, accessStatus, StringComparison.Ordinal))
                    ImGui.TextDisabled(actionStatus);
            }
        }
        ImGui.EndChild();
        ImGui.PopStyleColor();
        return active;
    }

    private async Task BeginForgeMembershipConnectionAsync()
    {
        if (forgeMembershipActionRunning)
            return;

        forgeMembershipActionRunning = true;
        try
        {
            var result = await plugin.ForgeAccess.BeginLinkAsync();
            forgeStatus = result.Message;
            if (!result.Success)
                return;

            var opened = false;
            await Plugin.Framework.Run(() =>
                opened = plugin.OpenExternalResource(result.ConnectUrl, "Discord RE:Forge membership verification"));
            forgeStatus = opened
                ? "Discord authorization was sent to your default browser. The authorization link remains available below."
                : "The browser could not be opened automatically. Use Open Authorization or Copy Authorization Link below.";
            _ = PollForgeMembershipCompletionAsync();
        }
        catch (Exception ex)
        {
            forgeStatus = $"Could not connect Discord: {ex.Message}";
        }
        finally
        {
            forgeMembershipActionRunning = false;
        }
    }

    private async Task PollForgeMembershipCompletionAsync()
    {
        try
        {
            await plugin.ForgeAccess.WaitForLinkAsync(TimeSpan.FromMinutes(2));
            forgeStatus = plugin.ForgeAccess.Status;
            plugin.NativeHudVisibility.RefreshNow();
            plugin.ForgeSquareMap.Rebuild();
        }
        catch (Exception ex)
        {
            forgeStatus = $"Discord verification polling stopped: {ex.Message}";
        }
    }

    private async Task OpenPendingForgeAuthorizationAsync()
    {
        var url = plugin.ForgeAccess.PendingConnectUrl;
        if (string.IsNullOrWhiteSpace(url))
        {
            forgeStatus = "No pending Discord authorization link is available. Start the Discord connection again.";
            return;
        }

        var opened = false;
        await Plugin.Framework.Run(() =>
            opened = plugin.OpenExternalResource(url, "Discord RE:Forge membership verification"));
        forgeStatus = opened
            ? "Discord authorization was sent to your default browser."
            : "The browser could not be opened automatically. Copy the authorization link and paste it into your browser.";
    }

    private async Task RestartForgeMembershipConnectionAsync()
    {
        if (forgeMembershipActionRunning)
            return;

        forgeMembershipActionRunning = true;
        try
        {
            await plugin.ForgeAccess.DisconnectAsync();
        }
        catch
        {

        }
        finally
        {
            forgeMembershipActionRunning = false;
        }

        await BeginForgeMembershipConnectionAsync();
    }

    private async Task RefreshForgeMembershipAsync()
    {
        if (forgeMembershipActionRunning)
            return;

        forgeMembershipActionRunning = true;
        try
        {
            await plugin.ForgeAccess.RefreshAsync();
            forgeStatus = plugin.ForgeAccess.Status;
            plugin.NativeHudVisibility.RefreshNow();
            plugin.ForgeSquareMap.Rebuild();
        }
        catch (Exception ex)
        {
            forgeStatus = $"Could not check membership: {ex.Message}";
        }
        finally
        {
            forgeMembershipActionRunning = false;
        }
    }

    private async Task DisconnectForgeMembershipAsync()
    {
        if (forgeMembershipActionRunning)
            return;

        forgeMembershipActionRunning = true;
        try
        {
            await plugin.ForgeAccess.DisconnectAsync();
            forgeStatus = plugin.ForgeAccess.Status;
            plugin.NativeHudVisibility.RefreshNow();
            plugin.ForgeSquareMap.Restore();
        }
        catch (Exception ex)
        {
            forgeStatus = $"Could not disconnect membership: {ex.Message}";
        }
        finally
        {
            forgeMembershipActionRunning = false;
        }
    }

    private void DrawForgeNavigation(Configuration configuration)
    {
        UiStyles.SectionLabel("Square map", plugin.CurrentTheme);
        ImGui.SameLine();
        ImGui.TextDisabled("RE:FORGE EXCLUSIVE");
        if (plugin.ForgeAccess.IsDevelopmentPreview)
        {
            ImGui.SameLine();
            ImGui.TextDisabled("· DEVELOPMENT PREVIEW");
        }

        ImGui.TextWrapped("Replace FFXIV's circular minimap with a live square navigation surface forged directly into your RE:Frame HUD. The complete Map window remains available through your normal Map key.");
        ImGui.Spacing();

        var hasAccess = plugin.ForgeAccess.HasAccess;
        if (!hasAccess)
            ImGui.BeginDisabled();

        var enabled = configuration.ForgeSquareMinimapEnabled;
        if (ImGui.Checkbox("Forge a square minimap", ref enabled))
        {
            configuration.ForgeSquareMinimapEnabled = enabled;
            plugin.SaveConfiguration();
            plugin.NativeHudVisibility.RefreshNow();
            if (enabled)
                plugin.ForgeSquareMap.Rebuild();
            else
                plugin.ForgeSquareMap.Restore();
            forgeStatus = enabled
                ? "The square navigation surface is being rebuilt from a clean native Map window."
                : "The square navigation surface was released; the circular minimap will return.";
        }

        if (configuration.ForgeSquareMinimapEnabled)
        {
            ImGui.Indent(18f);
            var followPlayer = configuration.ForgeSquareMinimapFollowPlayer;
            if (ImGui.Checkbox("Keep the map centered on me", ref followPlayer))
            {
                configuration.ForgeSquareMinimapFollowPlayer = followPlayer;
                plugin.SaveConfiguration();
                plugin.ForgeSquareMap.RefreshNow();
            }

            var mapZoom = configuration.ForgeSquareMinimapZoom;
            ImGui.SetNextItemWidth(MathF.Min(320f, ImGui.GetContentRegionAvail().X));
            if (ImGui.SliderFloat("Minimap zoom##forge-square-map-zoom", ref mapZoom, 0.50f, 4.00f, "%.2fx"))
            {
                configuration.ForgeSquareMinimapZoom = mapZoom;
                plugin.SaveConfiguration();
                plugin.ForgeSquareMap.RefreshNow();
            }
            if (ImGui.IsItemHovered())
                ImGui.SetTooltip("Magnifies the ordinary native Map beneath a fixed square crop. 1.00x is exactly the scale at which FFXIV opened it.");

            var overscan = configuration.ForgeSquareMinimapOverscan;
            ImGui.SetNextItemWidth(MathF.Min(320f, ImGui.GetContentRegionAvail().X));
            if (ImGui.SliderFloat("Map crop##forge-square-map-overscan", ref overscan, 0.90f, 1.18f, "%.2fx"))
            {
                configuration.ForgeSquareMinimapOverscan = overscan;
                plugin.SaveConfiguration();
                plugin.ForgeSquareMap.RefreshNow();
            }
            if (ImGui.IsItemHovered())
                ImGui.SetTooltip("Slightly enlarges the normal native Map behind the frame without fitting the full zone into the square.");

            ImGui.TextDisabled("Map coordinates remain an independent Edit HUD element. Press your normal Map key to expand the complete native Map window.");
            ImGui.TextWrapped(plugin.ForgeSquareMap.Status);
            ImGui.Spacing();

            var prerequisitesReady =
                configuration.ReplaceNativeHud &&
                configuration.ShowHudOverlay &&
                configuration.ShowMinimapFrame;
            if (!prerequisitesReady)
            {
                ImGui.PushStyleColor(ImGuiCol.Text, plugin.CurrentTheme.Warning);
                ImGui.TextWrapped("Square map prerequisites are disabled. RE:Frame HUD replacement, the HUD overlay, and the minimap frame must all be enabled.");
                ImGui.PopStyleColor();
            }

            if (ImGui.Button("REBUILD SQUARE MAP##forge-square-map-rebuild", new Vector2(230f, 34f)))
            {
                plugin.NativeHudVisibility.RefreshNow();
                plugin.ForgeSquareMap.Rebuild();
                forgeStatus = "The square map was released and rebuilt from a fresh native Map window.";
            }
            if (ImGui.IsItemHovered())
                ImGui.SetTooltip("Closes RE:Frame's embedded Map safely, restores its native state, then acquires a fresh surface.");

            ImGui.SameLine();
            if (ImGui.Button("OPEN FULL MAP##forge-square-map-open-full", new Vector2(190f, 34f)))
            {
                plugin.ForgeSquareMap.OpenFullMap();
                forgeStatus = "Opening the complete native Map by explicit request.";
            }
            if (ImGui.IsItemHovered())
                ImGui.SetTooltip("Explicitly opens the full native Map without relying on a hidden-addon transition.");

            ImGui.Unindent(18f);
        }

        if (!hasAccess)
            ImGui.EndDisabled();
    }

    private void DrawForgeLibrary(Configuration configuration)
    {
        UiStyles.SectionLabel("Your Armory", plugin.CurrentTheme);
        ImGui.TextDisabled($"{configuration.ForgeThemes.Count} custom theme{(configuration.ForgeThemes.Count == 1 ? string.Empty : "s")}");
        ImGui.Spacing();

        foreach (var theme in configuration.ForgeThemes.OrderBy(item => item.Name, StringComparer.OrdinalIgnoreCase))
        {
            var selected = string.Equals(forgeSelectedThemeId, theme.Id, StringComparison.OrdinalIgnoreCase);
            var active = configuration.UseForgeTheme &&
                         string.Equals(configuration.ActiveForgeThemeId, theme.Id, StringComparison.OrdinalIgnoreCase);

            var label = active ? $"◆ {theme.Name}" : theme.Name;
            if (ImGui.Selectable($"{label}##forge-theme-{theme.Id}", selected))
            {
                forgeSelectedThemeId = theme.Id;
                forgeExportCode = string.Empty;
                forgeStatus = active ? "This theme is currently forged into your HUD." : string.Empty;
            }
        }

        ImGui.Spacing();
        UiStyles.Divider(plugin.CurrentTheme);
        ImGui.Spacing();
        ImGui.TextWrapped("Create from the look currently shown in RE:Frame.");
        ImGui.SetNextItemWidth(-1f);
        ImGui.InputText("##forge-new-name", ref forgeNewThemeName, 49);
        if (ImGui.Button("+ CREATE NEW FORGE", new Vector2(-1f, 36f)))
        {
            var name = string.IsNullOrWhiteSpace(forgeNewThemeName) ? "My Forge" : forgeNewThemeName.Trim();
            var sourcePreset = configuration.SelectedTheme;
            var created = ForgeThemeDefinition.FromPalette(name, plugin.CurrentTheme, sourcePreset);
            created.Style = plugin.CurrentThemeStyle.Clone();
            configuration.ForgeThemes.Add(created);
            ForgeThemeLibrary.Activate(configuration, created);
            forgeSelectedThemeId = created.Id;
            forgeNewThemeName = "My Forge";
            forgeExportCode = string.Empty;
            forgeStatus = $"{created.Name} was created and applied.";
            plugin.SaveConfiguration();
        }

        ImGui.Spacing();
        UiStyles.Divider(plugin.CurrentTheme);
        ImGui.Spacing();
        UiStyles.SectionLabel("Import", plugin.CurrentTheme);
        ImGui.TextDisabled("Paste a REFORGE1 theme code.");
        ImGui.InputTextMultiline("##forge-import-code", ref forgeImportCode, 65536, new Vector2(-1f, 72f));
        if (ImGui.Button("IMPORT THEME", new Vector2(-1f, 34f)))
        {
            if (ForgeThemeCodec.TryDecode(forgeImportCode, out var imported, out var error) && imported is not null)
            {
                configuration.ForgeThemes.Add(imported);
                ForgeThemeLibrary.Activate(configuration, imported);
                forgeSelectedThemeId = imported.Id;
                forgeImportCode = string.Empty;
                forgeExportCode = string.Empty;
                forgeStatus = $"{imported.Name} was imported and applied.";
                plugin.SaveConfiguration();
            }
            else
            {
                forgeStatus = error;
            }
        }
    }

    private void DrawForgeEmptyState(Configuration configuration)
    {
        UiStyles.SectionLabel("The Anvil", plugin.CurrentTheme);
        ImGui.Spacing();
        ImGui.TextWrapped(configuration.ForgeThemes.Count == 0
            ? "Your Armory is empty. Create a Forge from your current RE:Frame look, then shape every part of it here."
            : "Choose a theme from Your Armory to place it on The Anvil.");

        ImGui.Spacing();
        DrawForgePreview(plugin.CurrentTheme, plugin.CurrentThemeStyle, "CURRENT RE:FRAME LOOK");
    }

    private void DrawForgeEditor(Configuration configuration, ForgeThemeDefinition theme)
    {
        var changed = false;
        var active = configuration.UseForgeTheme &&
                     string.Equals(configuration.ActiveForgeThemeId, theme.Id, StringComparison.OrdinalIgnoreCase);

        UiStyles.SectionLabel("The Anvil", theme.ToPalette());
        ImGui.SameLine();
        ImGui.TextDisabled(active ? "◆ APPLIED LIVE" : "PREVIEWING");

        var name = theme.Name;
        ImGui.SetNextItemWidth(-1f);
        if (ImGui.InputText("##forge-theme-name", ref name, 49))
        {
            theme.Name = name;
            changed = true;
        }

        DrawForgePreview(theme.ToPalette(), theme.Style, theme.Name);

        if (!active)
        {
            if (ImGui.Button("FORGE INTO MY HUD", new Vector2(210f, 38f)))
            {
                ForgeThemeLibrary.Activate(configuration, theme);
                forgeStatus = $"{theme.Name} is now forged into your HUD.";
                changed = true;
                active = true;
            }
        }
        else
        {
            ImGui.PushStyleColor(ImGuiCol.Button, UiStyles.WithAlpha(theme.Success, 0.30f));
            ImGui.Button("◆ FORGED LIVE", new Vector2(210f, 38f));
            ImGui.PopStyleColor();
            ImGui.SameLine();
            if (ImGui.Button("RETURN TO BUILT-IN THEME", new Vector2(220f, 38f)))
            {
                configuration.UseForgeTheme = false;
                configuration.ActiveForgeThemeId = string.Empty;
                forgeStatus = "Returned to the built-in RE:Frame theme system.";
                changed = true;
                active = false;
            }
        }

        ImGui.Spacing();
        UiStyles.Divider(theme.ToPalette());
        ImGui.Spacing();

        changed |= DrawForgePaletteEditor(theme);
        ImGui.Spacing();
        UiStyles.Divider(theme.ToPalette());
        ImGui.Spacing();
        changed |= DrawForgeSurfaceEditor(theme);
        ImGui.Spacing();
        UiStyles.Divider(theme.ToPalette());
        ImGui.Spacing();
        changed |= DrawForgeActions(configuration, theme);

        if (!string.IsNullOrWhiteSpace(forgeStatus))
        {
            ImGui.Spacing();
            ImGui.TextWrapped(forgeStatus);
        }

        if (changed)
        {
            theme.ModifiedUtc = DateTime.UtcNow;
            theme.Normalize();
            plugin.SaveConfiguration();
        }
    }

    private bool DrawForgePaletteEditor(ForgeThemeDefinition theme)
    {
        var changed = false;
        UiStyles.SectionLabel("Color & Light", theme.ToPalette());
        ImGui.TextDisabled("Every color is applied live. Use the picker, drag RGBA channels, or type #RRGGBB / #RRGGBBAA directly.");

        changed |= DrawForgeColor("Accent", "accent", theme.Accent, value => theme.Accent = value);
        changed |= DrawForgeColor("Accent bridge", "accent-mid", theme.AccentMid, value => theme.AccentMid = value);
        changed |= DrawForgeColor("Highlight", "accent-strong", theme.AccentStrong, value => theme.AccentStrong = value);

        ImGui.Spacing();
        changed |= DrawForgeColor("Primary panel", "panel", theme.Panel, value => theme.Panel = value);
        changed |= DrawForgeColor("Secondary panel", "panel-alt", theme.PanelAlt, value => theme.PanelAlt = value);
        changed |= DrawForgeColor("Text", "text", theme.Text, value => theme.Text = value);
        changed |= DrawForgeColor("Muted text", "muted", theme.Muted, value => theme.Muted = value);

        ImGui.Spacing();
        changed |= DrawForgeColor("Success", "success", theme.Success, value => theme.Success = value);
        changed |= DrawForgeColor("Warning", "warning", theme.Warning, value => theme.Warning = value);
        changed |= DrawForgeColor("Danger", "danger", theme.Danger, value => theme.Danger = value);

        ImGui.Spacing();
        UiStyles.SectionLabel("Signature Gradient", theme.ToPalette());
        changed |= DrawForgeColor("Gradient start", "gradient-start", theme.GradientStart, value => theme.GradientStart = value);
        changed |= DrawForgeColor("Gradient middle", "gradient-mid", theme.GradientMid, value => theme.GradientMid = value);
        changed |= DrawForgeColor("Gradient finish", "gradient-end", theme.GradientEnd, value => theme.GradientEnd = value);

        ImGui.Spacing();
        UiStyles.SectionLabel("Controls", theme.ToPalette());
        ImGui.TextDisabled("Forge the actual input and button states used throughout RE:Frame.");
        changed |= DrawForgeColor("Input field", "input", theme.Input, value => theme.Input = value);
        changed |= DrawForgeColor("Input field — hovered", "input-hovered", theme.InputHovered, value => theme.InputHovered = value);
        changed |= DrawForgeColor("Input field — active", "input-active", theme.InputActive, value => theme.InputActive = value);
        changed |= DrawForgeColor("Button", "button", theme.Button, value => theme.Button = value);
        changed |= DrawForgeColor("Button — hovered", "button-hovered", theme.ButtonHovered, value => theme.ButtonHovered = value);
        changed |= DrawForgeColor("Button — active", "button-active", theme.ButtonActive, value => theme.ButtonActive = value);

        ImGui.Spacing();
        UiStyles.SectionLabel("Navigation", theme.ToPalette());
        ImGui.TextDisabled("These colors own the main RE:Frame sidebar and selected navigation states.");
        changed |= DrawForgeColor("Navigation button", "navigation", theme.Navigation, value => theme.Navigation = value);
        changed |= DrawForgeColor("Navigation button — hovered", "navigation-hovered", theme.NavigationHovered, value => theme.NavigationHovered = value);
        changed |= DrawForgeColor("Navigation button — pressed", "navigation-active", theme.NavigationActive, value => theme.NavigationActive = value);
        changed |= DrawForgeColor("Navigation button — selected", "navigation-selected", theme.NavigationSelected, value => theme.NavigationSelected = value);

        ImGui.Spacing();
        UiStyles.SectionLabel("HUD Docks", theme.ToPalette());
        ImGui.TextDisabled("Forge the dock itself, its button faces, interaction states, dividers, and labels.");
        changed |= DrawForgeColor("Dock surface", "dock", theme.Dock, value => theme.Dock = value);
        changed |= DrawForgeColor("Dock border", "dock-border", theme.DockBorder, value => theme.DockBorder = value);
        changed |= DrawForgeColor("Dock button", "dock-button", theme.DockButton, value => theme.DockButton = value);
        changed |= DrawForgeColor("Dock button — hovered", "dock-button-hovered", theme.DockButtonHovered, value => theme.DockButtonHovered = value);
        changed |= DrawForgeColor("Dock button — active", "dock-button-active", theme.DockButtonActive, value => theme.DockButtonActive = value);
        changed |= DrawForgeColor("Dock dividers", "dock-divider", theme.DockDivider, value => theme.DockDivider = value);
        changed |= DrawForgeColor("Dock text", "dock-text", theme.DockText, value => theme.DockText = value);

        var followJobs = plugin.Configuration.FollowJobColors;
        if (ImGui.Checkbox("Let the active job recolor accents and gradients", ref followJobs))
        {
            plugin.Configuration.FollowJobColors = followJobs;
            changed = true;
        }
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip("Keeps this Forge's panels, text, and surfaces while applying the current job's accent language.");

        ImGui.Spacing();
        var sourcePreset = theme.SourcePreset;
        ImGui.SetNextItemWidth(-1f);
        if (ImGui.BeginCombo("##forge-starter-preset", ThemePresetInfo.Label(sourcePreset)))
        {
            foreach (var preset in ThemePresetInfo.All)
            {
                var selected = sourcePreset == preset;
                if (ImGui.Selectable(ThemePresetInfo.Label(preset), selected))
                {
                    sourcePreset = preset;
                    changed = true;
                }
                if (selected) ImGui.SetItemDefaultFocus();
            }
            ImGui.EndCombo();
        }
        theme.SourcePreset = sourcePreset;
        if (ImGui.Button("RESET COLORS FROM THIS STARTER", new Vector2(-1f, 34f)))
        {
            theme.ResetFromPreset(sourcePreset);
            changed = true;
        }

        return changed;
    }

    private bool DrawForgeSurfaceEditor(ForgeThemeDefinition theme)
    {
        var changed = false;
        var style = theme.Style ??= ForgeStyleSettings.Default;
        UiStyles.SectionLabel("Shape & Material", theme.ToPalette());
        ImGui.TextDisabled("Start from a forged material, then refine every value below.");

        if (ImGui.Button("GLASS", new Vector2(82f, 32f)))
        {
            ApplyForgeMaterial(theme, ForgeMaterialRecipe.Glass);
            changed = true;
        }
        ImGui.SameLine();
        if (ImGui.Button("MATTE", new Vector2(82f, 32f)))
        {
            ApplyForgeMaterial(theme, ForgeMaterialRecipe.Matte);
            changed = true;
        }
        ImGui.SameLine();
        if (ImGui.Button("LACQUER", new Vector2(82f, 32f)))
        {
            ApplyForgeMaterial(theme, ForgeMaterialRecipe.Lacquer);
            changed = true;
        }
        ImGui.SameLine();
        if (ImGui.Button("NEON", new Vector2(82f, 32f)))
        {
            ApplyForgeMaterial(theme, ForgeMaterialRecipe.Neon);
            changed = true;
        }
        ImGui.SameLine();
        if (ImGui.Button("SOFT", new Vector2(82f, 32f)))
        {
            ApplyForgeMaterial(theme, ForgeMaterialRecipe.Soft);
            changed = true;
        }

        ImGui.Spacing();
        changed |= DrawForgeSlider("Window rounding", "window-rounding", style.WindowRounding, 0f, 28f, value => style.WindowRounding = value);
        changed |= DrawForgeSlider("Panel rounding", "child-rounding", style.ChildRounding, 0f, 24f, value => style.ChildRounding = value);
        changed |= DrawForgeSlider("Control rounding", "frame-rounding", style.FrameRounding, 0f, 20f, value => style.FrameRounding = value);
        changed |= DrawForgeSlider("Border light", "border-opacity", style.BorderOpacity, 0f, 1f, value => style.BorderOpacity = value);

        ImGui.Spacing();
        changed |= DrawForgeSlider("Button resting light", "button-opacity", style.ButtonOpacity, 0f, 1f, value => style.ButtonOpacity = value);
        changed |= DrawForgeSlider("Button hover light", "button-hover", style.ButtonHoverOpacity, 0f, 1f, value => style.ButtonHoverOpacity = value);
        changed |= DrawForgeSlider("Button active light", "button-active", style.ButtonActiveOpacity, 0f, 1f, value => style.ButtonActiveOpacity = value);

        ImGui.Spacing();
        changed |= DrawForgeSlider("Input resting glass", "input-opacity", style.InputOpacity, 0f, 1f, value => style.InputOpacity = value);
        changed |= DrawForgeSlider("Input hover glass", "input-hover", style.InputHoverOpacity, 0f, 1f, value => style.InputHoverOpacity = value);
        changed |= DrawForgeSlider("Input active glass", "input-active", style.InputActiveOpacity, 0f, 1f, value => style.InputActiveOpacity = value);

        if (ImGui.Button("RESET MATERIAL", new Vector2(-1f, 34f)))
        {
            theme.Style = ForgeStyleSettings.Default;
            changed = true;
        }

        return changed;
    }

    private static void ApplyForgeMaterial(ForgeThemeDefinition theme, ForgeMaterialRecipe recipe)
    {
        var style = theme.Style ??= ForgeStyleSettings.Default;
        switch (recipe)
        {
            case ForgeMaterialRecipe.Matte:
                theme.Panel = SetAlpha(theme.Panel, 0.99f);
                theme.PanelAlt = SetAlpha(theme.PanelAlt, 0.96f);
                style.WindowRounding = 8f;
                style.ChildRounding = 7f;
                style.FrameRounding = 5f;
                style.BorderOpacity = 0.24f;
                style.ButtonOpacity = 0.38f;
                style.ButtonHoverOpacity = 0.48f;
                style.ButtonActiveOpacity = 0.58f;
                style.InputOpacity = 0.31f;
                style.InputHoverOpacity = 0.38f;
                style.InputActiveOpacity = 0.45f;
                break;

            case ForgeMaterialRecipe.Lacquer:
                theme.Panel = SetAlpha(theme.Panel, 0.96f);
                theme.PanelAlt = SetAlpha(theme.PanelAlt, 0.91f);
                style.WindowRounding = 12f;
                style.ChildRounding = 9f;
                style.FrameRounding = 6f;
                style.BorderOpacity = 0.62f;
                style.ButtonOpacity = 0.23f;
                style.ButtonHoverOpacity = 0.39f;
                style.ButtonActiveOpacity = 0.54f;
                style.InputOpacity = 0.17f;
                style.InputHoverOpacity = 0.25f;
                style.InputActiveOpacity = 0.34f;
                break;

            case ForgeMaterialRecipe.Neon:
                theme.Panel = SetAlpha(theme.Panel, 0.89f);
                theme.PanelAlt = SetAlpha(theme.PanelAlt, 0.82f);
                style.WindowRounding = 5f;
                style.ChildRounding = 4f;
                style.FrameRounding = 3f;
                style.BorderOpacity = 0.88f;
                style.ButtonOpacity = 0.34f;
                style.ButtonHoverOpacity = 0.58f;
                style.ButtonActiveOpacity = 0.74f;
                style.InputOpacity = 0.18f;
                style.InputHoverOpacity = 0.30f;
                style.InputActiveOpacity = 0.42f;
                break;

            case ForgeMaterialRecipe.Soft:
                theme.Panel = SetAlpha(theme.Panel, 0.91f);
                theme.PanelAlt = SetAlpha(theme.PanelAlt, 0.84f);
                style.WindowRounding = 22f;
                style.ChildRounding = 18f;
                style.FrameRounding = 14f;
                style.BorderOpacity = 0.26f;
                style.ButtonOpacity = 0.24f;
                style.ButtonHoverOpacity = 0.34f;
                style.ButtonActiveOpacity = 0.46f;
                style.InputOpacity = 0.22f;
                style.InputHoverOpacity = 0.29f;
                style.InputActiveOpacity = 0.36f;
                break;

            default:
                theme.Panel = SetAlpha(theme.Panel, 0.83f);
                theme.PanelAlt = SetAlpha(theme.PanelAlt, 0.76f);
                style.WindowRounding = 14f;
                style.ChildRounding = 11f;
                style.FrameRounding = 8f;
                style.BorderOpacity = 0.46f;
                style.ButtonOpacity = 0.28f;
                style.ButtonHoverOpacity = 0.42f;
                style.ButtonActiveOpacity = 0.52f;
                style.InputOpacity = 0.22f;
                style.InputHoverOpacity = 0.29f;
                style.InputActiveOpacity = 0.36f;
                break;
        }

        theme.ModifiedUtc = DateTime.UtcNow;
        theme.Normalize();
    }

    private static Vector4 SetAlpha(Vector4 color, float alpha) =>
        new(color.X, color.Y, color.Z, Math.Clamp(alpha, 0f, 1f));

    private bool DrawForgeActions(Configuration configuration, ForgeThemeDefinition theme)
    {
        var changed = false;
        UiStyles.SectionLabel("Archive & Share", theme.ToPalette());

        if (ImGui.Button("DUPLICATE", new Vector2(132f, 34f)))
        {
            var clone = theme.Clone();
            configuration.ForgeThemes.Add(clone);
            forgeSelectedThemeId = clone.Id;
            forgeExportCode = string.Empty;
            forgeStatus = $"Created {clone.Name}.";
            changed = true;
        }
        ImGui.SameLine();
        if (ImGui.Button("EXPORT CODE", new Vector2(132f, 34f)))
        {
            forgeExportCode = ForgeThemeCodec.Encode(theme);
            ImGui.SetClipboardText(forgeExportCode);
            forgeStatus = "The REFORGE1 code was copied to your clipboard.";
        }
        ImGui.SameLine();
        ImGui.PushStyleColor(ImGuiCol.Button, UiStyles.WithAlpha(theme.Danger, 0.28f));
        if (ImGui.Button("DELETE", new Vector2(112f, 34f)))
            forgeDeleteThemeId = theme.Id;
        ImGui.PopStyleColor();

        if (string.Equals(forgeDeleteThemeId, theme.Id, StringComparison.OrdinalIgnoreCase))
        {
            ImGui.Spacing();
            ImGui.TextWrapped($"Delete {theme.Name}? This cannot be undone unless you exported the theme first.");
            ImGui.PushStyleColor(ImGuiCol.Button, UiStyles.WithAlpha(theme.Danger, 0.38f));
            if (ImGui.Button("DELETE PERMANENTLY", new Vector2(190f, 34f)))
            {
                var wasActive = configuration.UseForgeTheme &&
                                string.Equals(configuration.ActiveForgeThemeId, theme.Id, StringComparison.OrdinalIgnoreCase);
                configuration.ForgeThemes.Remove(theme);
                if (wasActive)
                {
                    configuration.UseForgeTheme = false;
                    configuration.ActiveForgeThemeId = string.Empty;
                }
                forgeSelectedThemeId = configuration.ForgeThemes.FirstOrDefault()?.Id ?? string.Empty;
                forgeExportCode = string.Empty;
                forgeDeleteThemeId = string.Empty;
                forgeStatus = "The Forge theme was deleted.";
                plugin.SaveConfiguration();
                ImGui.PopStyleColor();
                return false;
            }
            ImGui.PopStyleColor();
            ImGui.SameLine();
            if (ImGui.Button("CANCEL", new Vector2(120f, 34f)))
                forgeDeleteThemeId = string.Empty;
        }

        if (!string.IsNullOrWhiteSpace(forgeExportCode))
        {
            ImGui.Spacing();
            ImGui.TextDisabled("Portable REFORGE1 code");
            ImGui.InputTextMultiline("##forge-export-code", ref forgeExportCode, 65536, new Vector2(-1f, 72f), ImGuiInputTextFlags.ReadOnly);
        }

        return changed;
    }

    private ForgeThemeDefinition? ResolveForgeSelection(Configuration configuration)
    {
        var selected = ForgeThemeLibrary.Find(configuration, forgeSelectedThemeId);
        if (selected is not null)
            return selected;

        selected = ForgeThemeLibrary.GetActive(configuration) ?? configuration.ForgeThemes.FirstOrDefault();
        forgeSelectedThemeId = selected?.Id ?? string.Empty;
        return selected;
    }

    private bool DrawForgeColor(string label, string id, Vector4 current, Action<Vector4> apply)
    {
        ImGui.TextUnformatted(label);

        var changed = false;
        var stateKey = $"{forgeSelectedThemeId}:{id}";
        var edited = current;
        ImGui.SetNextItemWidth(-1f);
        if (ImGui.ColorEdit4($"##forge-color-{id}", ref edited))
        {
            edited = ClampForgeColor(edited);
            apply(edited);
            current = edited;
            forgeColorHex[stateKey] = ColorToHex(edited);
            changed = true;
        }

        var canonical = ColorToHex(current);
        if (!forgeColorHex.TryGetValue(stateKey, out var hex) || string.IsNullOrWhiteSpace(hex))
            hex = canonical;

        ImGui.SetNextItemWidth(MathF.Min(280f, ImGui.GetContentRegionAvail().X));
        if (ImGui.InputText($"HEX / RGBA##forge-hex-{id}", ref hex, 24))
        {
            forgeColorHex[stateKey] = hex;
            if (TryParseForgeColor(hex, out var parsed))
            {
                apply(parsed);
                current = parsed;
                changed = true;
            }
        }
        else
        {
            forgeColorHex[stateKey] = hex;
        }

        if (ImGui.IsItemHovered())
            ImGui.SetTooltip("Type #RRGGBB, #RRGGBBAA, or R,G,B,A using 0–255 values. Alpha controls transparency.");

        return changed;
    }

    private static string ColorToHex(Vector4 color)
    {
        color = ClampForgeColor(color);
        var r = (int)MathF.Round(color.X * 255f);
        var g = (int)MathF.Round(color.Y * 255f);
        var b = (int)MathF.Round(color.Z * 255f);
        var a = (int)MathF.Round(color.W * 255f);
        return $"#{r:X2}{g:X2}{b:X2}{a:X2}";
    }

    private static bool TryParseForgeColor(string? value, out Vector4 color)
    {
        color = Vector4.Zero;
        var text = (value ?? string.Empty).Trim();
        if (text.Length == 0)
            return false;


        if (text.IndexOfAny(new[] { ',', ';', '/', ' ' }) >= 0)
        {
            var parts = text.Split(
                new[] { ',', ';', '/', ' ' },
                StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (parts.Length is not (3 or 4))
                return false;

            Span<int> channels = stackalloc int[4];
            channels[3] = 255;
            for (var index = 0; index < parts.Length; index++)
            {
                if (!int.TryParse(parts[index], NumberStyles.Integer, CultureInfo.InvariantCulture, out channels[index]) ||
                    channels[index] is < 0 or > 255)
                    return false;
            }

            color = new Vector4(
                channels[0] / 255f,
                channels[1] / 255f,
                channels[2] / 255f,
                channels[3] / 255f);
            return true;
        }

        var hex = text;
        if (hex.StartsWith('#'))
            hex = hex[1..];

        if (hex.Length is not (6 or 8) ||
            !uint.TryParse(hex, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var packed))
            return false;

        if (hex.Length == 6)
            packed = (packed << 8) | 0xFFu;

        color = new Vector4(
            ((packed >> 24) & 0xFF) / 255f,
            ((packed >> 16) & 0xFF) / 255f,
            ((packed >> 8) & 0xFF) / 255f,
            (packed & 0xFF) / 255f);
        return true;
    }

    private static Vector4 ClampForgeColor(Vector4 color) => new(
        Math.Clamp(color.X, 0f, 1f),
        Math.Clamp(color.Y, 0f, 1f),
        Math.Clamp(color.Z, 0f, 1f),
        Math.Clamp(color.W, 0f, 1f));

    private static bool DrawForgeSlider(string label, string id, float current, float minimum, float maximum, Action<float> apply)
    {
        ImGui.TextUnformatted(label);
        var edited = current;
        ImGui.SetNextItemWidth(-1f);
        if (!ImGui.SliderFloat($"##forge-slider-{id}", ref edited, minimum, maximum, "%.2f"))
            return false;

        apply(edited);
        return true;
    }

    private static void DrawForgePreview(ThemePalette palette, ForgeStyleSettings? style, string title)
    {
        style ??= ForgeStyleSettings.Default;
        var start = ImGui.GetCursorScreenPos();
        var width = MathF.Max(1f, ImGui.GetContentRegionAvail().X);
        const float height = 194f;
        var end = start + new Vector2(width, height);
        var draw = ImGui.GetWindowDrawList();

        draw.AddRectFilled(start, end, ImGui.GetColorU32(palette.Panel), style.ChildRounding);
        var gradientMin = start + new Vector2(10f, 10f);
        var gradientMax = new Vector2(end.X - 10f, start.Y + 28f);
        var gradientMid = (gradientMin.X + gradientMax.X) * 0.5f;
        draw.AddRectFilledMultiColor(
            gradientMin,
            new Vector2(gradientMid, gradientMax.Y),
            ImGui.GetColorU32(palette.GradientStart),
            ImGui.GetColorU32(palette.GradientMid),
            ImGui.GetColorU32(palette.GradientMid),
            ImGui.GetColorU32(palette.GradientStart));
        draw.AddRectFilledMultiColor(
            new Vector2(gradientMid, gradientMin.Y),
            gradientMax,
            ImGui.GetColorU32(palette.GradientMid),
            ImGui.GetColorU32(palette.GradientEnd),
            ImGui.GetColorU32(palette.GradientEnd),
            ImGui.GetColorU32(palette.GradientMid));

        var panelMin = start + new Vector2(10f, 38f);
        var panelMax = new Vector2(end.X - 10f, start.Y + 105f);
        draw.AddRectFilled(panelMin, panelMax, ImGui.GetColorU32(palette.PanelAlt), style.ChildRounding);
        draw.AddRect(panelMin, panelMax, ImGui.GetColorU32(UiStyles.WithAlpha(palette.AccentStrong, style.BorderOpacity)), style.ChildRounding, ImDrawFlags.None, 1f);
        draw.AddText(panelMin + new Vector2(12f, 10f), ImGui.GetColorU32(palette.AccentStrong), title.ToUpperInvariant());
        draw.AddText(panelMin + new Vector2(12f, 32f), ImGui.GetColorU32(palette.Text), "Live palette · controls · navigation · HUD docks");
        draw.AddText(panelMin + new Vector2(12f, 53f), ImGui.GetColorU32(palette.Muted), "RE:Frame it. Then RE:Forge it.");

        var navTop = start.Y + 114f;
        var navGap = 6f;
        var navWidth = MathF.Max(40f, (width - 20f - navGap * 2f) / 3f);
        var navHeight = 27f;
        var navColors = new[]
        {
            palette.ResolvedNavigation,
            palette.ResolvedNavigationHovered,
            palette.ResolvedNavigationSelected,
        };
        var navLabels = new[] { "IDLE", "HOVER", "SELECTED" };
        for (var index = 0; index < 3; index++)
        {
            var min = new Vector2(start.X + 10f + index * (navWidth + navGap), navTop);
            var max = min + new Vector2(navWidth, navHeight);
            draw.AddRectFilled(min, max, ImGui.GetColorU32(navColors[index]), style.FrameRounding);
            var labelSize = ImGui.CalcTextSize(navLabels[index]);
            draw.AddText(
                min + new Vector2((navWidth - labelSize.X) * 0.5f, (navHeight - labelSize.Y) * 0.5f),
                ImGui.GetColorU32(palette.Text),
                navLabels[index]);
        }

        var dockMin = new Vector2(start.X + 10f, start.Y + 150f);
        var dockMax = new Vector2(end.X - 10f, end.Y - 10f);
        draw.AddRectFilled(dockMin, dockMax, ImGui.GetColorU32(palette.ResolvedDock), style.FrameRounding);
        draw.AddRect(dockMin, dockMax, ImGui.GetColorU32(palette.ResolvedDockBorder), style.FrameRounding, ImDrawFlags.None, 1f);
        var dockSegment = (dockMax.X - dockMin.X) / 3f;
        var dockButtonColors = new[]
        {
            palette.ResolvedDockButton,
            palette.ResolvedDockButtonHovered,
            palette.ResolvedDockButtonActive,
        };
        for (var index = 0; index < 3; index++)
        {
            var min = new Vector2(dockMin.X + dockSegment * index + 3f, dockMin.Y + 3f);
            var max = new Vector2(dockMin.X + dockSegment * (index + 1) - 3f, dockMax.Y - 3f);
            draw.AddRectFilled(min, max, ImGui.GetColorU32(dockButtonColors[index]), MathF.Max(1f, style.FrameRounding - 2f));
            if (index > 0)
            {
                draw.AddLine(
                    new Vector2(dockMin.X + dockSegment * index, dockMin.Y + 4f),
                    new Vector2(dockMin.X + dockSegment * index, dockMax.Y - 4f),
                    ImGui.GetColorU32(palette.ResolvedDockDivider),
                    1f);
            }
            var dockLabel = index == 0 ? "REST" : index == 1 ? "HOVER" : "ACTIVE";
            var labelSize = ImGui.CalcTextSize(dockLabel);
            draw.AddText(
                min + new Vector2((max.X - min.X - labelSize.X) * 0.5f, (max.Y - min.Y - labelSize.Y) * 0.5f),
                ImGui.GetColorU32(palette.ResolvedDockText),
                dockLabel);
        }

        ImGui.Dummy(new Vector2(width, height + 8f));
    }

    private enum ForgeMaterialRecipe
    {
        Glass,
        Matte,
        Lacquer,
        Neon,
        Soft,
    }

    private enum ForgePage
    {
        Overview,
        Create,
        Immersion,
        ForgePlus,
        Automation,
        Share,
        Vault,
        Membership,
    }

}
