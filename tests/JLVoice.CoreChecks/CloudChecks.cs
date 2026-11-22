using JLVoice;
using System.Net;
using System.Text;
using System.Text.Json;

internal static class CloudChecks
{
    private sealed class Handler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> respond) : HttpMessageHandler
    {
        internal int Sends;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        { ++Sends; return respond(request, token); }
    }
    private static void Check(bool okay, string name) { if (!okay) throw new Exception(name); Console.WriteLine("PASS: " + name); }
    internal static async Task RunAsync()
    {
        const string key = "nvapi-FAKE-TEST-KEY";
        var wav = AudioCapture.EncodePcm(new byte[16000]);
        using var handler = new Handler((_, _) => throw new Exception("No request permitted"));
        using var client = new HttpClient(handler);
        foreach (var (consent, language, secret, failure) in new[] {
            (false, "cs", key, CloudFailure.Consent), (true, "auto", key, CloudFailure.Language),
            (true, "cs", "", CloudFailure.Key), (true, "cs", "secret\r\nheader", CloudFailure.Key) })
        {
            try { await NvidiaSpeechClient.TranscribeAsync(client,wav,secret,language,consent,CancellationToken.None); throw new Exception("Unauthorized request accepted"); }
            catch (CloudSpeechException error) { Check(error.Failure == failure && handler.Sends == 0, "cloud consent/language/key validation blocks network: " + failure); }
        }
        using var preCancelled = new CancellationTokenSource(); preCancelled.Cancel();
        try { await NvidiaSpeechClient.TranscribeAsync(client,wav,key,"cs",true,preCancelled.Token); throw new Exception("Cancelled request accepted"); }
        catch (OperationCanceledException) { Check(handler.Sends == 0,"pre-cancelled cloud call sends no audio"); }

        using var okayHandler = new Handler(async (request, _) =>
        {
            Check(request.RequestUri!.AbsoluteUri == NvidiaSpeechClient.Endpoint && request.Method == HttpMethod.Post &&
                request.Headers.Authorization?.Scheme == "Bearer" && request.Headers.Authorization.Parameter == key, "cloud audio and credential target the fixed HTTPS endpoint");
            var bytes = await request.Content!.ReadAsByteArrayAsync();
            var multipart = Encoding.UTF8.GetString(bytes);
            Check(multipart.Contains("cs-CZ") && multipart.Contains("RIFF") && !multipart.Contains(key), "multipart contains correct locale/WAV and no key in body");
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(JsonSerializer.Serialize(new { text = " Příští čtvrtek. " })) };
        });
        using var okay = new HttpClient(okayHandler);
        var result = await NvidiaSpeechClient.TranscribeAsync(okay,wav,key,"cs",true,CancellationToken.None);
        Check(result.Text == "Příští čtvrtek." && okayHandler.Sends == 1,"cloud response preserves Czech text without retrying");

        foreach (var (status, failure) in new[] { (401,CloudFailure.Unauthorized), (403,CloudFailure.Forbidden), (429,CloudFailure.RateLimited), (302,CloudFailure.Service), (503,CloudFailure.Service) })
        {
            using var badHandler = new Handler((_, _) => Task.FromResult(new HttpResponseMessage((HttpStatusCode)status) { Content = new StringContent(key + " untrusted error body") }));
            using var bad = new HttpClient(badHandler);
            try { await NvidiaSpeechClient.TranscribeAsync(bad,wav,key,"en",true,CancellationToken.None); throw new Exception("HTTP failure accepted"); }
            catch (CloudSpeechException error)
            {
                var notice = ErrorNotice.From(error,"cs");
                Check(error.Failure == failure && badHandler.Sends == 1 && !(error.Message + notice.Message + notice.Details).Contains(key), "HTTP " + status + " is sanitized and never retried");
            }
        }
        foreach (var body in new[] { "{}", "{\"text\":42}", "{\"text\":\"\"}", JsonSerializer.Serialize(new { text = key }), new string('x',1048577) })
        {
            using var invalidHandler = new Handler((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body) }));
            using var invalid = new HttpClient(invalidHandler);
            try { await NvidiaSpeechClient.TranscribeAsync(invalid,wav,key,"cs",true,CancellationToken.None); throw new Exception("Invalid response accepted"); }
            catch (CloudSpeechException error) { Check(error.Failure == CloudFailure.Response,"unusable, secret-echo or oversized cloud response is refused"); }
        }
        using var waitingHandler = new Handler(async (_, token) => { await Task.Delay(Timeout.Infinite,token); return new HttpResponseMessage(HttpStatusCode.OK); });
        using var waiting = new HttpClient(waitingHandler);
        using var cancel = new CancellationTokenSource(TimeSpan.FromMilliseconds(25));
        try { await NvidiaSpeechClient.TranscribeAsync(waiting,wav,key,"cs",true,cancel.Token); throw new Exception("Cancellation ignored"); }
        catch (OperationCanceledException) { Check(waitingHandler.Sends == 1,"active cloud request honors cancellation without retry or transcript"); }

        Check(new Settings().Provider == "offline" && new Settings().CloudConsentVersion == 0 && !new Settings().RememberNvidiaKey,"existing/new settings remain offline without consent or remembered key");
        var enabled = new Settings { Provider="nvidia",Language="cs",CloudConsentVersion=1,RememberNvidiaKey=true }; enabled.Validate();
        Check(!JsonSerializer.Serialize(enabled).Contains(key),"settings schema contains no API key field");
        foreach (var invalid in new[] { enabled with { CloudConsentVersion=0 }, enabled with { Language="auto" }, enabled with { Provider="other" } })
        {
            try { invalid.Validate(); throw new Exception("Unsafe provider settings accepted"); } catch (InvalidDataException) { }
        }
        Console.WriteLine("PASS: invalid/unknown cloud settings fail closed; all HTTP checks use in-memory mocks, no real NVIDIA request");
    }
}
