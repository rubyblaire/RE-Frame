using System;
using System.IO;
using System.Net.Http;
using System.Numerics;
using System.Threading;
using System.Threading.Tasks;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Textures;
using Dalamud.Interface.Textures.TextureWraps;
using Dalamud.Interface.Windowing;
using REFrameXIV.Services;
using REFrameXIV.UI;

namespace REFrameXIV.Windows;


public sealed class AfkBackdropWindow : Window, IDisposable
{
    private const float SourceAspect = 16f / 9f;
    private const string RemoteArtworkUrl = "https://i.imgur.com/cbGvsZp.jpeg";
    private const string ArtworkFileName = "AFKScreen-cbGvsZp.jpeg";

    private readonly Plugin plugin;
    private readonly AfkScreenService afkScreen;
    private readonly CancellationTokenSource artworkCancellation = new();
    private readonly string packagedArtworkPath;
    private readonly string cachedArtworkPath;

    private ISharedImmediateTexture? imageTexture;
    private string? pendingArtworkPath;
    private bool artworkDownloadStarted;
    private bool disposed;
    private long loadedSceneRevision = -1;

    public AfkBackdropWindow(Plugin plugin, AfkScreenService afkScreen)
        : base("RE:Frame AFK Screen###REFrameAfkBackdrop",
            ImGuiWindowFlags.NoDecoration |
            ImGuiWindowFlags.NoInputs |
            ImGuiWindowFlags.NoSavedSettings |
            ImGuiWindowFlags.NoNav |
            ImGuiWindowFlags.NoBackground)
    {
        this.plugin = plugin;
        this.afkScreen = afkScreen;

        packagedArtworkPath = Path.Combine(
            Plugin.PluginInterface.AssemblyLocation.DirectoryName!,
            "Assets",
            "AFK",
            "AFKScreen.jpeg");
        cachedArtworkPath = Path.Combine(
            Plugin.PluginInterface.ConfigDirectory.FullName,
            "AFK",
            ArtworkFileName);

        ReloadScene();

        IsOpen = false;
        RespectCloseHotkey = false;
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
        if (!afkScreen.IsActive)
        {
            IsOpen = false;
            return;
        }

        TryActivateDownloadedArtwork();
        if (loadedSceneRevision != afkScreen.SceneRevision)
            ReloadScene();
        if (imageTexture is null && !artworkDownloadStarted)
            BeginArtworkDownload();

        var draw = ImGui.GetWindowDrawList();
        var origin = ImGui.GetWindowPos();
        var canvas = ImGui.GetWindowSize();

        draw.AddRectFilled(origin, origin + canvas, 0xFF000000);

        var imageSize = canvas;
        var imageMin = origin;
        var imageMax = origin + imageSize;

        IDalamudTextureWrap? wrap = imageTexture?.GetWrapOrDefault();
        if (wrap is not null)
        {
            ResolveCoverUvs(imageSize, out var uvMin, out var uvMax);
            draw.AddImage(wrap.Handle, imageMin, imageMax, uvMin, uvMax, 0xFFFFFFFF);
        }

        var scene = afkScreen.ActiveScene;
        if (scene is not null && scene.DimAmount > 0.001f)
        {
            var alpha = (uint)Math.Clamp((int)MathF.Round(scene.DimAmount * 255f), 0, 255);
            draw.AddRectFilled(origin, origin + canvas, alpha << 24);
        }

        var premium = plugin.Configuration.ForgePremium;
        if (scene is not null && premium.AfkShowStatusText && !string.IsNullOrWhiteSpace(scene.StatusText))
        {
            var padding = MathF.Max(24f, canvas.Y * 0.04f);
            var status = scene.StatusText.Trim();
            var statusSize = ImGui.CalcTextSize(status);
            var panelMin = new Vector2(origin.X + padding, origin.Y + canvas.Y - padding - statusSize.Y - 30f);
            var panelMax = new Vector2(panelMin.X + MathF.Max(320f, statusSize.X + 48f), panelMin.Y + statusSize.Y + 30f);
            draw.AddRectFilled(panelMin, panelMax, 0x99000000, 10f);
            draw.AddText(panelMin + new Vector2(20f, 15f), 0xFFFFFFFF, status);

            if (premium.AfkShowCharacterName && !premium.AfkStreamSafe)
            {
                var characterName = Plugin.ObjectTable.LocalPlayer?.Name.ToString() ?? string.Empty;
                if (!string.IsNullOrWhiteSpace(characterName))
                {
                    var namePosition = panelMin + new Vector2(20f, -24f);
                    draw.AddText(namePosition, 0xCCFFFFFF, characterName);
                }
            }
        }
    }

    public void ReloadScene()
    {
        loadedSceneRevision = afkScreen.SceneRevision;
        var customPath = afkScreen.ActiveScene?.ArtworkPath?.Trim() ?? string.Empty;
        if (!string.IsNullOrWhiteSpace(customPath) && TryLoadArtwork(customPath))
            return;
        if (TryLoadArtwork(cachedArtworkPath) || TryLoadArtwork(packagedArtworkPath))
            return;
        BeginArtworkDownload();
    }

    public void Dispose()
    {
        disposed = true;
        artworkCancellation.Cancel();
        artworkCancellation.Dispose();
        IsOpen = false;
    }

    private bool TryLoadArtwork(string path)
    {
        if (!LooksLikeImageFile(path))
            return false;

        try
        {
            imageTexture = Plugin.TextureProvider.GetFromFile(path);
            return true;
        }
        catch (Exception ex)
        {
            Plugin.Log.Warning(ex, "RE:Frame could not load AFK artwork: {ArtworkPath}", path);
            return false;
        }
    }

    private void BeginArtworkDownload()
    {
        if (disposed || artworkDownloadStarted)
            return;

        artworkDownloadStarted = true;
        var token = artworkCancellation.Token;
        _ = Task.Run(async () =>
        {
            try
            {
                var directory = Path.GetDirectoryName(cachedArtworkPath)!;
                Directory.CreateDirectory(directory);

                using var handler = new HttpClientHandler { AllowAutoRedirect = true };
                using var client = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(25) };
                client.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 REFrameXIV/0.4.0.66");
                client.DefaultRequestHeaders.Accept.ParseAdd("image/avif,image/webp,image/apng,image/*,*/*;q=0.8");
                client.DefaultRequestHeaders.Referrer = new Uri("https://imgur.com/");

                using var response = await client.GetAsync(RemoteArtworkUrl, HttpCompletionOption.ResponseHeadersRead, token).ConfigureAwait(false);
                response.EnsureSuccessStatusCode();
                var bytes = await response.Content.ReadAsByteArrayAsync(token).ConfigureAwait(false);
                if (!LooksLikeImageBytes(bytes))
                    throw new InvalidDataException("The AFK artwork response was not a JPEG or PNG image.");

                var temporaryPath = cachedArtworkPath + ".tmp";
                await File.WriteAllBytesAsync(temporaryPath, bytes, token).ConfigureAwait(false);
                File.Move(temporaryPath, cachedArtworkPath, true);
                Interlocked.Exchange(ref pendingArtworkPath, cachedArtworkPath);
                Plugin.Log.Information("RE:Frame cached AFK artwork from the configured Imgur image.");
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested)
            {

            }
            catch (Exception ex)
            {
                Plugin.Log.Warning(ex, "RE:Frame could not download AFK artwork from {ArtworkUrl}.", RemoteArtworkUrl);
            }
        }, token);
    }

    private void TryActivateDownloadedArtwork()
    {
        var path = Interlocked.Exchange(ref pendingArtworkPath, null);
        if (string.IsNullOrWhiteSpace(path) || disposed)
            return;

        TryLoadArtwork(path);
    }

    private static bool LooksLikeImageFile(string path)
    {
        try
        {
            if (!File.Exists(path))
                return false;

            using var stream = File.OpenRead(path);
            Span<byte> header = stackalloc byte[8];
            if (stream.Read(header) < 8)
                return false;

            return LooksLikeImageBytes(header);
        }
        catch
        {
            return false;
        }
    }

    private static bool LooksLikeImageBytes(ReadOnlySpan<byte> bytes)
    {
        var jpeg = bytes.Length >= 3 && bytes[0] == 0xFF && bytes[1] == 0xD8 && bytes[2] == 0xFF;
        var png = bytes.Length >= 8 &&
                  bytes[0] == 0x89 && bytes[1] == 0x50 && bytes[2] == 0x4E && bytes[3] == 0x47 &&
                  bytes[4] == 0x0D && bytes[5] == 0x0A && bytes[6] == 0x1A && bytes[7] == 0x0A;
        return jpeg || png;
    }

    private static void ResolveCoverUvs(Vector2 destinationSize, out Vector2 uvMin, out Vector2 uvMax)
    {
        var destinationAspect = destinationSize.X / MathF.Max(1f, destinationSize.Y);
        uvMin = Vector2.Zero;
        uvMax = Vector2.One;

        if (destinationAspect > SourceAspect)
        {
            var visibleHeight = SourceAspect / destinationAspect;
            var crop = (1f - visibleHeight) * 0.5f;
            uvMin.Y = crop;
            uvMax.Y = 1f - crop;
        }
        else if (destinationAspect < SourceAspect)
        {
            var visibleWidth = destinationAspect / SourceAspect;
            var crop = (1f - visibleWidth) * 0.5f;
            uvMin.X = crop;
            uvMax.X = 1f - crop;
        }
    }
}
