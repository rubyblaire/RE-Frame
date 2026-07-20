using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Runtime.InteropServices;
using Dalamud.Plugin.Services;
using REFrameXIV.Models;

namespace REFrameXIV.Services;


public sealed class AfkScreenService : IDisposable
{
    private const float MovementThresholdSquared = 0.0004f;

    private const uint SoundAsync = 0x0001;
    private const uint SoundNoDefault = 0x0002;
    private const uint SoundMemory = 0x0004;
    private const uint SoundLoop = 0x0008;

    private readonly Configuration configuration;
    private readonly IObjectTable objectTable;
    private readonly IPluginLog log;
    private readonly GamepadSnapshot[] gamepadSnapshots = new GamepadSnapshot[4];

    private DateTime lastActivityUtc = DateTime.UtcNow;
    private DateTime ignoreActivityUntilUtc = DateTime.MinValue;
    private DateTime nextAudioStartAttemptUtc = DateTime.MinValue;
    private DateTime audioRefreshAfterUtc = DateTime.MinValue;
    private Vector3 lastPlayerPosition;
    private ulong lastPlayerId;
    private bool hasPlayerPosition;
    private bool wasGameForeground;
    private bool manualPreview;
    private bool gamepadsInitialized;
    private bool xInputAvailable = true;
    private bool isActive;
    private bool audioPlaying;
    private uint observedInputTick;
    private int appliedVolumeStep = -1;
    private nint gameWindow;
    private GCHandle pinnedAudio;
    private DateTime activeSceneStartedUtc = DateTime.MinValue;
    private long sceneRevision;

    public ForgeAfkScene? ActiveScene { get; private set; }
    public long SceneRevision => sceneRevision;

    public AfkScreenService(Configuration configuration, IObjectTable objectTable, IPluginLog log)
    {
        this.configuration = configuration;
        this.objectTable = objectTable;
        this.log = log;
        configuration.ForgePremium.EnsureValid();
        SelectConfiguredScene(DateTime.UtcNow, restartAudio: false);
        observedInputTick = ReadLastInputTick();
        wasGameForeground = IsGameForeground(ResolveGameWindow());
    }

    public bool IsActive => isActive;

    public TimeSpan IdleTime => DateTime.UtcNow - lastActivityUtc;


    public void Update(bool eligible)
    {
        var now = DateTime.UtcNow;
        var activityDetected = ObserveInputActivity() |
                               ObserveMovementActivity() |
                               ObserveGamepadActivity();
        if (now < ignoreActivityUntilUtc)
            activityDetected = false;


        if (!eligible || !wasGameForeground || (!configuration.EnableAfkScreen && !manualPreview))
        {
            ResetActivity(now);
            StopPresentation();
            return;
        }

        if (activityDetected)
        {
            ResetActivity(now);
            StopPresentation();
            return;
        }

        if (IsActive)
        {
            var sceneChanged = AdvanceSceneIfNeeded(now);
            EnsureAudioState(now, forceRefresh: sceneChanged);
            return;
        }

        if (manualPreview)
        {
            StartPresentation(now);
            return;
        }

        var timeoutMinutes = Math.Clamp(configuration.AfkTimeoutMinutes, 1, 120);
        if (now - lastActivityUtc >= TimeSpan.FromMinutes(timeoutMinutes))
            StartPresentation(now);
    }

    public void Preview()
    {
        if (IsActive)
        {
            Stop();
            return;
        }

        manualPreview = true;
        observedInputTick = ReadLastInputTick();
        wasGameForeground = IsGameForeground(ResolveGameWindow());
        ignoreActivityUntilUtc = DateTime.UtcNow.AddMilliseconds(750);
        nextAudioStartAttemptUtc = DateTime.MinValue;
        StartPresentation(DateTime.UtcNow);
    }

    public void Stop()
    {
        manualPreview = false;
        ignoreActivityUntilUtc = DateTime.MinValue;
        StopPresentation();
        ResetActivity(DateTime.UtcNow);
        observedInputTick = ReadLastInputTick();
    }

    public void ApplyConfigurationChange()
    {
        configuration.ForgePremium.EnsureValid();
        SelectConfiguredScene(DateTime.UtcNow, restartAudio: IsActive);

        if (!configuration.EnableAfkScreen && !manualPreview)
        {
            Stop();
            return;
        }

        if (!IsActive)
            return;

        if (!configuration.AfkScreenAudioEnabled)
        {
            StopAudio();
            return;
        }


        var desiredVolumeStep = ResolveVolumeStep();
        if (!audioPlaying)
            EnsureAudioState(DateTime.UtcNow, forceRefresh: true);
        else if (desiredVolumeStep != appliedVolumeStep)
            audioRefreshAfterUtc = DateTime.UtcNow.AddMilliseconds(180);
    }

    public void Dispose()
    {
        manualPreview = false;
        StopPresentation();
    }

    private void SelectConfiguredScene(DateTime now, bool restartAudio)
    {
        configuration.ForgePremium.EnsureValid();
        var settings = configuration.ForgePremium;
        var selected = settings.AfkScenes.FirstOrDefault(scene =>
                           scene.Enabled &&
                           string.Equals(scene.Id, settings.ActiveAfkSceneId, StringComparison.OrdinalIgnoreCase))
                       ?? settings.AfkScenes.FirstOrDefault(scene => scene.Enabled)
                       ?? settings.AfkScenes.FirstOrDefault();
        SetActiveScene(selected, now, restartAudio);
    }

    private bool AdvanceSceneIfNeeded(DateTime now)
    {
        var settings = configuration.ForgePremium;
        settings.EnsureValid();
        if (!settings.RotateAfkScenes || ActiveScene is null)
            return false;

        var duration = TimeSpan.FromSeconds(Math.Clamp(ActiveScene.DurationSeconds, 10, 900));
        if (now - activeSceneStartedUtc < duration)
            return false;

        var enabled = settings.AfkScenes.Where(scene => scene.Enabled).ToArray();
        if (enabled.Length <= 1)
        {
            activeSceneStartedUtc = now;
            return false;
        }

        var currentIndex = Array.FindIndex(enabled, scene =>
            string.Equals(scene.Id, ActiveScene.Id, StringComparison.OrdinalIgnoreCase));
        var next = enabled[(Math.Max(-1, currentIndex) + 1) % enabled.Length];
        SetActiveScene(next, now, restartAudio: true);
        return true;
    }

    private void SetActiveScene(ForgeAfkScene? scene, DateTime now, bool restartAudio)
    {
        var changed = !string.Equals(ActiveScene?.Id, scene?.Id, StringComparison.OrdinalIgnoreCase) ||
                      !string.Equals(ActiveScene?.ArtworkPath, scene?.ArtworkPath, StringComparison.Ordinal) ||
                      !string.Equals(ActiveScene?.AudioPath, scene?.AudioPath, StringComparison.Ordinal);
        ActiveScene = scene;
        activeSceneStartedUtc = now;
        if (changed || restartAudio)
        {
            sceneRevision++;
            if (restartAudio)
                StopAudio();
        }
    }

    private bool ObserveMovementActivity()
    {
        var player = objectTable.LocalPlayer;
        if (player is null)
        {
            hasPlayerPosition = false;
            lastPlayerId = 0;
            return false;
        }

        var currentId = player.GameObjectId;
        var currentPosition = player.Position;
        if (!hasPlayerPosition || currentId != lastPlayerId)
        {
            hasPlayerPosition = true;
            lastPlayerId = currentId;
            lastPlayerPosition = currentPosition;
            return true;
        }

        var moved = Vector3.DistanceSquared(currentPosition, lastPlayerPosition) > MovementThresholdSquared;
        lastPlayerPosition = currentPosition;
        return moved;
    }

    private bool ObserveInputActivity()
    {
        var currentGameWindow = ResolveGameWindow();
        var gameForeground = IsGameForeground(currentGameWindow);
        var currentInputTick = ReadLastInputTick();
        var changed = currentInputTick != 0 && currentInputTick != observedInputTick;
        var countsAsGameInput = changed && (gameForeground || wasGameForeground);

        if (changed)
            observedInputTick = currentInputTick;
        wasGameForeground = gameForeground;
        return countsAsGameInput;
    }

    private bool ObserveGamepadActivity()
    {
        if (!xInputAvailable)
            return false;

        var activity = false;
        try
        {
            for (var index = 0; index < gamepadSnapshots.Length; index++)
            {
                var connected = XInputGetState((uint)index, out var state) == 0;
                var snapshot = connected
                    ? GamepadSnapshot.From(state.Gamepad)
                    : default;

                if (snapshot.HasActiveInput ||
                    (gamepadsInitialized && snapshot != gamepadSnapshots[index]))
                    activity = true;

                gamepadSnapshots[index] = snapshot;
            }

            gamepadsInitialized = true;
        }
        catch (DllNotFoundException)
        {
            xInputAvailable = false;
        }
        catch (EntryPointNotFoundException)
        {
            xInputAvailable = false;
        }

        return activity;
    }

    private void ResetActivity(DateTime now)
    {
        lastActivityUtc = now;
        manualPreview = false;
    }

    private void StartPresentation(DateTime now)
    {
        if (IsActive)
            return;

        isActive = true;
        SelectConfiguredScene(now, restartAudio: false);
        GreetingVoiceService.Stop();
        EnsureAudioState(now, forceRefresh: true);
    }

    private void StopPresentation()
    {
        if (!IsActive && !audioPlaying)
            return;

        isActive = false;
        StopAudio();
    }

    private void EnsureAudioState(DateTime now, bool forceRefresh = false)
    {
        if (!IsActive || !configuration.AfkScreenAudioEnabled)
        {
            StopAudio();
            return;
        }

        var desiredVolumeStep = ResolveVolumeStep();
        var needsRefresh = !audioPlaying || desiredVolumeStep != appliedVolumeStep;
        if (!needsRefresh)
            return;

        if (!forceRefresh && now < audioRefreshAfterUtc)
            return;
        if (now < nextAudioStartAttemptUtc)
            return;

        TryStartAudio(now, desiredVolumeStep);
    }

    private int ResolveVolumeStep()
        => (int)MathF.Round(Math.Clamp(configuration.AfkScreenVolume, 0f, 1f) * 100f);

    private void TryStartAudio(DateTime now, int volumeStep)
    {
        var customAudioPath = ActiveScene?.AudioPath?.Trim() ?? string.Empty;
        var audioPath = !string.IsNullOrWhiteSpace(customAudioPath) && File.Exists(customAudioPath)
            ? customAudioPath
            : Path.Combine(
                Plugin.PluginInterface.AssemblyLocation.DirectoryName!,
                "Assets",
                "AFK",
                "AFKMusic.wav");

        if (!File.Exists(audioPath))
        {
            log.Warning("RE:Frame AFK music asset was not found: {AudioPath}", audioPath);
            nextAudioStartAttemptUtc = now.AddMinutes(1);
            return;
        }

        byte[] waveBytes;
        try
        {
            waveBytes = BuildVolumeAdjustedWave(audioPath, volumeStep / 100f);
        }
        catch (Exception ex)
        {
            log.Warning(ex, "RE:Frame could not prepare the AFK music asset: {AudioPath}", audioPath);
            nextAudioStartAttemptUtc = now.AddMinutes(1);
            return;
        }

        StopAudio();
        try
        {
            pinnedAudio = GCHandle.Alloc(waveBytes, GCHandleType.Pinned);
            var flags = SoundAsync | SoundNoDefault | SoundMemory | SoundLoop;
            if (!PlaySoundW(pinnedAudio.AddrOfPinnedObject(), nint.Zero, flags))
            {
                log.Warning("RE:Frame could not start the bundled AFK music.");
                FreePinnedAudio();
                nextAudioStartAttemptUtc = now.AddSeconds(30);
                return;
            }

            audioPlaying = true;
            appliedVolumeStep = volumeStep;
            audioRefreshAfterUtc = DateTime.MinValue;
            nextAudioStartAttemptUtc = DateTime.MinValue;
        }
        catch (Exception ex)
        {
            log.Warning(ex, "RE:Frame could not start the bundled AFK music.");
            FreePinnedAudio();
            audioPlaying = false;
            appliedVolumeStep = -1;
            nextAudioStartAttemptUtc = now.AddSeconds(30);
        }
    }

    private void StopAudio()
    {
        if (audioPlaying)
        {
            try
            {
                _ = PlaySoundW(nint.Zero, nint.Zero, 0);
            }
            catch (Exception ex)
            {
                log.Verbose(ex, "RE:Frame could not stop the AFK music cleanly.");
            }
        }

        audioPlaying = false;
        appliedVolumeStep = -1;
        FreePinnedAudio();
    }

    private void FreePinnedAudio()
    {
        if (pinnedAudio.IsAllocated)
            pinnedAudio.Free();
    }

    private static byte[] BuildVolumeAdjustedWave(string filePath, float volume)
    {
        var bytes = File.ReadAllBytes(filePath);
        if (bytes.Length < 44 || ReadFourCc(bytes, 0) != "RIFF" || ReadFourCc(bytes, 8) != "WAVE")
            throw new InvalidDataException("The AFK music is not a RIFF/WAVE file.");

        var format = 0;
        var bitsPerSample = 0;
        var dataOffset = -1;
        var dataLength = 0;
        var offset = 12;
        while (offset + 8 <= bytes.Length)
        {
            var chunkId = ReadFourCc(bytes, offset);
            var chunkLength = ReadUInt32LittleEndian(bytes, offset + 4);
            var chunkData = offset + 8;
            var boundedLength = (int)Math.Min(chunkLength, (uint)Math.Max(0, bytes.Length - chunkData));

            if (chunkId == "fmt " && boundedLength >= 16)
            {
                format = ReadUInt16LittleEndian(bytes, chunkData);
                bitsPerSample = ReadUInt16LittleEndian(bytes, chunkData + 14);
            }
            else if (chunkId == "data")
            {
                dataOffset = chunkData;
                dataLength = boundedLength;
            }

            var paddedLength = (long)chunkLength + (chunkLength & 1u);
            var nextOffset = chunkData + paddedLength;
            if (nextOffset <= offset || nextOffset > bytes.Length)
                break;
            offset = (int)nextOffset;
        }

        if (format != 1 || bitsPerSample != 16 || dataOffset < 0 || dataLength < 2)
            throw new InvalidDataException("The AFK music must be uncompressed 16-bit PCM WAV audio.");

        volume = Math.Clamp(volume, 0f, 1f);
        if (volume >= 0.999f)
            return bytes;

        var sampleEnd = dataOffset + (dataLength & ~1);
        for (var sampleOffset = dataOffset; sampleOffset < sampleEnd; sampleOffset += 2)
        {
            var sample = (short)ReadUInt16LittleEndian(bytes, sampleOffset);
            var scaled = (int)MathF.Round(sample * volume);
            scaled = Math.Clamp(scaled, short.MinValue, short.MaxValue);
            bytes[sampleOffset] = (byte)(scaled & 0xFF);
            bytes[sampleOffset + 1] = (byte)((scaled >> 8) & 0xFF);
        }

        return bytes;
    }

    private static string ReadFourCc(byte[] bytes, int offset)
        => new(new[] { (char)bytes[offset], (char)bytes[offset + 1], (char)bytes[offset + 2], (char)bytes[offset + 3] });

    private static ushort ReadUInt16LittleEndian(byte[] bytes, int offset)
        => (ushort)(bytes[offset] | (bytes[offset + 1] << 8));

    private static uint ReadUInt32LittleEndian(byte[] bytes, int offset)
        => (uint)(bytes[offset] |
                  (bytes[offset + 1] << 8) |
                  (bytes[offset + 2] << 16) |
                  (bytes[offset + 3] << 24));

    private nint ResolveGameWindow()
    {
        if (gameWindow != nint.Zero && IsWindow(gameWindow))
            return gameWindow;

        using var process = Process.GetCurrentProcess();
        process.Refresh();
        gameWindow = process.MainWindowHandle;
        return gameWindow;
    }

    private static bool IsGameForeground(nint window)
    {
        if (window == nint.Zero)
            return false;

        var foreground = GetForegroundWindow();
        if (foreground == nint.Zero)
            return false;

        return foreground == window || GetAncestor(foreground, 2) == window;
    }

    private static uint ReadLastInputTick()
    {
        var info = new LastInputInfo { Size = (uint)Marshal.SizeOf<LastInputInfo>() };
        return GetLastInputInfo(ref info) ? info.Time : 0;
    }

    private readonly record struct GamepadSnapshot(
        bool Connected,
        ushort Buttons,
        byte LeftTrigger,
        byte RightTrigger,
        short LeftX,
        short LeftY,
        short RightX,
        short RightY)
    {
        private const int LeftThumbDeadZone = 7849;
        private const int RightThumbDeadZone = 8689;
        private const byte TriggerThreshold = 30;

        public bool HasActiveInput => Connected &&
                                      (Buttons != 0 ||
                                       LeftTrigger != 0 ||
                                       RightTrigger != 0 ||
                                       LeftX != 0 ||
                                       LeftY != 0 ||
                                       RightX != 0 ||
                                       RightY != 0);

        public static GamepadSnapshot From(XInputGamepad gamepad)
            => new(
                true,
                gamepad.Buttons,
                QuantizeTrigger(gamepad.LeftTrigger),
                QuantizeTrigger(gamepad.RightTrigger),
                QuantizeAxis(gamepad.ThumbLX, LeftThumbDeadZone),
                QuantizeAxis(gamepad.ThumbLY, LeftThumbDeadZone),
                QuantizeAxis(gamepad.ThumbRX, RightThumbDeadZone),
                QuantizeAxis(gamepad.ThumbRY, RightThumbDeadZone));

        private static byte QuantizeTrigger(byte value)
            => value < TriggerThreshold ? (byte)0 : (byte)(value / 8);

        private static short QuantizeAxis(short value, int deadZone)
        {
            var expanded = (int)value;
            return Math.Abs(expanded) < deadZone ? (short)0 : (short)(expanded / 2048);
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct XInputState
    {
        public uint PacketNumber;
        public XInputGamepad Gamepad;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct XInputGamepad
    {
        public ushort Buttons;
        public byte LeftTrigger;
        public byte RightTrigger;
        public short ThumbLX;
        public short ThumbLY;
        public short ThumbRX;
        public short ThumbRY;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct LastInputInfo
    {
        public uint Size;
        public uint Time;
    }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetLastInputInfo(ref LastInputInfo info);

    [DllImport("user32.dll")]
    private static extern nint GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern nint GetAncestor(nint window, uint flags);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWindow(nint window);

    [DllImport("xinput1_4.dll")]
    private static extern uint XInputGetState(uint userIndex, out XInputState state);

    [DllImport("winmm.dll", EntryPoint = "PlaySoundW", SetLastError = false)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool PlaySoundW(nint sound, nint module, uint flags);
}
