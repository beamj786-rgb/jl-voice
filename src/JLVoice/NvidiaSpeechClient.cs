using System.Diagnostics;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;

namespace JLVoice;

internal enum CloudFailure { Consent, Key, Language, Unauthorized, Forbidden, RateLimited, Service, Network, Timeout, Response }
internal sealed class CloudSpeechException(CloudFailure failure) : Exception("Cloud speech request failed.")
{
    internal CloudFailure Failure { get; } = failure;
}

// Shared by the WPF app and developer probe. No fallback, retries, telemetry or key storage.
internal static class NvidiaSpeechClient
{
    internal const string Endpoint = "https://2b940e91-a70e-4483-a958-d50408b96589.invocation.api.nvcf.nvidia.com/v1/audio/transcriptions";
    private static readonly Lazy<HttpClient> client = new(() => new HttpClient(new HttpClientHandler
    { AllowAutoRedirect = false, UseCookies = false }) { Timeout = Timeout.InfiniteTimeSpan });
    internal sealed record Result(string Text, double RequestMs);
    internal static bool ValidKey(string? key) => !string.IsNullOrWhiteSpace(key) && key.Length <= 1280 &&
        key.All(ch => char.IsAsciiLetterOrDigit(ch) || "-._~+/=".Contains(ch));

    internal static Task<Result> TranscribeAsync(byte[] wav, string key, string language, bool consent, CancellationToken token)
        => TranscribeAsync(client.Value, wav, key, language, consent, token);

    internal static async Task<Result> TranscribeAsync(HttpClient http, byte[] wav, string key, string language, bool consent, CancellationToken token)
    {
        // Validate before constructing/sending any request, including in tests.
        if (!consent) throw new CloudSpeechException(CloudFailure.Consent);
        if (!ValidKey(key)) throw new CloudSpeechException(CloudFailure.Key);
        var locale = language switch { "cs" => "cs-CZ", "en" => "en-GB", _ => throw new CloudSpeechException(CloudFailure.Language) };
        if (wav.Length < 16000 || wav.Length > 16000 * 2 * 90 + 65536) throw new InvalidDataException("Unsupported audio size.");
        token.ThrowIfCancellationRequested();
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token);
        deadline.CancelAfter(TimeSpan.FromSeconds(120));
        try
        {
            using var form = new MultipartFormDataContent();
            form.Add(new StringContent(locale), "language");
            var file = new ByteArrayContent(wav); file.Headers.ContentType = new MediaTypeHeaderValue("audio/wav");
            form.Add(file, "file", "dictation.wav");
            using var request = new HttpRequestMessage(HttpMethod.Post, Endpoint) { Content = form };
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", key);
            var clock = Stopwatch.StartNew();
            using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, deadline.Token);
            if (!response.IsSuccessStatusCode)
                throw new CloudSpeechException((int)response.StatusCode switch
                { 401 => CloudFailure.Unauthorized, 403 => CloudFailure.Forbidden, 429 => CloudFailure.RateLimited, _ => CloudFailure.Service });
            using var body = await response.Content.ReadAsStreamAsync(deadline.Token);
            using var limited = new MemoryStream(); var buffer = new byte[8192];
            for (int count; (count = await body.ReadAsync(buffer.AsMemory(), deadline.Token)) != 0;)
            {
                if (limited.Length + count > 1024 * 1024) throw new CloudSpeechException(CloudFailure.Response);
                limited.Write(buffer, 0, count);
            }
            using var json = JsonDocument.Parse(limited.ToArray());
            if (json.RootElement.ValueKind != JsonValueKind.Object || !json.RootElement.TryGetProperty("text", out var value) || value.ValueKind != JsonValueKind.String)
                throw new CloudSpeechException(CloudFailure.Response);
            var text = value.GetString()!.Trim();
            if (text.Length == 0 || text.Contains(key, StringComparison.Ordinal)) throw new CloudSpeechException(CloudFailure.Response);
            token.ThrowIfCancellationRequested();
            return new(text, clock.Elapsed.TotalMilliseconds);
        }
        catch (OperationCanceledException) when (!token.IsCancellationRequested) { throw new CloudSpeechException(CloudFailure.Timeout); }
        catch (HttpRequestException) { throw new CloudSpeechException(CloudFailure.Network); }
        catch (IOException) { throw new CloudSpeechException(CloudFailure.Network); }
        catch (JsonException) { throw new CloudSpeechException(CloudFailure.Response); }
    }
}
