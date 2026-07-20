using System;
using System.IO;
using System.IO.Compression;
using System.Text;
using System.Text.Json;

namespace REFrameXIV.Theme;

public static class ForgeThemeCodec
{
    public const string Prefix = "REFORGE1:";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        IncludeFields = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false,
    };

    public static string Encode(ForgeThemeDefinition theme)
    {
        theme.Normalize();
        var envelope = new ForgeThemeEnvelope
        {
            Version = 1,
            Theme = theme.Clone(theme.Name),
        };
        var json = JsonSerializer.Serialize(envelope, JsonOptions);
        var input = Encoding.UTF8.GetBytes(json);
        using var compressed = new MemoryStream();
        using (var gzip = new GZipStream(compressed, CompressionLevel.SmallestSize, leaveOpen: true))
            gzip.Write(input, 0, input.Length);
        return Prefix + ToBase64Url(compressed.ToArray());
    }

    public static bool TryDecode(string? code, out ForgeThemeDefinition? theme, out string error)
    {
        theme = null;
        error = string.Empty;

        var value = code?.Trim() ?? string.Empty;
        if (!value.StartsWith(Prefix, StringComparison.OrdinalIgnoreCase))
        {
            error = "That is not a RE:Forge theme code.";
            return false;
        }

        try
        {
            var packed = FromBase64Url(value[Prefix.Length..]);
            using var input = new MemoryStream(packed);
            using var gzip = new GZipStream(input, CompressionMode.Decompress);
            using var output = new MemoryStream();
            var buffer = new byte[8192];
            var total = 0;
            while (true)
            {
                var read = gzip.Read(buffer, 0, buffer.Length);
                if (read <= 0)
                    break;

                total += read;
                if (total > 128 * 1024)
                {
                    error = "That RE:Forge theme code is larger than the supported limit.";
                    return false;
                }

                output.Write(buffer, 0, read);
            }

            var json = Encoding.UTF8.GetString(output.ToArray());
            var envelope = JsonSerializer.Deserialize<ForgeThemeEnvelope>(json, JsonOptions);
            if (envelope?.Version != 1 || envelope.Theme is null)
            {
                error = "This RE:Forge theme code is unsupported or incomplete.";
                return false;
            }

            theme = envelope.Theme;
            theme.Id = Guid.NewGuid().ToString("N");
            theme.Name = string.IsNullOrWhiteSpace(theme.Name) ? "Imported Forge" : $"{theme.Name} Imported";
            theme.CreatedUtc = DateTime.UtcNow;
            theme.ModifiedUtc = DateTime.UtcNow;
            theme.Normalize();
            return true;
        }
        catch (Exception)
        {
            error = "The RE:Forge theme code could not be read.";
            return false;
        }
    }

    private static string ToBase64Url(byte[] bytes) => Convert.ToBase64String(bytes)
        .TrimEnd('=')
        .Replace('+', '-')
        .Replace('/', '_');

    private static byte[] FromBase64Url(string value)
    {
        var padded = value.Replace('-', '+').Replace('_', '/');
        padded += (padded.Length % 4) switch
        {
            2 => "==",
            3 => "=",
            _ => string.Empty,
        };
        return Convert.FromBase64String(padded);
    }

    private sealed class ForgeThemeEnvelope
    {
        public int Version { get; set; }
        public ForgeThemeDefinition? Theme { get; set; }
    }
}
