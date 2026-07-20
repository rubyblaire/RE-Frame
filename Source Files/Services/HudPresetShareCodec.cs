using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Text.Json;
using REFrameXIV.Models;
using REFrameXIV.Theme;

namespace REFrameXIV.Services;


public static class HudPresetShareCodec
{
    private const string Prefix = "RF3:";
    private const string Rf2Prefix = "RF2:";
    private const string LegacyPrefix = "RFHUD1:";
    private const byte FormatVersion = 7;
    private const byte Rf2FormatVersion = 11;
    private const ushort MissingLayout = ushort.MaxValue;
    private const ushort QuantizedMaximum = ushort.MaxValue - 1;
    private const string Base85Alphabet = "0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz!#$%&()*+-./:=?@[]^_{}~";


    private static readonly UiMode[] Rf3LegacyModes =
    {
        UiMode.Leisure,
        UiMode.Quest,
        UiMode.RaidReady,
        UiMode.Work,
    };

    private static readonly UiMode[] Rf3Modes =
    {
        UiMode.Leisure,
        UiMode.Quest,
        UiMode.RaidReady,
        UiMode.Work,
        UiMode.Roleplay,
    };

    private static readonly JsonSerializerOptions LegacyJsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false,
    };

    public static string Encode(HudPresetData preset)
    {
        using var raw = new MemoryStream(256);
        using (var writer = new BinaryWriter(raw, Encoding.UTF8, leaveOpen: true))
        {
            writer.Write(FormatVersion);
            WriteShortString(writer, preset.Name, 48);
            WriteShortString(writer, preset.SourceJob, 8);
            writer.Write(PackFlags(preset));
            writer.Write(PackRaidFlags(preset));
            writer.Write((byte)preset.SelectedTheme);
            writer.Write(Quantize8(preset.InterfaceScale, 0.75f, 1.35f));
            writer.Write(Quantize8(preset.HudOpacity, 0.35f, 1f));
            writer.Write(Quantize8(preset.HaloRadius, 75f, 190f));
            writer.Write(Quantize8(preset.HaloThickness, 2f, 12f));
            writer.Write(Quantize8(preset.HaloVerticalOffset, -140f, 100f));
            writer.Write((byte)HotbarGridLayouts.NormalizeColumns(preset.ActionBarOneColumns));
            writer.Write((byte)HotbarGridLayouts.NormalizeColumns(preset.ActionBarTwoColumns));
            writer.Write((byte)HotbarGridLayouts.NormalizeColumns(preset.ActionBarThreeColumns));
            writer.Write((byte)HotbarGridLayouts.NormalizeColumns(preset.PetBarColumns));

            WriteSparseLayouts(writer, preset.HudLayouts, null, HudElementIds.All);
            WriteSparsePlacements(writer, preset);

            byte profilePresence = 0;
            var profiles = new HudModeProfile?[Rf3Modes.Length];
            for (var i = 0; i < Rf3Modes.Length; i++)
            {
                if (!TryResolveProfile(preset, Rf3Modes[i], out var profile))
                    continue;

                profilePresence |= (byte)(1 << i);
                profiles[i] = profile;
            }

            writer.Write(profilePresence);
            for (var i = 0; i < profiles.Length; i++)
            {
                if (profiles[i] is { } profile)
                    WriteSparseModeProfile(writer, profile, preset.HudLayouts, HudElementIds.All);
            }
        }

        raw.Position = 0;
        using var compressed = new MemoryStream(192);
        using (var brotli = new BrotliStream(compressed, CompressionLevel.SmallestSize, leaveOpen: true))
            raw.CopyTo(brotli);

        return Prefix + ToBase85(compressed.ToArray());
    }

    public static bool TryDecode(string code, out HudPresetData preset, out string error)
    {
        preset = new HudPresetData();
        error = string.Empty;
        if (string.IsNullOrWhiteSpace(code))
        {
            error = "Paste a RE:Frame HUD share code first.";
            return false;
        }

        var value = code.Trim();
        if (value.StartsWith(Prefix, StringComparison.OrdinalIgnoreCase))
            return TryDecodeRf3(value, out preset, out error);
        if (value.StartsWith(Rf2Prefix, StringComparison.OrdinalIgnoreCase))
            return TryDecodeRf2Compact(value, out preset, out error);
        if (value.StartsWith(LegacyPrefix, StringComparison.OrdinalIgnoreCase))
            return TryDecodeLegacy(value, out preset, out error);

        error = "This is not a RE:Frame RF3, RF2, or RFHUD1 share code.";
        return false;
    }

    private static bool TryDecodeRf3(string value, out HudPresetData preset, out string error)
    {
        preset = new HudPresetData();
        error = string.Empty;
        try
        {
            var compressed = FromBase85(value[Prefix.Length..]);
            if (compressed.Length == 0 || compressed.Length > 4096)
            {
                error = "The RF3 share code has an invalid size.";
                return false;
            }

            using var compressedStream = new MemoryStream(compressed);
            using var brotli = new BrotliStream(compressedStream, CompressionMode.Decompress);
            using var reader = new BinaryReader(brotli, Encoding.UTF8, leaveOpen: false);

            var version = reader.ReadByte();
            if (version is < 1 or > FormatVersion)
            {
                error = $"RF3 format version {version} is not supported.";
                return false;
            }

            preset.Name = NormalizeName(ReadShortString(reader));
            preset.SourceJob = NormalizeJob(ReadShortString(reader));
            UnpackFlags(reader.ReadUInt32(), preset, Rf2FormatVersion);
            if (version >= 2)
                UnpackRaidFlags(reader.ReadByte(), preset);

            var theme = reader.ReadByte();
            preset.SelectedTheme = Enum.IsDefined(typeof(ThemePreset), (int)theme)
                ? (ThemePreset)theme
                : ThemePreset.CornflowerSeafoam;
            preset.InterfaceScale = Dequantize8(reader.ReadByte(), 0.75f, 1.35f);
            preset.HudOpacity = Dequantize8(reader.ReadByte(), 0.35f, 1f);
            preset.HaloRadius = Dequantize8(reader.ReadByte(), 75f, 190f);
            preset.HaloThickness = Dequantize8(reader.ReadByte(), 2f, 12f);
            preset.HaloVerticalOffset = Dequantize8(reader.ReadByte(), -140f, 100f);
            if (version >= 5)
            {
                preset.ActionBarOneColumns = HotbarGridLayouts.NormalizeColumns(reader.ReadByte());
                preset.ActionBarTwoColumns = HotbarGridLayouts.NormalizeColumns(reader.ReadByte());
                preset.ActionBarThreeColumns = HotbarGridLayouts.NormalizeColumns(reader.ReadByte());
            }
            if (version >= 6)
                preset.PetBarColumns = HotbarGridLayouts.NormalizeColumns(reader.ReadByte());
            preset.CreatedUtc = DateTime.UtcNow;
            var elementIds = version switch
            {
                1 => HudElementIds.ShareCodeRf3V1,
                <= 6 => HudElementIds.ShareCodeRf3V6,
                _ => HudElementIds.All,
            };
            preset.HudLayouts = ReadSparseLayouts(reader, elementIds);
            preset.HudModeProfiles = new Dictionary<string, HudModeProfile>(StringComparer.OrdinalIgnoreCase);

            ReadSparsePlacements(reader, preset, version);

            var profilePresence = reader.ReadByte();
            var serializedModes = version >= 3 ? Rf3Modes : Rf3LegacyModes;
            var validProfileMask = (1 << serializedModes.Length) - 1;
            if ((profilePresence & ~validProfileMask) != 0)
                throw new InvalidDataException("The RF3 profile mask contains unsupported modes.");

            for (var i = 0; i < serializedModes.Length; i++)
            {
                if ((profilePresence & (1 << i)) == 0)
                    continue;

                var mode = serializedModes[i];
                preset.HudModeProfiles[HudModeProfileService.Key(mode)] = ReadSparseModeProfile(reader, elementIds);
            }

            if (preset.HudLayouts.Count == 0)
            {
                error = "The RF3 code did not contain a usable HUD layout.";
                return false;
            }

            return true;
        }
        catch (Exception ex)
        {
            error = $"The RF3 HUD share code could not be read: {ex.Message}";
            return false;
        }
    }

    private static bool TryResolveProfile(HudPresetData preset, UiMode mode, out HudModeProfile profile)
    {
        profile = null!;
        var profiles = preset.HudModeProfiles;
        if (profiles is null)
            return false;

        if (profiles.TryGetValue(HudModeProfileService.Key(mode), out var resolved) && resolved is not null)
        {
            profile = resolved;
            return true;
        }

        if (mode == UiMode.RaidReady &&
            profiles.TryGetValue(HudModeProfileService.SerializedKey(UiMode.Combat), out var legacyCombat) &&
            legacyCombat is not null)
        {
            profile = legacyCombat;
            return true;
        }

        return false;
    }

    private static void WriteSparseLayouts(
        BinaryWriter writer,
        IReadOnlyDictionary<string, HudElementLayout>? layouts,
        IReadOnlyDictionary<string, HudElementLayout>? sharedLayouts,
        IReadOnlyList<string> elementIds)
    {
        uint presence = 0;
        var values = new HudElementLayout?[elementIds.Count];
        if (layouts is not null)
        {
            for (var i = 0; i < elementIds.Count; i++)
            {
                var id = elementIds[i];
                if (!layouts.TryGetValue(id, out var layout) || layout is null)
                    continue;

                if (sharedLayouts is not null &&
                    sharedLayouts.TryGetValue(id, out var shared) &&
                    shared is not null &&
                    LayoutsEqualQuantized(layout, shared))
                {
                    continue;
                }

                presence |= 1u << i;
                values[i] = layout;
            }
        }

        writer.Write(presence);
        for (var i = 0; i < values.Length; i++)
        {
            if (values[i] is { } layout)
                WriteLayout(writer, layout);
        }
    }

    private static Dictionary<string, HudElementLayout> ReadSparseLayouts(BinaryReader reader, IReadOnlyList<string> elementIds)
    {
        var presence = reader.ReadUInt32();
        ValidateElementMask(presence, "layout", elementIds.Count);
        var layouts = new Dictionary<string, HudElementLayout>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < elementIds.Count; i++)
        {
            if ((presence & (1u << i)) != 0)
                layouts[elementIds[i]] = ReadLayout(reader);
        }

        return layouts;
    }

    private static void WriteSparseModeProfile(
        BinaryWriter writer,
        HudModeProfile profile,
        IReadOnlyDictionary<string, HudElementLayout>? sharedLayouts,
        IReadOnlyList<string> elementIds)
    {
        profile.EnsureValid();
        uint visibilityPresence = 0;
        uint visibilityValues = 0;
        uint lockValues = 0;
        for (var i = 0; i < elementIds.Count; i++)
        {
            var id = elementIds[i];
            if (profile.ElementVisibility.TryGetValue(id, out var visible))
            {
                visibilityPresence |= 1u << i;
                if (visible)
                    visibilityValues |= 1u << i;
            }

            if (profile.ElementLocks.TryGetValue(id, out var locked) && locked)
                lockValues |= 1u << i;
        }

        writer.Write(visibilityPresence);
        writer.Write(visibilityValues);
        writer.Write(lockValues);
        WriteSparseLayouts(writer, profile.HudLayouts, sharedLayouts, elementIds);
    }

    private static HudModeProfile ReadSparseModeProfile(BinaryReader reader, IReadOnlyList<string> elementIds)
    {
        var profile = new HudModeProfile();
        var visibilityPresence = reader.ReadUInt32();
        var visibilityValues = reader.ReadUInt32();
        var lockValues = reader.ReadUInt32();
        ValidateElementMask(visibilityPresence, "visibility", elementIds.Count);
        ValidateElementMask(visibilityValues, "visibility value", elementIds.Count);
        ValidateElementMask(lockValues, "lock", elementIds.Count);

        for (var i = 0; i < elementIds.Count; i++)
        {
            var bit = 1u << i;
            if ((visibilityPresence & bit) != 0)
                profile.ElementVisibility[elementIds[i]] = (visibilityValues & bit) != 0;
            if ((lockValues & bit) != 0)
                profile.ElementLocks[elementIds[i]] = true;
        }

        profile.HudLayouts = ReadSparseLayouts(reader, elementIds);
        return profile;
    }

    private static void WriteSparsePlacements(BinaryWriter writer, HudPresetData preset)
    {
        var placements = new[]
        {
            preset.JobGaugePlacement,
            preset.NativeStatusEffectsPlacement,
            preset.NativeScenarioGuidePlacement,
            preset.NativeQuestListPlacement,
            preset.NativeDutyInfoPlacement,
        };

        byte presence = 0;
        for (var i = 0; i < placements.Length; i++)
        {
            if (placements[i] is not null)
                presence |= (byte)(1 << i);
        }

        writer.Write(presence);
        foreach (var placement in placements)
        {
            if (placement is not null)
                WritePlacementPayload(writer, placement);
        }

        var components = preset.JobGaugePlacement?.Components?.Where(pair => !string.IsNullOrWhiteSpace(pair.Key) && pair.Value is not null)
            .OrderBy(pair => pair.Key, StringComparer.OrdinalIgnoreCase)
            .Take(16)
            .ToArray() ?? Array.Empty<KeyValuePair<string, NativeJobGaugePlacement>>();
        writer.Write((byte)components.Length);
        foreach (var (key, placement) in components)
        {
            WriteShortString(writer, key, 64);
            WritePlacementPayload(writer, placement);
        }
    }

    private static void ReadSparsePlacements(BinaryReader reader, HudPresetData preset, byte version)
    {
        var presence = reader.ReadByte();
        if ((presence & ~0x1F) != 0)
            throw new InvalidDataException("The RF3 native-placement mask is invalid.");

        NativeJobGaugePlacement? ReadAt(int bit)
            => (presence & (1 << bit)) != 0 ? ReadPlacementPayload(reader) : null;

        preset.JobGaugePlacement = ReadAt(0);
        preset.NativeStatusEffectsPlacement = ReadAt(1);
        preset.NativeScenarioGuidePlacement = ReadAt(2);
        preset.NativeQuestListPlacement = ReadAt(3);
        preset.NativeDutyInfoPlacement = ReadAt(4);

        if (version < 4)
            return;

        var componentCount = reader.ReadByte();
        if (componentCount > 16)
            throw new InvalidDataException("The RF3 job-gauge component count is invalid.");
        if (componentCount == 0)
            return;

        preset.JobGaugePlacement ??= new NativeJobGaugePlacement();
        preset.JobGaugePlacement.Components = new Dictionary<string, NativeJobGaugePlacement>(StringComparer.OrdinalIgnoreCase);
        for (var index = 0; index < componentCount; index++)
        {
            var key = ReadShortString(reader);
            if (string.IsNullOrWhiteSpace(key))
                throw new InvalidDataException("The RF3 job-gauge component key is invalid.");
            preset.JobGaugePlacement.Components[key] = ReadPlacementPayload(reader);
        }
    }

    private static void WriteLayout(BinaryWriter writer, HudElementLayout layout)
    {
        writer.Write(Quantize16(layout.X, -0.5f, 1.5f));
        writer.Write(Quantize16(layout.Y, -0.5f, 1.5f));
        writer.Write(Quantize16(layout.Width, 0f, 2f));
        writer.Write(Quantize16(layout.Height, 0f, 2f));
    }

    private static HudElementLayout ReadLayout(BinaryReader reader)
        => new()
        {
            X = Dequantize16(reader.ReadUInt16(), -0.5f, 1.5f),
            Y = Dequantize16(reader.ReadUInt16(), -0.5f, 1.5f),
            Width = Dequantize16(reader.ReadUInt16(), 0f, 2f),
            Height = Dequantize16(reader.ReadUInt16(), 0f, 2f),
        };

    private static bool LayoutsEqualQuantized(HudElementLayout left, HudElementLayout right)
        => Quantize16(left.X, -0.5f, 1.5f) == Quantize16(right.X, -0.5f, 1.5f) &&
           Quantize16(left.Y, -0.5f, 1.5f) == Quantize16(right.Y, -0.5f, 1.5f) &&
           Quantize16(left.Width, 0f, 2f) == Quantize16(right.Width, 0f, 2f) &&
           Quantize16(left.Height, 0f, 2f) == Quantize16(right.Height, 0f, 2f);

    private static void ValidateElementMask(uint mask, string label, int elementCount)
    {
        var validMask = elementCount >= 32 ? uint.MaxValue : (1u << elementCount) - 1u;
        if ((mask & ~validMask) != 0)
            throw new InvalidDataException($"The RF3 {label} mask contains unsupported elements.");
    }

    private static void WritePlacementPayload(BinaryWriter writer, NativeJobGaugePlacement placement)
    {
        writer.Write(QuantizeQuarter(placement.X));
        writer.Write(QuantizeQuarter(placement.Y));
        writer.Write(QuantizeQuarter(placement.OriginalX));
        writer.Write(QuantizeQuarter(placement.OriginalY));
        writer.Write(QuantizeScale(placement.Scale));
        writer.Write(QuantizeScale(placement.OriginalScale));
        writer.Write(placement.HasOriginal);
    }

    private static NativeJobGaugePlacement ReadPlacementPayload(BinaryReader reader)
        => new()
        {
            X = reader.ReadInt16() / 4f,
            Y = reader.ReadInt16() / 4f,
            OriginalX = reader.ReadInt16() / 4f,
            OriginalY = reader.ReadInt16() / 4f,
            Scale = reader.ReadUInt16() / 1000f,
            OriginalScale = reader.ReadUInt16() / 1000f,
            HasOriginal = reader.ReadBoolean(),
        };

    private static bool TryDecodeRf2Compact(string value, out HudPresetData preset, out string error)
    {
        preset = new HudPresetData();
        error = string.Empty;
        try
        {
            var compressed = FromBase64Url(value[Rf2Prefix.Length..]);
            if (compressed.Length == 0 || compressed.Length > 4096)
            {
                error = "The RF2 share code has an invalid size.";
                return false;
            }

            using var compressedStream = new MemoryStream(compressed);
            using var brotli = new BrotliStream(compressedStream, CompressionMode.Decompress);
            using var reader = new BinaryReader(brotli, Encoding.UTF8, leaveOpen: false);

            var version = reader.ReadByte();
            if (version is < 2 or > Rf2FormatVersion)
            {
                error = $"RF2 format version {version} is not supported.";
                return false;
            }

            preset.Name = NormalizeName(ReadShortString(reader));
            preset.SourceJob = NormalizeJob(ReadShortString(reader));
            UnpackFlags(reader.ReadUInt32(), preset, version);

            var theme = reader.ReadByte();
            preset.SelectedTheme = Enum.IsDefined(typeof(ThemePreset), (int)theme)
                ? (ThemePreset)theme
                : ThemePreset.CornflowerSeafoam;
            preset.InterfaceScale = Dequantize8(reader.ReadByte(), 0.75f, 1.35f);
            preset.HudOpacity = Dequantize8(reader.ReadByte(), 0.35f, 1f);
            preset.HaloRadius = Dequantize8(reader.ReadByte(), 75f, 190f);
            preset.HaloThickness = Dequantize8(reader.ReadByte(), 2f, 12f);
            preset.HaloVerticalOffset = Dequantize8(reader.ReadByte(), -140f, 100f);
            preset.CreatedUtc = DateTime.UtcNow;
            preset.HudLayouts = new Dictionary<string, HudElementLayout>(StringComparer.OrdinalIgnoreCase);
            preset.HudModeProfiles = new Dictionary<string, HudModeProfile>(StringComparer.OrdinalIgnoreCase);

            var layoutIds = version >= 11
                ? HudElementIds.ShareCodeV11
                : version >= 8
                    ? HudElementIds.ShareCodeV10
                : version >= 6
                    ? HudElementIds.ShareCodeV7
                    : version == 5
                        ? HudElementIds.ShareCodeV5
                        : version == 4
                            ? HudElementIds.ShareCodeV4
                            : HudElementIds.ShareCodeV3;
            foreach (var id in layoutIds)
            {
                var x = reader.ReadUInt16();
                var y = reader.ReadUInt16();
                var width = reader.ReadUInt16();
                var height = reader.ReadUInt16();
                if (x == MissingLayout)
                    continue;

                preset.HudLayouts[id] = new HudElementLayout
                {
                    X = Dequantize16(x, -0.5f, 1.5f),
                    Y = Dequantize16(y, -0.5f, 1.5f),
                    Width = Dequantize16(width, 0f, 2f),
                    Height = Dequantize16(height, 0f, 2f),
                };
            }

            if (version < 5)
                UpgradeLegacyLayouts(preset.HudLayouts);

            preset.JobGaugePlacement = ReadPlacement(reader);
            preset.NativeStatusEffectsPlacement = ReadPlacement(reader);
            if (version >= 3)
            {
                preset.NativeScenarioGuidePlacement = ReadPlacement(reader);
                preset.NativeQuestListPlacement = ReadPlacement(reader);
                preset.NativeDutyInfoPlacement = ReadPlacement(reader);
            }

            if (version >= 7)
            {
                var profileElementIds = version >= 11
                    ? HudElementIds.ShareCodeV11
                    : version >= 8
                        ? HudElementIds.ShareCodeV10
                        : HudElementIds.ShareCodeV7;
                var serializedModes = version >= 10
                    ? HudModeProfileService.SerializedModes
                    : HudModeProfileService.LegacySerializedModes;
                foreach (var mode in serializedModes)
                    ReadModeProfile(reader, preset, mode, profileElementIds, version);
            }

            if (preset.HudLayouts.Count == 0)
            {
                error = "The RF2 code did not contain a usable HUD layout.";
                return false;
            }

            return true;
        }
        catch (Exception ex)
        {
            error = $"The RF2 HUD share code could not be read: {ex.Message}";
            return false;
        }
    }

    private static bool TryDecodeLegacy(string value, out HudPresetData preset, out string error)
    {
        preset = new HudPresetData();
        error = string.Empty;
        try
        {
            var encoded = value[LegacyPrefix.Length..].Replace('-', '+').Replace('_', '/');
            encoded = encoded.PadRight(encoded.Length + ((4 - encoded.Length % 4) % 4), '=');
            var compressed = Convert.FromBase64String(encoded);
            using var input = new MemoryStream(compressed);
            using var gzip = new GZipStream(input, CompressionMode.Decompress);
            using var reader = new StreamReader(gzip, Encoding.UTF8);
            var json = reader.ReadToEnd();
            var decoded = JsonSerializer.Deserialize<HudPresetData>(json, LegacyJsonOptions);
            if (decoded is null || decoded.HudLayouts is null || decoded.HudLayouts.Count == 0)
            {
                error = "The legacy share code did not contain a usable HUD preset.";
                return false;
            }

            preset = decoded;
            preset.Name = NormalizeName(preset.Name);
            preset.SourceJob = NormalizeJob(preset.SourceJob);
            UpgradeLegacyLayouts(preset.HudLayouts);
            preset.HudModeProfiles = HudModeProfileService.CloneProfiles(preset.HudModeProfiles);
            return true;
        }
        catch (Exception ex)
        {
            error = $"The legacy HUD share code could not be read: {ex.Message}";
            return false;
        }
    }

    private static void WriteModeProfile(BinaryWriter writer, HudPresetData preset, UiMode mode)
    {
        var profiles = preset.HudModeProfiles;
        var profileKey = mode == UiMode.Combat
            ? HudModeProfileService.Key(UiMode.RaidReady)
            : HudModeProfileService.Key(mode);
        if (profiles is null ||
            !profiles.TryGetValue(profileKey, out var profile) ||
            profile is null)
        {
            writer.Write(false);
            return;
        }

        writer.Write(true);
        profile.EnsureValid();
        uint visibilityPresence = 0;
        uint visibilityValues = 0;
        for (var i = 0; i < HudElementIds.All.Length; i++)
        {
            var id = HudElementIds.All[i];
            if (!profile.ElementVisibility.TryGetValue(id, out var visible))
                continue;

            visibilityPresence |= 1u << i;
            if (visible)
                visibilityValues |= 1u << i;
        }

        writer.Write(visibilityPresence);
        writer.Write(visibilityValues);

        uint lockValues = 0;
        for (var i = 0; i < HudElementIds.All.Length; i++)
        {
            var id = HudElementIds.All[i];
            if (profile.ElementLocks.TryGetValue(id, out var locked) && locked)
                lockValues |= 1u << i;
        }
        writer.Write(lockValues);

        foreach (var id in HudElementIds.All)
        {
            if (!profile.HudLayouts.TryGetValue(id, out var layout) || layout is null)
            {
                writer.Write(MissingLayout);
                writer.Write(MissingLayout);
                writer.Write(MissingLayout);
                writer.Write(MissingLayout);
                continue;
            }

            writer.Write(Quantize16(layout.X, -0.5f, 1.5f));
            writer.Write(Quantize16(layout.Y, -0.5f, 1.5f));
            writer.Write(Quantize16(layout.Width, 0f, 2f));
            writer.Write(Quantize16(layout.Height, 0f, 2f));
        }
    }

    private static void ReadModeProfile(BinaryReader reader, HudPresetData preset, UiMode mode, IReadOnlyList<string> elementIds, byte version)
    {
        if (!reader.ReadBoolean())
            return;

        var profile = new HudModeProfile();
        var visibilityPresence = reader.ReadUInt32();
        var visibilityValues = reader.ReadUInt32();
        var lockValues = version >= 9 ? reader.ReadUInt32() : 0u;
        for (var i = 0; i < elementIds.Count; i++)
        {
            var bit = 1u << i;
            if ((visibilityPresence & bit) != 0)
                profile.ElementVisibility[elementIds[i]] = (visibilityValues & bit) != 0;
            if ((lockValues & bit) != 0)
                profile.ElementLocks[elementIds[i]] = true;
        }

        foreach (var id in elementIds)
        {
            var x = reader.ReadUInt16();
            var y = reader.ReadUInt16();
            var width = reader.ReadUInt16();
            var height = reader.ReadUInt16();
            if (x == MissingLayout)
                continue;

            profile.HudLayouts[id] = new HudElementLayout
            {
                X = Dequantize16(x, -0.5f, 1.5f),
                Y = Dequantize16(y, -0.5f, 1.5f),
                Width = Dequantize16(width, 0f, 2f),
                Height = Dequantize16(height, 0f, 2f),
            };
        }

        if (mode == UiMode.Combat)
        {


            preset.HudModeProfiles[HudModeProfileService.SerializedKey(mode)] = profile;
            var raidKey = HudModeProfileService.Key(UiMode.RaidReady);
            if (!preset.HudModeProfiles.ContainsKey(raidKey))
                preset.HudModeProfiles[raidKey] = profile.Clone();
        }
        else
        {
            preset.HudModeProfiles[HudModeProfileService.Key(mode)] = profile;
        }
    }

    private static byte PackRaidFlags(HudPresetData preset)
    {
        byte flags = 0;
        if (preset.ShowRaidBuffs) flags |= 1 << 0;
        if (preset.ShowRaidDebuffs) flags |= 1 << 1;
        if (preset.ShowRaidersKit) flags |= 1 << 2;
        return flags;
    }

    private static void UnpackRaidFlags(byte flags, HudPresetData preset)
    {
        preset.ShowRaidBuffs = (flags & (1 << 0)) != 0;
        preset.ShowRaidDebuffs = (flags & (1 << 1)) != 0;
        preset.ShowRaidersKit = (flags & (1 << 2)) != 0;
    }

    private static uint PackFlags(HudPresetData preset)
    {
        uint flags = 0;
        SetFlag(ref flags, 0, preset.ShowLocationFrame);
        SetFlag(ref flags, 1, preset.ShowJobRibbon);
        SetFlag(ref flags, 2, preset.ShowMinimapFrame);
        SetFlag(ref flags, 3, preset.ShowChatFrame);
        SetFlag(ref flags, 4, preset.ShowPartyFrames);
        SetFlag(ref flags, 5, preset.ShowPlayerFrame);
        SetFlag(ref flags, 6, preset.ShowTargetFrame);
        SetFlag(ref flags, 7, preset.ShowFocusFrame);
        SetFlag(ref flags, 8, preset.ShowEnemyList);
        SetFlag(ref flags, 9, preset.ShowActionBarFrames);
        SetFlag(ref flags, 10, preset.ShowPetBar);
        SetFlag(ref flags, 11, preset.ShowUtilityBarFrames);
        SetFlag(ref flags, 12, preset.ShowRaidTools);
        SetFlag(ref flags, 13, preset.ShowCombatHalo);
        SetFlag(ref flags, 14, preset.ShowCombatHaloInRaidReady);
        SetFlag(ref flags, 15, preset.ShowLeisureDock);
        SetFlag(ref flags, 16, preset.HaloFollowsPlayer);
        SetFlag(ref flags, 17, preset.FrameNativeHoldouts);
        SetFlag(ref flags, 18, preset.FollowJobColors);
        SetFlag(ref flags, 19, preset.ShowAllianceFrames);
        SetFlag(ref flags, 20, preset.ShowAllianceFrameOne);
        SetFlag(ref flags, 21, preset.ShowAllianceFrameTwo);
        SetFlag(ref flags, 22, preset.ShowTargetOfTargetFrame);
        SetFlag(ref flags, 23, preset.ShowCastBar);
        SetFlag(ref flags, 24, preset.ShowActionBarOne);
        SetFlag(ref flags, 25, preset.ShowActionBarTwo);
        SetFlag(ref flags, 26, preset.ShowActionBarThree);
        SetFlag(ref flags, 27, preset.ShowGreeting);
        SetFlag(ref flags, 28, preset.ShowCrossHotbar);
        SetFlag(ref flags, 29, preset.ShowPocketRibbon);
        return flags;
    }

    private static void UnpackFlags(uint flags, HudPresetData preset, byte version)
    {
        preset.ShowLocationFrame = HasFlag(flags, 0);
        preset.ShowJobRibbon = HasFlag(flags, 1);
        preset.ShowMinimapFrame = HasFlag(flags, 2);
        preset.ShowChatFrame = HasFlag(flags, 3);
        preset.ShowPartyFrames = HasFlag(flags, 4);
        preset.ShowPlayerFrame = HasFlag(flags, 5);
        preset.ShowTargetFrame = HasFlag(flags, 6);
        preset.ShowFocusFrame = HasFlag(flags, 7);
        preset.ShowEnemyList = HasFlag(flags, 8);
        preset.ShowActionBarFrames = HasFlag(flags, 9);
        preset.ShowPetBar = HasFlag(flags, 10);
        preset.ShowUtilityBarFrames = HasFlag(flags, 11);
        preset.ShowRaidTools = HasFlag(flags, 12);
        preset.ShowCombatHalo = HasFlag(flags, 13);
        preset.ShowCombatHaloInRaidReady = HasFlag(flags, 14);
        preset.ShowLeisureDock = HasFlag(flags, 15);
        preset.HaloFollowsPlayer = HasFlag(flags, 16);
        preset.FrameNativeHoldouts = HasFlag(flags, 17);
        preset.FollowJobColors = HasFlag(flags, 18);
        preset.ShowAllianceFrames = version >= 4 ? HasFlag(flags, 19) : true;
        preset.ShowAllianceFrameOne = version >= 5 ? HasFlag(flags, 20) : true;
        preset.ShowAllianceFrameTwo = version >= 5 ? HasFlag(flags, 21) : true;
        preset.ShowTargetOfTargetFrame = version >= 5 ? HasFlag(flags, 22) : true;
        preset.ShowCastBar = version >= 5 ? HasFlag(flags, 23) : true;
        preset.ShowActionBarOne = version >= 5 ? HasFlag(flags, 24) : true;
        preset.ShowActionBarTwo = version >= 5 ? HasFlag(flags, 25) : true;
        preset.ShowActionBarThree = version >= 5 ? HasFlag(flags, 26) : true;
        preset.ShowGreeting = version >= 6 ? HasFlag(flags, 27) : true;
        preset.ShowCrossHotbar = version >= 8 ? HasFlag(flags, 28) : true;
        preset.ShowPocketRibbon = version >= 11 ? HasFlag(flags, 29) : true;
    }

    private static void UpgradeLegacyLayouts(Dictionary<string, HudElementLayout> layouts)
    {
        if (layouts.TryGetValue(HudElementIds.Alliance, out var alliance))
        {
            var gap = 8f / 1920f;
            var width = MathF.Max(180f / 1920f, (alliance.Width - gap) * 0.5f);
            layouts.TryAdd(HudElementIds.AllianceOne, new HudElementLayout
            {
                X = alliance.X,
                Y = alliance.Y,
                Width = width,
                Height = alliance.Height,
            });
            layouts.TryAdd(HudElementIds.AllianceTwo, new HudElementLayout
            {
                X = alliance.X + width + gap,
                Y = alliance.Y,
                Width = width,
                Height = alliance.Height,
            });
        }

        if (layouts.TryGetValue(HudElementIds.ActionBars, out var actionBars))
        {
            var gap = 3f / 1080f;
            var height = MathF.Max(32f / 1080f, (actionBars.Height - gap * 2f) / 3f);
            layouts.TryAdd(HudElementIds.ActionBarThree, CloneLayout(actionBars, actionBars.X, actionBars.Y, actionBars.Width, height));
            layouts.TryAdd(HudElementIds.ActionBarTwo, CloneLayout(actionBars, actionBars.X, actionBars.Y + height + gap, actionBars.Width, height));
            layouts.TryAdd(HudElementIds.ActionBarOne, CloneLayout(actionBars, actionBars.X, actionBars.Y + (height + gap) * 2f, actionBars.Width, height));
        }

        if (layouts.TryGetValue(HudElementIds.Target, out var target))
        {
            var gapX = 8f / 1920f;
            var gapY = 6f / 1080f;
            var targetHeight = MathF.Max(58f / 1080f, target.Height * 0.68f);
            target.Height = targetHeight;
            var castWidth = MathF.Max(220f / 1920f, target.Width * 0.60f - gapX * 0.5f);
            var targetOfTargetWidth = MathF.Max(170f / 1920f, target.Width - castWidth - gapX);
            var lowerY = target.Y + targetHeight + gapY;
            layouts.TryAdd(HudElementIds.CastBar, CloneLayout(target, target.X, lowerY, castWidth, 42f / 1080f));
            layouts.TryAdd(HudElementIds.TargetOfTarget, CloneLayout(target, target.X + castWidth + gapX, lowerY, targetOfTargetWidth, 42f / 1080f));
        }
    }

    private static HudElementLayout CloneLayout(HudElementLayout source, float x, float y, float width, float height)
        => new() { X = x, Y = y, Width = width, Height = height };

    private static void SetFlag(ref uint flags, int bit, bool enabled)
    {
        if (enabled)
            flags |= 1u << bit;
    }

    private static bool HasFlag(uint flags, int bit) => (flags & (1u << bit)) != 0;

    private static void WritePlacement(BinaryWriter writer, NativeJobGaugePlacement? placement)
    {
        writer.Write(placement is not null);
        if (placement is null)
            return;

        writer.Write(QuantizeQuarter(placement.X));
        writer.Write(QuantizeQuarter(placement.Y));
        writer.Write(QuantizeQuarter(placement.OriginalX));
        writer.Write(QuantizeQuarter(placement.OriginalY));
        writer.Write(QuantizeScale(placement.Scale));
        writer.Write(QuantizeScale(placement.OriginalScale));
        writer.Write(placement.HasOriginal);
    }

    private static NativeJobGaugePlacement? ReadPlacement(BinaryReader reader)
    {
        if (!reader.ReadBoolean())
            return null;

        return new NativeJobGaugePlacement
        {
            X = reader.ReadInt16() / 4f,
            Y = reader.ReadInt16() / 4f,
            OriginalX = reader.ReadInt16() / 4f,
            OriginalY = reader.ReadInt16() / 4f,
            Scale = reader.ReadUInt16() / 1000f,
            OriginalScale = reader.ReadUInt16() / 1000f,
            HasOriginal = reader.ReadBoolean(),
        };
    }

    private static short QuantizeQuarter(float value)
        => (short)Math.Clamp((int)MathF.Round(value * 4f), short.MinValue, short.MaxValue);

    private static ushort QuantizeScale(float value)
        => (ushort)Math.Clamp((int)MathF.Round(value * 1000f), 0, ushort.MaxValue);

    private static byte Quantize8(float value, float minimum, float maximum)
    {
        var normalized = (Math.Clamp(value, minimum, maximum) - minimum) / (maximum - minimum);
        return (byte)Math.Clamp((int)MathF.Round(normalized * byte.MaxValue), 0, byte.MaxValue);
    }

    private static float Dequantize8(byte value, float minimum, float maximum)
        => minimum + (value / (float)byte.MaxValue * (maximum - minimum));

    private static ushort Quantize16(float value, float minimum, float maximum)
    {
        var normalized = (Math.Clamp(value, minimum, maximum) - minimum) / (maximum - minimum);
        return (ushort)Math.Clamp((int)MathF.Round(normalized * QuantizedMaximum), 0, QuantizedMaximum);
    }

    private static float Dequantize16(ushort value, float minimum, float maximum)
        => minimum + (value / (float)QuantizedMaximum * (maximum - minimum));

    private static void WriteShortString(BinaryWriter writer, string? value, int maximumBytes)
    {
        var normalized = string.IsNullOrWhiteSpace(value) ? string.Empty : value.Trim();
        var bytes = Encoding.UTF8.GetBytes(normalized);
        if (bytes.Length > maximumBytes)
            Array.Resize(ref bytes, maximumBytes);
        writer.Write((byte)bytes.Length);
        writer.Write(bytes);
    }

    private static string ReadShortString(BinaryReader reader)
    {
        var length = reader.ReadByte();
        return Encoding.UTF8.GetString(reader.ReadBytes(length));
    }

    private static string ToBase85(byte[] bytes)
    {
        if (bytes.Length == 0)
            return string.Empty;

        var result = new StringBuilder((bytes.Length * 5 + 3) / 4);
        for (var offset = 0; offset < bytes.Length; offset += 4)
        {
            var byteCount = Math.Min(4, bytes.Length - offset);
            uint value = 0;
            for (var i = 0; i < 4; i++)
            {
                value <<= 8;
                if (i < byteCount)
                    value |= bytes[offset + i];
            }

            Span<char> block = stackalloc char[5];
            for (var i = 4; i >= 0; i--)
            {
                block[i] = Base85Alphabet[(int)(value % 85)];
                value /= 85;
            }

            for (var i = 0; i < byteCount + 1; i++)
                result.Append(block[i]);
        }

        return result.ToString();
    }

    private static byte[] FromBase85(string encoded)
    {
        if (string.IsNullOrEmpty(encoded))
            return Array.Empty<byte>();
        if (encoded.Length % 5 == 1)
            throw new FormatException("The RF3 Base85 payload has an invalid length.");

        using var output = new MemoryStream((encoded.Length / 5) * 4 + 4);
        for (var offset = 0; offset < encoded.Length; offset += 5)
        {
            var charCount = Math.Min(5, encoded.Length - offset);
            ulong value = 0;
            for (var i = 0; i < 5; i++)
            {
                var digit = i < charCount
                    ? Base85Alphabet.IndexOf(encoded[offset + i])
                    : 84;
                if (digit < 0)
                    throw new FormatException($"The RF3 Base85 payload contains unsupported character '{encoded[offset + i]}'.");

                value = value * 85UL + (uint)digit;
            }

            if (value > uint.MaxValue)
                throw new FormatException("The RF3 Base85 payload contains an out-of-range block.");

            var packed = (uint)value;
            var byteCount = charCount == 5 ? 4 : charCount - 1;
            for (var i = 0; i < byteCount; i++)
                output.WriteByte((byte)(packed >> (24 - i * 8)));
        }

        return output.ToArray();
    }

    private static string ToBase64Url(byte[] bytes)
        => Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static byte[] FromBase64Url(string encoded)
    {
        encoded = encoded.Replace('-', '+').Replace('_', '/');
        encoded = encoded.PadRight(encoded.Length + ((4 - encoded.Length % 4) % 4), '=');
        return Convert.FromBase64String(encoded);
    }

    private static string NormalizeName(string? value)
        => string.IsNullOrWhiteSpace(value) ? "Imported HUD" : value.Trim();

    private static string NormalizeJob(string? value)
        => string.IsNullOrWhiteSpace(value) ? "XIV" : value.Trim().ToUpperInvariant();
}
