using System.Diagnostics;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;

namespace JLVoice;

internal enum CloudFailure { Consent, Key, Language, Unauthorized, Forbidden, RateLimited, Service, Network, Timeout, Response }
internal sealed class CloudSpeechException(CloudFailure failure, int? statusCode = null) : Exception("Cloud speech request failed.")
{
    internal CloudFailure Failure { get; } = failure;
    // HTTP status number only (never the response body); null when no HTTP response was received.
    internal int? StatusCode { get; } = statusCode;
}

// Shared by the WPF app and developer probe. No fallback, retries, telemetry or key storage.
internal static class NvidiaSpeechClient
{
    internal const string Endpoint = "https://2b940e91-a70e-4483-a958-d50408b96589.invocation.api.nvcf.nvidia.com/v1/audio/transcriptions";
    private static readonly Lazy<HttpClient> client = new(() => new HttpClient(new HttpClientHandler
    { AllowAutoRedirect = false, UseCookies = false }) { Timeout = Timeout.InfiniteTimeSpan });
    // Extra: names and short scalar values of response fields other than "text" (for example a language code, if the
    // server ever returns one). Bounded, secret-checked, never the raw body.
    internal sealed record Result(string Text, double RequestMs, IReadOnlyDictionary<string, string>? Extra = null);
    private static IReadOnlyDictionary<string, string> ExtraFields(JsonElement root, string key)
    {
        var extra = new Dictionary<string, string>();
        foreach (var property in root.EnumerateObject())
        {
            if (property.Name == "text" || extra.Count >= 20 || property.Name.Length > 40 || property.Name.Contains(key, StringComparison.Ordinal)) continue;
            var value = property.Value.ValueKind switch
            {
                JsonValueKind.String => property.Value.GetString() ?? "", JsonValueKind.Number or JsonValueKind.True or JsonValueKind.False => property.Value.GetRawText(),
                JsonValueKind.Null => "null", JsonValueKind.Array => $"<array of {property.Value.GetArrayLength()}>", _ => "<object>"
            };
            extra[property.Name] = value.Length > 64 || value.Contains(key, StringComparison.Ordinal) ? "<omitted>" : value;
        }
        return extra;
    }
    internal static bool ValidKey(string? key) => !string.IsNullOrWhiteSpace(key) && key.Length <= 1280 &&
        key.All(ch => char.IsAsciiLetterOrDigit(ch) || "-._~+/=".Contains(ch));

    internal static Task<Result> TranscribeAsync(byte[] wav, string key, string language, bool consent, CancellationToken token)
        => TranscribeAsync(client.Value, wav, key, language, consent, token);

    internal static Task<Result> TranscribeAsync(HttpClient http, byte[] wav, string key, string language, bool consent, CancellationToken token)
    {
        // Validate before constructing/sending any request, including in tests.
        if (!consent) throw new CloudSpeechException(CloudFailure.Consent);
        if (!ValidKey(key)) throw new CloudSpeechException(CloudFailure.Key);
        var locale = language switch { "cs" => "cs-CZ", "en" => "en-GB", _ => throw new CloudSpeechException(CloudFailure.Language) };
        return SendAsync(http, wav, key, locale, token);
    }

    // Developer language-contract experiment only (tools/JLVoice.NvidiaProbe). The app never calls this:
    // it sends any of a fixed set of language values, or omits the field (null), on the same hardened request path.
    internal static readonly string?[] ExperimentLocales = ["cs-CZ", "cs", "multi", "en-GB", "en-US", null];
    internal static Task<Result> TranscribeExperimentalAsync(HttpClient http, byte[] wav, string key, string? locale, CancellationToken token)
    {
        if (!ValidKey(key)) throw new CloudSpeechException(CloudFailure.Key);
        if (!ExperimentLocales.Contains(locale)) throw new CloudSpeechException(CloudFailure.Language);
        return SendAsync(http, wav, key, locale, token);
    }

    private static async Task<Result> SendAsync(HttpClient http, byte[] wav, string key, string? locale, CancellationToken token)
    {
        if (wav.Length < 16000 || wav.Length > 16000 * 2 * 90 + 65536) throw new InvalidDataException("Unsupported audio size.");
        token.ThrowIfCancellationRequested();
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token);
        deadline.CancelAfter(TimeSpan.FromSeconds(120));
        try
        {
            using var form = new MultipartFormDataContent();
            if (locale is not null) form.Add(new StringContent(locale), "language");
            var file = new ByteArrayContent(wav); file.Headers.ContentType = new MediaTypeHeaderValue("audio/wav");
            form.Add(file, "file", "dictation.wav");
            using var request = new HttpRequestMessage(HttpMethod.Post, Endpoint) { Content = form };
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", key);
            var clock = Stopwatch.StartNew();
            using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, deadline.Token);
            if (!response.IsSuccessStatusCode)
                throw new CloudSpeechException((int)response.StatusCode switch
                { 401 => CloudFailure.Unauthorized, 403 => CloudFailure.Forbidden, 429 => CloudFailure.RateLimited, _ => CloudFailure.Service }, (int)response.StatusCode);
            using var body = await response.Content.ReadAsStreamAsync(deadline.Token);
            using var limited = new MemoryStream(); var buffer = new byte[8192];
            for (int count; (count = await body.ReadAsync(buffer.AsMemory(), deadline.Token)) != 0;)
            {
                if (limited.Length + count > 1024 * 1024) throw new CloudSpeechException(CloudFailure.Response, (int)response.StatusCode);
                limited.Write(buffer, 0, count);
            }
            using var json = JsonDocument.Parse(limited.ToArray());
            if (json.RootElement.ValueKind != JsonValueKind.Object || !json.RootElement.TryGetProperty("text", out var value) || value.ValueKind != JsonValueKind.String)
                throw new CloudSpeechException(CloudFailure.Response, (int)response.StatusCode);
            var text = value.GetString()!.Trim();
            // HTTP 200 with an empty or key-echoing transcript: status is kept so experiments can tell it from a transport failure.
            if (text.Length == 0 || text.Contains(key, StringComparison.Ordinal)) throw new CloudSpeechException(CloudFailure.Response, (int)response.StatusCode);
            token.ThrowIfCancellationRequested();
            return new(text, clock.Elapsed.TotalMilliseconds, ExtraFields(json.RootElement, key));
        }
        catch (OperationCanceledException) when (!token.IsCancellationRequested) { throw new CloudSpeechException(CloudFailure.Timeout); }
        catch (HttpRequestException) { throw new CloudSpeechException(CloudFailure.Network); }
        catch (IOException) { throw new CloudSpeechException(CloudFailure.Network); }
        catch (JsonException) { throw new CloudSpeechException(CloudFailure.Response); }
    }
}
