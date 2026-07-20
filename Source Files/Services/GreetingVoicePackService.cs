using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using REFrameXIV.Models;

namespace REFrameXIV.Services;


public static class GreetingVoicePackService
{
    public const string RubyPackId = "ruby";
    public const string RubyPackName = "Ruby";
    public const string JarvinPackId = "jarvin";
    public const string JarvinPackName = "Jarvin";
    public const int MaxGreetingSets = 3;

    private static readonly string[] MorningFileNames = { "Morning.wav", "Morning2.wav", "Morning3.wav" };
    private static readonly string[] AfternoonFileNames = { "Afternoon.wav", "Afternoon2.wav", "Afternoon3.wav" };
    private static readonly string[] EveningFileNames = { "Evening.wav", "Evening2.wav", "Evening3.wav" };

    public static string VoicePacksDirectory
        => Path.Combine(Plugin.PluginInterface.ConfigDirectory.FullName, "VoicePacks");

    public static bool IsBuiltInPackId(string? packId)
        => string.Equals(packId, RubyPackId, StringComparison.OrdinalIgnoreCase) ||
           string.Equals(packId, JarvinPackId, StringComparison.OrdinalIgnoreCase);

    public static void NormalizeConfiguration(Configuration configuration)
    {
        configuration.CustomGreetingVoicePacks ??= new List<GreetingVoicePack>();
        configuration.GreetingVoiceRotationStates ??= new Dictionary<string, GreetingVoiceRotationState>(StringComparer.OrdinalIgnoreCase);

        if (configuration.GreetingVoiceRotationStates.Comparer != StringComparer.OrdinalIgnoreCase)
        {
            configuration.GreetingVoiceRotationStates = new Dictionary<string, GreetingVoiceRotationState>(
                configuration.GreetingVoiceRotationStates,
                StringComparer.OrdinalIgnoreCase);
        }

        var seenIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            RubyPackId,
            JarvinPackId,
        };

        for (var index = configuration.CustomGreetingVoicePacks.Count - 1; index >= 0; index--)
        {
            var pack = configuration.CustomGreetingVoicePacks[index];
            if (pack is null)
            {
                configuration.CustomGreetingVoicePacks.RemoveAt(index);
                continue;
            }

            pack.EnsureValid();
            if (!seenIds.Add(pack.Id))
                configuration.CustomGreetingVoicePacks.RemoveAt(index);
        }

        configuration.ActiveGreetingVoicePackId = configuration.ActiveGreetingVoicePackId?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(configuration.ActiveGreetingVoicePackId) ||
            (!IsBuiltInPackId(configuration.ActiveGreetingVoicePackId) &&
             configuration.CustomGreetingVoicePacks.All(pack =>
                 !string.Equals(pack.Id, configuration.ActiveGreetingVoicePackId, StringComparison.OrdinalIgnoreCase))))
        {
            configuration.ActiveGreetingVoicePackId = JarvinPackId;
        }
    }

    public static string GetDisplayName(Configuration configuration, string? packId)
    {
        if (string.Equals(packId, RubyPackId, StringComparison.OrdinalIgnoreCase))
            return RubyPackName;

        if (string.Equals(packId, JarvinPackId, StringComparison.OrdinalIgnoreCase))
            return JarvinPackName;

        var pack = configuration.CustomGreetingVoicePacks.FirstOrDefault(candidate =>
            string.Equals(candidate.Id, packId, StringComparison.OrdinalIgnoreCase));
        if (pack is null)
            return JarvinPackName;

        return IsBuiltInPackName(pack.Name)
            ? $"{pack.Name} (Custom)"
            : pack.Name;
    }

    public static string[] ResolveFiles(int localHour, Configuration configuration, out string resolvedPackId)
    {
        NormalizeConfiguration(configuration);
        var selectedId = configuration.ActiveGreetingVoicePackId;

        if (IsBuiltInPackId(selectedId))
        {
            var builtInFiles = GetBuiltInFiles(selectedId, localHour);
            if (builtInFiles.Any(File.Exists))
            {
                resolvedPackId = selectedId;
                return builtInFiles;
            }

            if (!string.Equals(selectedId, RubyPackId, StringComparison.OrdinalIgnoreCase))
            {
                Plugin.Log.Warning(
                    "RE:Frame bundled greeting voice pack {PackName} has no playable files for the current daypart; falling back to Ruby.",
                    GetDisplayName(configuration, selectedId));
            }
        }
        else
        {
            var pack = configuration.CustomGreetingVoicePacks.FirstOrDefault(candidate =>
                string.Equals(candidate.Id, selectedId, StringComparison.OrdinalIgnoreCase));
            if (pack is not null)
            {
                var customFiles = GetCustomFiles(pack, localHour);
                if (customFiles.Any(File.Exists))
                {
                    resolvedPackId = pack.Id;
                    return customFiles;
                }

                Plugin.Log.Warning(
                    "RE:Frame greeting voice pack {PackName} has no playable files for the current daypart; falling back to Ruby.",
                    pack.Name);
            }
        }

        resolvedPackId = RubyPackId;
        return GetBuiltInFiles(RubyPackId, localHour);
    }

    public static bool TryImportPack(
        Configuration configuration,
        bool hasPremiumAccess,
        string name,
        IReadOnlyList<(string Morning, string Afternoon, string Evening)> greetingSets,
        out GreetingVoicePack? importedPack,
        out string status)
    {
        importedPack = null;
        NormalizeConfiguration(configuration);

        if (!hasPremiumAccess)
        {
            status = "Custom voice-pack importing requires an active RE:Forge membership.";
            return false;
        }

        name = name?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(name))
        {
            status = "Enter a name for the voice pack.";
            return false;
        }

        if (IsBuiltInPackName(name) ||
            configuration.CustomGreetingVoicePacks.Any(pack => string.Equals(pack.Name, name, StringComparison.OrdinalIgnoreCase)))
        {
            status = "A voice pack with that name already exists.";
            return false;
        }

        if (greetingSets.Count < 1 || greetingSets.Count > MaxGreetingSets)
        {
            status = $"Voice packs require between 1 and {MaxGreetingSets} complete greeting sets.";
            return false;
        }

        for (var index = 0; index < greetingSets.Count; index++)
        {
            var set = greetingSets[index];
            if (!TryValidateWaveFile(set.Morning, out var morningError))
            {
                status = $"Set {index + 1} Morning: {morningError}";
                return false;
            }

            if (!TryValidateWaveFile(set.Afternoon, out var afternoonError))
            {
                status = $"Set {index + 1} Afternoon: {afternoonError}";
                return false;
            }

            if (!TryValidateWaveFile(set.Evening, out var eveningError))
            {
                status = $"Set {index + 1} Evening: {eveningError}";
                return false;
            }
        }

        var id = Guid.NewGuid().ToString("N");
        var root = VoicePacksDirectory;
        var destination = Path.Combine(root, id);
        var staging = Path.Combine(root, $".{id}.importing");

        try
        {
            Directory.CreateDirectory(root);
            if (Directory.Exists(staging))
                Directory.Delete(staging, true);
            Directory.CreateDirectory(staging);

            for (var index = 0; index < greetingSets.Count; index++)
            {
                var set = greetingSets[index];
                File.Copy(set.Morning, Path.Combine(staging, GetFileName("Morning", index)), true);
                File.Copy(set.Afternoon, Path.Combine(staging, GetFileName("Afternoon", index)), true);
                File.Copy(set.Evening, Path.Combine(staging, GetFileName("Evening", index)), true);
            }

            Directory.Move(staging, destination);

            importedPack = new GreetingVoicePack
            {
                Id = id,
                Name = name,
                GreetingSetCount = greetingSets.Count,
            };
            configuration.CustomGreetingVoicePacks.Add(importedPack);
            configuration.ActiveGreetingVoicePackId = importedPack.Id;
            configuration.GreetingVoiceRotationStates[importedPack.Id] = new GreetingVoiceRotationState();
            status = $"Imported and activated {name}.";
            return true;
        }
        catch (Exception ex)
        {
            try
            {
                if (Directory.Exists(staging))
                    Directory.Delete(staging, true);
            }
            catch
            {

            }

            Plugin.Log.Warning(ex, "RE:Frame could not import greeting voice pack {PackName}.", name);
            status = "The voice pack could not be imported. Check the selected files and folder permissions.";
            return false;
        }
    }

    public static bool TryDeletePack(Configuration configuration, string packId, out string status)
    {
        NormalizeConfiguration(configuration);
        if (IsBuiltInPackId(packId))
        {
            status = $"The bundled {GetDisplayName(configuration, packId)} voice pack cannot be removed.";
            return false;
        }

        var pack = configuration.CustomGreetingVoicePacks.FirstOrDefault(candidate =>
            string.Equals(candidate.Id, packId, StringComparison.OrdinalIgnoreCase));
        if (pack is null)
        {
            status = "That voice pack is no longer available.";
            return false;
        }

        try
        {
            var directory = GetPackDirectory(pack.Id);
            if (Directory.Exists(directory))
                Directory.Delete(directory, true);
        }
        catch (Exception ex)
        {
            Plugin.Log.Warning(ex, "RE:Frame could not remove greeting voice pack {PackName}.", pack.Name);
            status = "The voice pack files could not be removed. They may still be in use.";
            return false;
        }

        configuration.CustomGreetingVoicePacks.Remove(pack);
        configuration.GreetingVoiceRotationStates.Remove(pack.Id);
        if (string.Equals(configuration.ActiveGreetingVoicePackId, pack.Id, StringComparison.OrdinalIgnoreCase))
            configuration.ActiveGreetingVoicePackId = JarvinPackId;

        status = $"Removed {pack.Name}.";
        return true;
    }

    public static bool IsPackReady(GreetingVoicePack pack, out string status)
    {
        pack.EnsureValid();
        for (var setIndex = 0; setIndex < pack.GreetingSetCount; setIndex++)
        {
            foreach (var period in new[] { "Morning", "Afternoon", "Evening" })
            {
                var path = Path.Combine(GetPackDirectory(pack.Id), GetFileName(period, setIndex));
                if (!TryValidateWaveFile(path, out var validationError))
                {
                    status = $"{period} set {setIndex + 1}: {validationError}";
                    return false;
                }
            }
        }

        status = pack.GreetingSetCount == 1
            ? "Ready • 1 greeting set"
            : $"Ready • {pack.GreetingSetCount} rotating greeting sets";
        return true;
    }

    public static string GetPackDirectory(string packId)
        => Path.Combine(VoicePacksDirectory, packId);

    private static bool IsBuiltInPackName(string? name)
        => string.Equals(name, RubyPackName, StringComparison.OrdinalIgnoreCase) ||
           string.Equals(name, JarvinPackName, StringComparison.OrdinalIgnoreCase);

    private static string[] GetBuiltInFiles(string packId, int localHour)
    {
        var assemblyDirectory = Plugin.PluginInterface.AssemblyLocation.DirectoryName;
        if (string.IsNullOrWhiteSpace(assemblyDirectory))
            return Array.Empty<string>();

        var isJarvin = string.Equals(packId, JarvinPackId, StringComparison.OrdinalIgnoreCase);
        var directory = isJarvin
            ? Path.Combine(assemblyDirectory, "Assets", "Greetings", "Jarvin")
            : Path.Combine(assemblyDirectory, "Assets", "Greetings");
        var setCount = isJarvin ? 2 : 3;
        return GetPeriodFileNames(localHour)
            .Take(setCount)
            .Select(name => Path.Combine(directory, name))
            .ToArray();
    }

    private static string[] GetCustomFiles(GreetingVoicePack pack, int localHour)
    {
        var period = ResolvePeriodName(localHour);
        var directory = GetPackDirectory(pack.Id);
        var files = new string[pack.GreetingSetCount];
        for (var index = 0; index < files.Length; index++)
            files[index] = Path.Combine(directory, GetFileName(period, index));
        return files;
    }

    private static string[] GetPeriodFileNames(int localHour)
    {
        localHour = ((localHour % 24) + 24) % 24;
        if (localHour >= 5 && localHour <= 11)
            return MorningFileNames;
        if (localHour >= 12 && localHour <= 16)
            return AfternoonFileNames;
        return EveningFileNames;
    }

    private static string ResolvePeriodName(int localHour)
    {
        localHour = ((localHour % 24) + 24) % 24;
        if (localHour >= 5 && localHour <= 11)
            return "Morning";
        if (localHour >= 12 && localHour <= 16)
            return "Afternoon";
        return "Evening";
    }

    private static string GetFileName(string period, int zeroBasedSetIndex)
        => zeroBasedSetIndex == 0 ? $"{period}.wav" : $"{period}{zeroBasedSetIndex + 1}.wav";

    private static bool TryValidateWaveFile(string path, out string error)
    {
        path = path?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(path))
        {
            error = "choose a .wav file.";
            return false;
        }

        if (!File.Exists(path))
        {
            error = "the selected file does not exist.";
            return false;
        }

        if (!string.Equals(Path.GetExtension(path), ".wav", StringComparison.OrdinalIgnoreCase))
        {
            error = "the selected file must use the .wav extension.";
            return false;
        }

        try
        {
            using var stream = File.OpenRead(path);
            using var reader = new BinaryReader(stream);
            if (stream.Length < 12 || new string(reader.ReadChars(4)) != "RIFF")
            {
                error = "the selected file is not a RIFF WAV file.";
                return false;
            }

            _ = reader.ReadUInt32();
            if (new string(reader.ReadChars(4)) != "WAVE")
            {
                error = "the selected file is not a WAVE audio file.";
                return false;
            }
        }
        catch (Exception ex)
        {
            Plugin.Log.Verbose(ex, "RE:Frame could not validate greeting voice file {FilePath}.", path);
            error = "the selected file could not be read.";
            return false;
        }

        error = string.Empty;
        return true;
    }
}
