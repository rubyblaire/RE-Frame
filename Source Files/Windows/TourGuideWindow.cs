using System;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Windowing;
using REFrameXIV.Models;
using REFrameXIV.Localization;
using REFrameXIV.Theme;
using REFrameXIV.UI;

namespace REFrameXIV.Windows;


public sealed class TourGuideWindow : Window, IDisposable
{
    private readonly Plugin plugin;
    private int page;
    private string displayFitStatus = string.Empty;
    private const int LastPage = 7;

    public TourGuideWindow(Plugin plugin)
        : base("Welcome to RE:Frame XIV###REFrameTourGuide", ImGuiWindowFlags.NoCollapse)
    {
        this.plugin = plugin;
        Size = new Vector2(900f, 690f);
        SizeCondition = ImGuiCond.FirstUseEver;
        SizeConstraints = new WindowSizeConstraints
        {
            MinimumSize = new Vector2(700f, 560f),
            MaximumSize = new Vector2(float.MaxValue, float.MaxValue),
        };
        AllowBackgroundBlur = true;
    }

    public void OpenTour(bool markSeen = false)
    {
        if (markSeen && !plugin.Configuration.TourGuideSeen)
        {


            plugin.Configuration.TourGuideSeen = true;
            plugin.SaveConfiguration();
        }

        page = 0;
        displayFitStatus = string.Empty;
        IsOpen = true;
        BringToFront();
    }


    public void ResumeTour()
    {
        IsOpen = true;
        BringToFront();
    }

    public override void PreDraw() => UiStyles.PushWindowStyle(plugin.CurrentTheme, plugin.CurrentThemeStyle);
    public override void PostDraw() => UiStyles.PopWindowStyle();

    public override void Draw()
    {
        DrawHeader();
        ImGui.Spacing();
        UiStyles.Divider(plugin.CurrentTheme);
        ImGui.Spacing();

        var footerHeight = 64f;
        if (ImGui.BeginChild("##reframe-tour-content", new Vector2(0f, -footerHeight), false))
        {
            switch (page)
            {
                case 0: DrawWelcome(); break;
                case 1: DrawHowItWorks(); break;
                case 2: DrawMakeItYours(); break;
                case 3: DrawDockTour(); break;
                case 4: DrawRoleplayDock(); break;
                case 5: DrawEverydayTools(); break;
                case 6: DrawGreetingsAndAfkScreen(); break;
                default: DrawReady(); break;
            }
        }
        ImGui.EndChild();

        UiStyles.Divider(plugin.CurrentTheme);
        ImGui.Spacing();
        DrawNavigation();
    }

    private void DrawHeader()
    {
        ImGui.SetWindowFontScale(1.30f);
        ImGui.TextUnformatted(PageTitle());
        ImGui.SetWindowFontScale(1f);
        ImGui.TextDisabled(Localizer.Format("tour.stop", "Tour stop {0} of {1}", page + 1, LastPage + 1));
        ImGui.ProgressBar((page + 1f) / (LastPage + 1f), new Vector2(-1f, 5f), string.Empty);
    }

    private string PageTitle() => page switch
    {
        0 => L("tour.title.welcome", "Welcome to RE:Frame XIV"),
        1 => L("tour.title.modes", "One HUD, Several Ways to Play"),
        2 => L("tour.title.layout", "Make the Interface Yours"),
        3 => L("tour.title.docks", "Meet the Docks"),
        4 => L("tour.title.roleplay", "The Roleplay Dock"),
        5 => L("tour.title.tools", "Your Everyday Tools"),
        6 => L("tour.title.greetings", "Greetings & the AFK Screen"),
        _ => L("tour.title.ready", "You’re Ready to Re:Frame"),
    };

    private void DrawWelcome()
    {
        ImGui.TextWrapped(L("tour.welcome.body", "Thank you for trying RE:Frame XIV. Truly. This project was made to give Final Fantasy XIV a cleaner, more adaptive interface without taking the personality out of your screen."));
        ImGui.Spacing();
        Section(L("tour.welcome.section", "This is a tour, not a setup checklist"));
        ImGui.TextWrapped(L("tour.welcome.explain", "Nothing here will overwrite your current choices. We’ll simply show you where the important tools live, how the adaptive HUD works, and how to shape every dock around the way you play."));
        ImGui.Spacing();
        Callout(L("tour.welcome.callout", "The most important command to remember is /ref edit. That opens Layout Studio, where RE:Frame becomes your own."));
    }

    private void DrawHowItWorks()
    {
        ImGui.TextWrapped(L("tour.modes.body", "RE:Frame uses one authoritative HUD canvas, then gives each activity its own saved presentation. A frame can be visible, hidden, moved, resized, or locked differently in every dock."));
        ImGui.Spacing();
        Section(L("tour.modes.section", "Automatic and manual modes"));
        Bullet(L("tour.modes.auto", "Automatic switches between Leisure, Quest, Raid, and Work as your activity changes."));
        Bullet(L("tour.modes.roleplay", "Roleplay is selected manually so your social layout stays exactly where you put it."));
        Bullet(L("tour.modes.manual", "A manual dock remains active until you return to Automatic."));
        ImGui.Spacing();
        Section(L("tour.modes.recovery.section", "Your FFXIV UI is still recoverable"));
        ImGui.TextWrapped(L("tour.modes.recovery.body", "RE:Frame owns supported interface pieces modularly. The Interface page lets you decide what RE:Frame replaces, and /ref restore returns the original FFXIV HUD whenever you need it."));
    }

    private void DrawMakeItYours()
    {
        Section(L("tour.layout.open.section", "Open Layout Studio"));
        Callout(L("tour.layout.open.callout", "Type /ref edit"));
        Bullet(L("tour.layout.mode", "Choose Leisure, Roleplay, Quest, Raid, or Work at the top of the editor."));
        Bullet(L("tour.layout.drag", "Drag and resize frames directly on the HUD canvas."));
        Bullet(L("tour.layout.panel", "Use the side panel to show, hide, lock, reset, or copy elements between docks."));
        Bullet(L("tour.layout.fit.tip", "Use Display Fit after installation, a resolution change, or moving FFXIV to another monitor."));
        ImGui.Spacing();

        Section(L("tour.layout.fit.section", "Fit RE:Frame to this display"));
        var viewport = HudCanvas.Current().Size;
        var fitWidth = Math.Max(1, (int)MathF.Round(viewport.X));
        var fitHeight = Math.Max(1, (int)MathF.Round(viewport.Y));
        ImGui.TextWrapped(L("tour.layout.fit.body", "Display Fit uses RE:Frame’s authoritative HUD canvas to preserve practical frame sizes and screen anchoring on this FFXIV window. It does not import another player’s monitor or resolution settings."));
        ImGui.Spacing();
        if (ImGui.Button(LF("tour.layout.fit.button", "DISPLAY FIT · {0} × {1}", fitWidth, fitHeight), new Vector2(250f, 38f)))
        {
            plugin.FitHudToViewport(viewport);
            displayFitStatus = LF("tour.layout.fit.status", "Display Fit applied to {0} × {1}.", fitWidth, fitHeight);
        }
        if (!string.IsNullOrWhiteSpace(displayFitStatus))
            ImGui.TextDisabled(displayFitStatus);

        ImGui.Spacing();
        Section(L("tour.layout.bars.section", "Edit actions and keybinds separately"));
        ImGui.TextWrapped(L("tour.layout.bars.body", "Use /ref bars to arrange action slots, and /ref keybinds to change RE:Frame’s native-backed hotbar controls. Layout Studio and the bar editor stay separate so their input surfaces never fight each other."));
        ImGui.Spacing();
        if (ImGui.Button(L("tour.layout.open.button", "OPEN LAYOUT STUDIO NOW"), new Vector2(250f, 38f)))
            plugin.OpenHudEditorFromTour();
    }

    private void DrawDockTour()
    {
        DockCard(L("mode.leisure", "Leisure").ToUpperInvariant(), L("tour.dock.leisure.description", "A calm everyday layout for exploration, travel, appearance tools, and casual play."), UiMode.Leisure);
        DockCard(L("mode.roleplay", "Roleplay").ToUpperInvariant(), L("tour.dock.roleplay.description", "A social layout centered on chat channels, emotes, Scenekeeper, and quick dock switching."), UiMode.Roleplay);
        DockCard(L("mode.quest", "Quest").ToUpperInvariant(), L("tour.dock.quest.description", "An adventuring layout that keeps combat information, objectives, quests, and FATE priority in view."), UiMode.Quest);
        DockCard(L("mode.raid", "Raid").ToUpperInvariant(), L("tour.dock.raid.description", "A duty-ready layout with party information, raid utilities, consumables, and encounter tools."), UiMode.RaidReady);
        DockCard(L("mode.work", "Work").ToUpperInvariant(), L("tour.dock.work.description", "A crafting and gathering layout with CP/GP, resources, workstation tools, and work buffs."), UiMode.Work);
        ImGui.Spacing();
        ImGui.TextDisabled(L("tour.dock.footer", "Selecting a dock here changes the live HUD. You can return to Automatic from any dock switcher or with /ref auto."));
    }

    private void DrawRoleplayDock()
    {
        ImGui.TextWrapped(L("tour.roleplay.body", "The Roleplay Dock gives social play its own uncluttered home. Its default profile keeps chat and navigation visible while muting the combat stack; every piece remains editable in /ref edit."));
        ImGui.Spacing();
        Section(L("tour.roleplay.chat.section", "CHAT"));
        ImGui.TextWrapped(L("tour.roleplay.chat.body", "Quickly switch the active chat channel between Say, Party, Free Company, and Linkshell 1."));
        Section(L("tour.roleplay.emotes.section", "EMOTES"));
        ImGui.TextWrapped(L("tour.roleplay.emotes.body", "Opens FFXIV’s native Emote list so your full collection remains available."));
        Section(L("tour.roleplay.scenes.section", "SCENES"));
        ImGui.TextWrapped(L("tour.roleplay.scenes.body", "Opens Scenekeeper through its configured slash command. The default is /scenekeeper, and it can be changed on the Integrations page if your installation uses something different."));
        Section(L("tour.roleplay.docks.section", "DOCKS"));
        ImGui.TextWrapped(L("tour.roleplay.docks.body", "Move directly to Leisure, Quest, Raid, Work, or Automatic without leaving the HUD."));
        ImGui.Spacing();
        if (ImGui.Button(L("tour.roleplay.use", "USE ROLEPLAY DOCK"), new Vector2(220f, 38f)))
            plugin.SetMode(UiMode.Roleplay);
        ImGui.SameLine();
        if (ImGui.Button(L("tour.roleplay.integrations", "OPEN INTEGRATIONS"), new Vector2(220f, 38f)))
        {
            IsOpen = false;
            plugin.OpenIntegrationsPage();
        }
    }

    private void DrawEverydayTools()
    {
        Section(L("tour.tools.command.section", "Command Center"));
        ImGui.TextWrapped(L("tour.tools.command.body", "The COMMAND segment and /ref command open a searchable palette for native FFXIV windows, RE:Frame features, modes, and installed integrations."));
        Section(L("tour.tools.pocket.section", "Job Ribbon and Pocket"));
        ImGui.TextWrapped(L("tour.tools.pocket.body", "The Job Ribbon opens saved gearsets. Pocket keeps mounts, minions, fashion accessories, and other everyday collections close without crowding the main dock."));
        Section(L("tour.tools.main.section", "Main window"));
        ImGui.TextWrapped(L("tour.tools.main.body", "Use /ref to open Docks, Job Presets, Twin Arc, Integrations, Interface ownership, HUD recovery, and Settings."));
        Section(L("tour.tools.recovery.section", "Safe recovery"));
        ImGui.TextWrapped(L("tour.tools.recovery.body", "Use /ref refresh if the presentation needs to be redrawn, /ref restore to return the original FFXIV HUD, and the HUD Safety page for layout recovery snapshots."));
    }

    private void DrawGreetingsAndAfkScreen()
    {
        ImGui.TextWrapped(L("tour.greeting.intro", "RE:Frame has two softer presentation features for the moments when you arrive in Eorzea—and when you step away for a while."));
        ImGui.Spacing();

        Section(L("tour.greeting.section", "A welcome that knows the time"));
        ImGui.TextWrapped(L("tour.greeting.body", "In Leisure mode, the Greeting frame can welcome your current character with Good Morning, Good Afternoon, or Good Evening using your computer’s local time."));
        Bullet(L("tour.greeting.move", "Move and resize the Greeting frame through /ref edit, just like the rest of the HUD."));
        Bullet(L("tour.greeting.settings", "Settings controls whether it appears and how loud its voice is."));
        Bullet(L("tour.greeting.packs", "Integrations is where you can install, select, and manage greeting voice packs."));
        ImGui.Spacing();
        if (ImGui.Button(L("tour.greeting.preview", "PREVIEW GREETING"), new Vector2(220f, 38f)))
            plugin.PreviewLoginGreeting();

        ImGui.Spacing();
        Section(L("tour.afk.section", "A screensaver for quiet moments"));
        ImGui.TextWrapped(L("tour.afk.body", "When enabled, the AFK screen becomes a full-screen presentation after your chosen inactivity delay. It waits until you are safely logged in, out of combat and cutscenes, not editing the HUD, and FFXIV is the foreground window."));
        Bullet(L("tour.afk.dismiss", "Keyboard, mouse, controller, or character movement dismisses it and returns you to the HUD."));
        Bullet(L("tour.afk.settings", "Settings controls the inactivity delay, AFK audio, and volume."));
        ImGui.Spacing();
        if (ImGui.Button(L("tour.afk.preview", "PREVIEW AFK SCREEN"), new Vector2(220f, 38f)))
            plugin.ToggleAfkScreenPreview();
        ImGui.SameLine();
        ImGui.TextDisabled(L("tour.afk.return", "Move the mouse or press a key to return."));
    }

    private void DrawReady()
    {
        ImGui.TextWrapped(L("tour.ready.body", "That’s the whole philosophy: RE:Frame adapts to what you’re doing, but you remain the designer. Thank you for letting it become part of your Eorzea."));
        ImGui.Spacing();
        Callout(L("tour.ready.callout", "Start with /ref edit, choose a dock, and move one frame. The rest will make sense the moment the HUD starts feeling like yours."));
        ImGui.Spacing();
        if (ImGui.Button(L("tour.ready.start", "START RE:FRAMING"), new Vector2(230f, 42f)))
        {
            plugin.Configuration.TourGuideSeen = true;
            plugin.SaveConfiguration();
            IsOpen = false;
            plugin.SetHudEditMode(true);
        }
        ImGui.SameLine();
        if (ImGui.Button(L("tour.ready.command", "OPEN COMMAND CENTER"), new Vector2(230f, 42f)))
        {
            plugin.Configuration.TourGuideSeen = true;
            plugin.SaveConfiguration();
            IsOpen = false;
            plugin.OpenMainUi();
        }
    }

    private void DrawNavigation()
    {
        if (page > 0)
        {
            if (ImGui.Button(L("tour.nav.back", "BACK"), new Vector2(120f, 36f)))
                page--;
            ImGui.SameLine();
        }

        if (ImGui.Button(L("tour.nav.close", "CLOSE TOUR"), new Vector2(140f, 36f)))
        {
            plugin.Configuration.TourGuideSeen = true;
            plugin.SaveConfiguration();
            IsOpen = false;
        }

        if (page < LastPage)
        {
            var width = 140f;
            ImGui.SameLine(ImGui.GetWindowWidth() - width - 24f);
            if (ImGui.Button(L("tour.nav.next", "NEXT"), new Vector2(width, 36f)))
                page++;
        }
    }

    private void DockCard(string title, string text, UiMode mode)
    {
        var active = plugin.Configuration.ModeOverride == mode;
        ImGui.PushStyleColor(ImGuiCol.ChildBg, UiStyles.WithAlpha(plugin.CurrentTheme.PanelAlt, active ? 0.70f : 0.42f));
        if (ImGui.BeginChild($"##tour-dock-{mode}", new Vector2(0f, 76f), true, ImGuiWindowFlags.NoScrollbar))
        {
            const float buttonWidth = 94f;
            var buttonX = ImGui.GetWindowWidth() - buttonWidth - 24f;

            ImGui.TextUnformatted(title);
            ImGui.PushStyleColor(ImGuiCol.Text, plugin.CurrentTheme.Muted);
            ImGui.PushTextWrapPos(MathF.Max(ImGui.GetCursorPosX() + 180f, buttonX - 14f));
            ImGui.TextWrapped(text);
            ImGui.PopTextWrapPos();
            ImGui.PopStyleColor();

            ImGui.SetCursorPos(new Vector2(buttonX, 20f));
            if (ImGui.Button(active ? L("common.active", "ACTIVE") : L("common.try", "TRY IT"), new Vector2(buttonWidth, 32f)))
                plugin.SetMode(mode);
        }
        ImGui.EndChild();
        ImGui.PopStyleColor();
        ImGui.Spacing();
    }

    private void Section(string text)
    {
        UiStyles.SectionLabel(text, plugin.CurrentTheme);
        ImGui.Spacing();
    }

    private static void Bullet(string text) => ImGui.BulletText(text);

    private void Callout(string text)
    {
        ImGui.PushStyleColor(ImGuiCol.ChildBg, UiStyles.WithAlpha(plugin.CurrentTheme.AccentStrong, 0.16f));
        if (ImGui.BeginChild($"##tour-callout-{page}-{ImGui.GetCursorPosY()}", new Vector2(0f, 72f), true, ImGuiWindowFlags.NoScrollbar))
        {
            ImGui.PushTextWrapPos(0f);
            ImGui.TextWrapped(text);
            ImGui.PopTextWrapPos();
        }
        ImGui.EndChild();
        ImGui.PopStyleColor();
    }

    private static string L(string key, string english) => Localizer.Text(key, english);

    private static string LF(string key, string english, params object[] arguments)
        => Localizer.Format(key, english, arguments);

    public void Dispose() { }
}
