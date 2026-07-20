using System;
using System.IO;
using System.Runtime.InteropServices;
using REFrameXIV.Models;

namespace REFrameXIV.Services;


public static class GreetingVoiceService
{
    private const uint SoundAsync = 0x0001;
    private const uint SoundNoDefault = 0x0002;
    private const uint SoundMemory = 0x0004;
    private const uint SoundFileName = 0x00020000;

    private static readonly object PlaybackLock = new();
    private static GCHandle pinnedAudio;


    public static double Play(int localHour, Configuration configuration, out bool rotationAdvanced)
    {
        rotationAdvanced = false;

        var files = GreetingVoicePackService.ResolveFiles(localHour, configuration, out var resolvedPackId);
        if (files.Length == 0)
        {
            Plugin.Log.Warning("RE:Frame could not resolve any greeting voice-over files.");
            return 0d;
        }

        var period = ResolvePeriod(localHour);
        var startIndex = NormalizeIndex(GetNextIndex(configuration, resolvedPackId, period), files.Length);
        for (var offset = 0; offset < files.Length; offset++)
        {
            var index = (startIndex + offset) % files.Length;
            var filePath = files[index];
            if (!File.Exists(filePath))
            {
                Plugin.Log.Warning("RE:Frame greeting voice-over asset was not found: {FilePath}", filePath);
                continue;
            }

            try
            {
                var durationSeconds = TryReadWaveDurationSeconds(filePath);
                if (!TryPlay(filePath, configuration.GreetingVoiceVolume))
                {
                    Plugin.Log.Warning("RE:Frame could not play greeting voice-over asset: {FilePath}", filePath);
                    continue;
                }

                SetNextIndex(configuration, resolvedPackId, period, (index + 1) % files.Length);
                rotationAdvanced = true;
                return durationSeconds;
            }
            catch (Exception ex)
            {
                Plugin.Log.Warning(ex, "RE:Frame could not play greeting voice-over asset: {FilePath}", filePath);
            }
        }

        return 0d;
    }

    public static void Stop()
    {
        lock (PlaybackLock)
        {
            try
            {
                _ = PlaySoundMemory(nint.Zero, nint.Zero, 0);
            }
            catch (Exception ex)
            {
                Plugin.Log.Verbose(ex, "RE:Frame could not stop the greeting voice-over cleanly.");
            }
            finally
            {
                FreePinnedAudio();
            }
        }
    }

    private static bool TryPlay(string filePath, float volume)
    {
        lock (PlaybackLock)
        {
            StopPlaybackWithoutLock();

            volume = Math.Clamp(volume, 0f, 1f);
            if (TryBuildVolumeAdjustedWave(filePath, volume, out var waveBytes))
            {
                pinnedAudio = GCHandle.Alloc(waveBytes, GCHandleType.Pinned);
                if (PlaySoundMemory(
                        pinnedAudio.AddrOfPinnedObject(),
                        nint.Zero,
                        SoundAsync | SoundNoDefault | SoundMemory))
                    return true;

                FreePinnedAudio();
                return false;
            }


            return PlaySoundFile(filePath, nint.Zero, SoundAsync | SoundNoDefault | SoundFileName);
        }
    }

    private static void StopPlaybackWithoutLock()
    {
        try
        {
            _ = PlaySoundMemory(nint.Zero, nint.Zero, 0);
        }
        finally
        {
            FreePinnedAudio();
        }
    }

    private static void FreePinnedAudio()
    {
        if (pinnedAudio.IsAllocated)
            pinnedAudio.Free();
    }

    private static bool TryBuildVolumeAdjustedWave(string filePath, float volume, out byte[] waveBytes)
    {
        waveBytes = File.ReadAllBytes(filePath);
        if (waveBytes.Length < 44 || ReadFourCc(waveBytes, 0) != "RIFF" || ReadFourCc(waveBytes, 8) != "WAVE")
            return false;

        var format = 0;
        var bitsPerSample = 0;
        var dataOffset = -1;
        var dataLength = 0;
        var offset = 12;
        while (offset + 8 <= waveBytes.Length)
        {
            var chunkId = ReadFourCc(waveBytes, offset);
            var chunkLength = ReadUInt32LittleEndian(waveBytes, offset + 4);
            var chunkData = offset + 8;
            var boundedLength = (int)Math.Min(chunkLength, (uint)Math.Max(0, waveBytes.Length - chunkData));

            if (chunkId == "fmt " && boundedLength >= 16)
            {
                format = ReadUInt16LittleEndian(waveBytes, chunkData);
                bitsPerSample = ReadUInt16LittleEndian(waveBytes, chunkData + 14);
            }
            else if (chunkId == "data")
            {
                dataOffset = chunkData;
                dataLength = boundedLength;
            }

            var paddedLength = (long)chunkLength + (chunkLength & 1u);
            var nextOffset = chunkData + paddedLength;
            if (nextOffset <= offset || nextOffset > waveBytes.Length)
                break;
            offset = (int)nextOffset;
        }

        if (format != 1 || bitsPerSample != 16 || dataOffset < 0 || dataLength < 2)
            return false;

        if (volume >= 0.999f)
            return true;

        var sampleEnd = dataOffset + (dataLength & ~1);
        for (var sampleOffset = dataOffset; sampleOffset < sampleEnd; sampleOffset += 2)
        {
            var sample = (short)ReadUInt16LittleEndian(waveBytes, sampleOffset);
            var scaled = (int)MathF.Round(sample * volume);
            scaled = Math.Clamp(scaled, short.MinValue, short.MaxValue);
            waveBytes[sampleOffset] = (byte)(scaled & 0xFF);
            waveBytes[sampleOffset + 1] = (byte)((scaled >> 8) & 0xFF);
        }

        return true;
    }

    private static GreetingPeriod ResolvePeriod(int localHour)
    {
        localHour = ((localHour % 24) + 24) % 24;
        if (localHour >= 5 && localHour <= 11)
            return GreetingPeriod.Morning;
        if (localHour >= 12 && localHour <= 16)
            return GreetingPeriod.Afternoon;
        return GreetingPeriod.Evening;
    }

    private static int GetNextIndex(Configuration configuration, string packId, GreetingPeriod period)
    {
        if (string.Equals(packId, GreetingVoicePackService.RubyPackId, StringComparison.OrdinalIgnoreCase))
        {
            return period switch
            {
                GreetingPeriod.Morning => configuration.NextMorningGreetingVoiceIndex,
                GreetingPeriod.Afternoon => configuration.NextAfternoonGreetingVoiceIndex,
                _ => configuration.NextEveningGreetingVoiceIndex,
            };
        }

        GreetingVoicePackService.NormalizeConfiguration(configuration);
        if (!configuration.GreetingVoiceRotationStates.TryGetValue(packId, out var state))
        {
            state = new GreetingVoiceRotationState();
            configuration.GreetingVoiceRotationStates[packId] = state;
        }

        return period switch
        {
            GreetingPeriod.Morning => state.NextMorningIndex,
            GreetingPeriod.Afternoon => state.NextAfternoonIndex,
            _ => state.NextEveningIndex,
        };
    }

    private static void SetNextIndex(Configuration configuration, string packId, GreetingPeriod period, int value)
    {
        if (string.Equals(packId, GreetingVoicePackService.RubyPackId, StringComparison.OrdinalIgnoreCase))
        {
            switch (period)
            {
                case GreetingPeriod.Morning:
                    configuration.NextMorningGreetingVoiceIndex = value;
                    break;
                case GreetingPeriod.Afternoon:
                    configuration.NextAfternoonGreetingVoiceIndex = value;
                    break;
                default:
                    configuration.NextEveningGreetingVoiceIndex = value;
                    break;
            }

            return;
        }

        GreetingVoicePackService.NormalizeConfiguration(configuration);
        if (!configuration.GreetingVoiceRotationStates.TryGetValue(packId, out var state))
        {
            state = new GreetingVoiceRotationState();
            configuration.GreetingVoiceRotationStates[packId] = state;
        }

        switch (period)
        {
            case GreetingPeriod.Morning:
                state.NextMorningIndex = value;
                break;
            case GreetingPeriod.Afternoon:
                state.NextAfternoonIndex = value;
                break;
            default:
                state.NextEveningIndex = value;
                break;
        }
    }

    private static int NormalizeIndex(int index, int count)
    {
        if (count <= 0)
            return 0;

        index %= count;
        return index < 0 ? index + count : index;
    }

    private static double TryReadWaveDurationSeconds(string filePath)
    {
        try
        {
            using var stream = File.OpenRead(filePath);
            using var reader = new BinaryReader(stream);
            if (stream.Length < 12 || ReadFourCc(reader) != "RIFF")
                return 0d;

            _ = reader.ReadUInt32();
            if (ReadFourCc(reader) != "WAVE")
                return 0d;

            uint byteRate = 0;
            uint dataSize = 0;
            while (stream.Position + 8 <= stream.Length)
            {
                var chunkId = ReadFourCc(reader);
                var chunkSize = reader.ReadUInt32();
                var chunkStart = stream.Position;
                var chunkEnd = Math.Min(stream.Length, chunkStart + chunkSize);

                if (chunkId == "fmt " && chunkSize >= 12 && chunkStart + 12 <= stream.Length)
                {
                    _ = reader.ReadUInt16();
                    _ = reader.ReadUInt16();
                    _ = reader.ReadUInt32();
                    byteRate = reader.ReadUInt32();
                }
                else if (chunkId == "data")
                {
                    dataSize = chunkSize;
                }

                var paddedEnd = chunkEnd + (chunkSize & 1u);
                stream.Position = Math.Min(stream.Length, paddedEnd);
                if (byteRate > 0 && dataSize > 0)
                    return dataSize / (double)byteRate;
            }
        }
        catch (Exception ex)
        {
            Plugin.Log.Verbose(ex, "RE:Frame could not read the duration of greeting voice-over asset: {FilePath}", filePath);
        }

        return 0d;
    }

    private static string ReadFourCc(BinaryReader reader)
        => new string(reader.ReadChars(4));

    private static string ReadFourCc(byte[] bytes, int offset)
        => new(new[] { (char)bytes[offset], (char)bytes[offset + 1], (char)bytes[offset + 2], (char)bytes[offset + 3] });

    private static ushort ReadUInt16LittleEndian(byte[] bytes, int offset)
        => (ushort)(bytes[offset] | (bytes[offset + 1] << 8));

    private static uint ReadUInt32LittleEndian(byte[] bytes, int offset)
        => (uint)(bytes[offset] |
                  (bytes[offset + 1] << 8) |
                  (bytes[offset + 2] << 16) |
                  (bytes[offset + 3] << 24));

    private enum GreetingPeriod
    {
        Morning,
        Afternoon,
        Evening,
    }

    [DllImport("winmm.dll", EntryPoint = "PlaySoundW", CharSet = CharSet.Unicode, SetLastError = false)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool PlaySoundFile(string? sound, nint module, uint flags);

    [DllImport("winmm.dll", EntryPoint = "PlaySoundW", SetLastError = false)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool PlaySoundMemory(nint sound, nint module, uint flags);
}
