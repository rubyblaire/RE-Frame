using System;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace REFrameXIV.Services;


public static class HudPresetShortCodeService
{
    public const string Prefix = "RF4-";
    private const int MaximumPortableCodeLength = 8192;
    private const int MaximumKeyLength = 32;

    public static bool IsShortCode(string? code)
    {
        if (string.IsNullOrWhiteSpace(code)) return false;
        var value = code.TrimStart();
        return value.StartsWith("RF4-", StringComparison.OrdinalIgnoreCase) ||
               value.StartsWith("RF4:", StringComparison.OrdinalIgnoreCase);
    }

    public static async Task<ShortCodeResult> PublishAsync(
        string portableCode,
        CancellationToken cancellationToken = default)
    {
        portableCode = portableCode?.Trim() ?? string.Empty;
        if (!TryNormalizeEndpoint(Rf4BackendSettings.Endpoint, out var endpointUri, out var endpointError))
            return ShortCodeResult.Fail(endpointError);
        if (!LooksLikePortableCode(portableCode))
            return ShortCodeResult.Fail("Only RF3, RF2, or RFHUD1 preset data can be turned into an RF4 short code.");
        if (portableCode.Length > MaximumPortableCodeLength)
            return ShortCodeResult.Fail("The preset payload is too large for the short-code service.");

        try
        {
            using var client = CreateClient();
            var requestJson = JsonSerializer.Serialize(new { action = "publish", payload = portableCode });
            using var request = new HttpRequestMessage(HttpMethod.Post, endpointUri)
            {
                Content = new StringContent(requestJson, Encoding.UTF8, "application/json"),
            };
            using var response = await client.SendAsync(request, HttpCompletionOption.ResponseContentRead, cancellationToken)
                .ConfigureAwait(false);
            var responseText = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
                return ShortCodeResult.Fail($"The RE:Frame short-code server rejected the upload ({(int)response.StatusCode}).");

            if (!TryReadResponse(responseText, out var success, out var value, out var message))
                return ShortCodeResult.Fail("The RE:Frame short-code server returned an unreadable response.");
            if (!success)
                return ShortCodeResult.Fail(string.IsNullOrWhiteSpace(message) ? "The RE:Frame short-code server rejected the upload." : message);

            if (!TryNormalizeKey(value, out var normalizedKey, out var keyError))
                return ShortCodeResult.Fail($"The RE:Frame short-code server returned an unusable key: {keyError}");

            var shortCode = FormatShortCode(normalizedKey);
            return ShortCodeResult.Ok(shortCode, $"Created {shortCode} ({shortCode.Length} characters).");
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return ShortCodeResult.Fail("The RE:Frame short-code server timed out. Your offline RF3 backup is still available.");
        }
        catch (OperationCanceledException)
        {
            return ShortCodeResult.Fail("The short-code request was cancelled.");
        }
        catch (HttpRequestException ex)
        {
            return ShortCodeResult.Fail($"Could not reach your RE:Frame Google Script: {ex.Message}");
        }
        catch (Exception ex)
        {
            return ShortCodeResult.Fail($"Could not create the RF4 short code: {ex.Message}");
        }
    }

    public static async Task<ShortCodeResult> ResolveAsync(
        string shortCode,
        CancellationToken cancellationToken = default)
    {
        if (!TryNormalizeEndpoint(Rf4BackendSettings.Endpoint, out var endpointUri, out var endpointError))
            return ShortCodeResult.Fail(endpointError);
        if (!TryExtractKey(shortCode, out var key, out var error))
            return ShortCodeResult.Fail(error);

        try
        {
            using var client = CreateClient();
            var separator = endpointUri.Query.Length == 0 ? "?" : "&";
            var lookupUri = new Uri(endpointUri + separator + "action=resolve&code=" + Uri.EscapeDataString(key));
            using var response = await client.GetAsync(lookupUri, cancellationToken).ConfigureAwait(false);
            var responseText = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
                return ShortCodeResult.Fail($"That RF4 code could not be retrieved ({(int)response.StatusCode}).");
            if (!TryReadResponse(responseText, out var success, out var value, out var message))
                return ShortCodeResult.Fail("The RE:Frame short-code server returned an unreadable response.");
            if (!success)
                return ShortCodeResult.Fail(string.IsNullOrWhiteSpace(message) ? "That RF4 code was not found." : message);

            var portableCode = value.Trim();
            if (portableCode.Length == 0 || portableCode.Length > MaximumPortableCodeLength)
                return ShortCodeResult.Fail("That RF4 code returned an invalid preset payload.");
            if (!LooksLikePortableCode(portableCode))
                return ShortCodeResult.Fail("That RF4 lookup does not contain a RE:Frame HUD preset.");

            return ShortCodeResult.Ok(portableCode, "RF4 short code resolved successfully.");
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return ShortCodeResult.Fail("The RF4 lookup timed out. Check your connection and try again.");
        }
        catch (OperationCanceledException)
        {
            return ShortCodeResult.Fail("The RF4 lookup was cancelled.");
        }
        catch (HttpRequestException ex)
        {
            return ShortCodeResult.Fail($"Could not reach your RE:Frame Google Script: {ex.Message}");
        }
        catch (Exception ex)
        {
            return ShortCodeResult.Fail($"Could not resolve the RF4 short code: {ex.Message}");
        }
    }

    private static HttpClient CreateClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromSeconds(20) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("REFrameXIV/0.4.0.66");
        client.DefaultRequestHeaders.Accept.ParseAdd("application/json");
        return client;
    }

    private static bool TryNormalizeEndpoint(string? endpoint, out Uri uri, out string error)
    {
        uri = null!;
        error = string.Empty;
        var value = endpoint?.Trim() ?? string.Empty;
        if (value.Length == 0)
        {
            error = "The built-in RE:Frame short-code service has not been configured by the developer.";
            return false;
        }
        if (!Uri.TryCreate(value, UriKind.Absolute, out uri!) || uri.Scheme != Uri.UriSchemeHttps)
        {
            error = "The built-in RE:Frame short-code service endpoint is invalid.";
            return false;
        }
        return true;
    }

    private static bool TryReadResponse(string responseText, out bool success, out string value, out string message)
    {
        success = false;
        value = string.Empty;
        message = string.Empty;
        try
        {
            using var json = JsonDocument.Parse(responseText);
            var root = json.RootElement;
            success = root.TryGetProperty("ok", out var okElement) && okElement.GetBoolean();
            if (root.TryGetProperty("value", out var valueElement)) value = valueElement.GetString() ?? string.Empty;
            if (root.TryGetProperty("message", out var messageElement)) message = messageElement.GetString() ?? string.Empty;
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static bool LooksLikePortableCode(string value) =>
        value.StartsWith("RF3:", StringComparison.OrdinalIgnoreCase) ||
        value.StartsWith("RF2:", StringComparison.OrdinalIgnoreCase) ||
        value.StartsWith("RFHUD1:", StringComparison.OrdinalIgnoreCase);

    private static bool TryExtractKey(string value, out string key, out string error)
    {
        key = string.Empty;
        error = string.Empty;
        if (string.IsNullOrWhiteSpace(value))
        {
            error = "Paste an RF4 short code first.";
            return false;
        }
        var trimmed = value.Trim();
        if (trimmed.StartsWith("RF4-", StringComparison.OrdinalIgnoreCase))
            trimmed = trimmed[4..];
        else if (trimmed.StartsWith("RF4:", StringComparison.OrdinalIgnoreCase))
            trimmed = trimmed[4..];
        else
        {
            error = "This is not an RF4 short code.";
            return false;
        }
        return TryNormalizeKey(trimmed, out key, out error);
    }

    private static bool TryNormalizeKey(string? value, out string key, out string error)
    {
        key = new string((value ?? string.Empty).Where(char.IsAsciiLetterOrDigit).ToArray()).ToUpperInvariant();
        error = string.Empty;
        if (key.Length != 8)
        {
            error = "the key must contain exactly eight letters or numbers";
            return false;
        }
        return true;
    }

    private static string FormatShortCode(string key) => $"{Prefix}{key[..4]}-{key[4..]}";
}

public readonly record struct ShortCodeResult(bool Success, string Value, string Message)
{
    public static ShortCodeResult Ok(string value, string message) => new(true, value, message);
    public static ShortCodeResult Fail(string message) => new(false, string.Empty, message);
}
