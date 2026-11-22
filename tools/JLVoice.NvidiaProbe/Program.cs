using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

internal static class Program
{
    // Official multilingual TDT endpoint, not the English-only v2 endpoint.
    internal const string Endpoint = "https://2b940e91-a70e-4483-a958-d50408b96589.invocation.api.nvcf.nvidia.com/v1/audio/transcriptions";
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    internal sealed class ApiFailure(int status) : Exception("NVIDIA HTTP " + status)
    { public int Status { get; } = status; }

    internal static async Task<string> Transcribe(HttpClient client, string key, string language, byte[] wav)
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(120));
        using var form = new MultipartFormDataContent();
        form.Add(new StringContent(language), "language");
        var file = new ByteArrayContent(wav);
        file.Headers.ContentType = new MediaTypeHeaderValue("audio/wav");
        form.Add(file, "file", "sample.wav");
        using var request = new HttpRequestMessage(HttpMethod.Post, Endpoint) { Content = form };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", key);
        using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, deadline.Token);
        if (!response.IsSuccessStatusCode) throw new ApiFailure((int)response.StatusCode);
        using var body = await response.Content.ReadAsStreamAsync(deadline.Token);
        using var limited = new MemoryStream();
        var buffer = new byte[8192];
        for (int count; (count = await body.ReadAsync(buffer.AsMemory(), deadline.Token)) != 0;)
        {
            if (limited.Length + count > 1024 * 1024) throw new InvalidDataException("Unexpected response size.");
            await limited.WriteAsync(buffer.AsMemory(0, count));
        }
        using var json = JsonDocument.Parse(limited.ToArray());
        var text = json.RootElement.GetProperty("text").GetString();
        if (text?.Contains(key, StringComparison.Ordinal) == true) throw new InvalidDataException("Unexpected authenticated response.");
        return string.IsNullOrWhiteSpace(text) ? throw new InvalidDataException("No transcript.") : text;
    }

    private static string ReadKey()
    {
        if (Console.IsInputRedirected) throw new InvalidOperationException("Use an interactive console for the hidden API-key prompt.");
        var text = new StringBuilder();
        while (true)
        {
            var pressed = Console.ReadKey(intercept: true);
            if (pressed.Key == ConsoleKey.Enter) break;
            if (pressed.Key == ConsoleKey.Escape) throw new OperationCanceledException();
            if (pressed.Key == ConsoleKey.Backspace) { if (text.Length > 0) text.Length--; continue; }
            if (!char.IsControl(pressed.KeyChar)) text.Append(pressed.KeyChar);
            if (text.Length > 4096) throw new InvalidDataException("Invalid key length.");
        }
        Console.WriteLine();
        var key = text.ToString().Trim(); text.Clear();
        return key.Length > 0 ? key : throw new InvalidDataException("Empty API key.");
    }

    internal static string[] Words(string text) => Regex.Matches(text.Normalize(NormalizationForm.FormC).ToLowerInvariant(), @"[^\W_]+(?:['’][^\W_]+)*").Select(match => match.Value).ToArray();
    internal static int Distance(string[] expected, string[] actual)
    {
        var row = Enumerable.Range(0, actual.Length + 1).ToArray();
        for (var i = 1; i <= expected.Length; ++i)
        {
            var next = new int[actual.Length + 1]; next[0] = i;
            for (var j = 1; j <= actual.Length; ++j)
                next[j] = Math.Min(Math.Min(row[j] + 1, next[j-1] + 1), row[j-1] + (expected[i-1] == actual[j-1] ? 0 : 1));
            row = next;
        }
        return row[^1];
    }
    private static object Score(string reference, string hypothesis)
    {
        var expected = Words(reference); var actual = Words(hypothesis);
        var expectedChars = string.Join(' ', expected).EnumerateRunes().Select(r => r.ToString()).ToArray();
        var actualChars = string.Join(' ', actual).EnumerateRunes().Select(r => r.ToString()).ToArray();
        var errors = Distance(expected, actual); var charErrors = Distance(expectedChars, actualChars);
        return new { words = expected.Length, errors, wer = expected.Length == 0 ? (double?)null : (double)errors/expected.Length,
            characters = expectedChars.Length, character_errors = charErrors, cer = expectedChars.Length == 0 ? (double?)null : (double)charErrors/expectedChars.Length };
    }

    private static async Task<int> Main(string[] args)
    {
        Console.OutputEncoding = new UTF8Encoding(false);
        if (args.SequenceEqual(new[] { "--self-test" })) { await SelfTest(); return 0; }
        if (args.Length != 0) { Console.Error.WriteLine("Run without arguments; no API key belongs on the command line."); return 1; }
        Console.WriteLine("JL Voice — NVIDIA cloud quality probe / test kvality");
        Console.WriteLine("Odešle šest přiložených veřejných CZ/EN nahrávek do NVIDIA. Soukromé audio nečte.");
        Console.WriteLine("API klíč je jen v paměti tohoto procesu; neukládá se do výsledků ani prostředí.");
        Console.Write("Vlož NVIDIA API klíč a stiskni Enter (vstup není vidět, Escape zruší): ");
        string key;
        try { key = ReadKey(); } catch { Console.Error.WriteLine("Test zrušen nebo klíč nebyl zadán."); return 1; }
        using var handler = new HttpClientHandler { AllowAutoRedirect = false };
        using var client = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(120) };
        var rows = new List<object>(); var complete = false;
        var output = Path.Combine(AppContext.BaseDirectory, "nvidia-results-" + DateTime.UtcNow.ToString("yyyyMMdd-HHmmss") + "-" + Guid.NewGuid().ToString("N")[..6] + ".json");
        try
        {
            var corpusRoot = Path.Combine(AppContext.BaseDirectory, "public-corpus");
            using var corpus = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(corpusRoot, "corpus.json")));
            foreach (var sample in corpus.RootElement.GetProperty("samples").EnumerateArray())
            {
                var name = sample.GetProperty("audio").GetString()!;
                if (Path.GetFileName(name) != name) throw new InvalidDataException("Invalid sample name.");
                var wav = await File.ReadAllBytesAsync(Path.Combine(corpusRoot, name));
                var digest = Convert.ToHexString(SHA256.HashData(wav)).ToLowerInvariant();
                if (digest != sample.GetProperty("audio_sha256").GetString()) throw new InvalidDataException("Public fixture integrity mismatch.");
                var language = sample.GetProperty("language").GetString()!;
                var locale = language switch { "cs" => "cs-CZ", "en" => "en-GB", _ => throw new InvalidDataException("Unsupported probe language.") };
                var clock = Stopwatch.StartNew();
                var text = await Transcribe(client, key, locale, wav); clock.Stop();
                var reference = sample.GetProperty("reference").GetString()!;
                rows.Add(new { id = sample.GetProperty("id").GetString(), group = language, mode = locale, audio_sha256 = digest,
                    text, reference, request_wall_ms = clock.Elapsed.TotalMilliseconds, inference_ms = (double?)null,
                    server_peak_ram_bytes = (long?)null, score = Score(reference, text) });
                Console.WriteLine($"\n{language} · {clock.Elapsed.TotalSeconds:F2} s celkem");
                Console.WriteLine("REFERENCE: " + reference); Console.WriteLine("VÝSLEDEK:  " + text);
            }
            complete = true;
        }
        catch (ApiFailure error) { Console.Error.WriteLine($"NVIDIA odmítla požadavek: HTTP {error.Status}. Bez automatického opakování."); }
        catch { Console.Error.WriteLine("Test nedokončen: chyba sítě, souborů nebo formátu odpovědi. Detaily požadavku ani klíč se nelogují."); }
        finally
        {
            key = "";
            var report = new { engine = "NVIDIA hosted Parakeet TDT 0.6B", endpoint = Endpoint, complete,
                created_utc = DateTime.UtcNow, client_os = Environment.OSVersion.ToString(), samples = rows,
                limitations = new[] { "Cloud model version/hash and server inference/RAM are not exposed by this probe.",
                    "Request time includes upload, network, queue, inference and response; no insertion measured.",
                    "Short public read-speech set; no representative mixed/quality or offline acceptance.",
                    "A failed or partial run is not a successful benchmark." } };
            using var file = new FileStream(output, FileMode.CreateNew, FileAccess.Write);
            await JsonSerializer.SerializeAsync(file, report, JsonOptions);
            Console.WriteLine("\nVýsledky bez klíče: " + output);
        }
        Console.WriteLine("Stiskni Enter pro zavření."); Console.ReadLine();
        return complete ? 0 : 1;
    }

    private sealed class FakeHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token) => Task.FromResult(respond(request));
    }
    private static async Task SelfTest()
    {
        if (Distance(Words("Příští čtvrtek"), Words("pristi čtvrtek")) != 1 || Distance(Words("Hello, world!"), Words("hello world")) != 0)
            throw new Exception("Unicode scoring failed.");
        const string key = "NOT-A-REAL-API-KEY";
        using var client = new HttpClient(new FakeHandler(request =>
        {
            if (request.RequestUri!.AbsoluteUri != Endpoint || request.Method != HttpMethod.Post || request.Headers.Authorization?.Parameter != key || request.Content is not MultipartFormDataContent)
                throw new Exception("Authentication/destination contract failed.");
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{\"text\":\"Příští čtvrtek.\"}") };
        }));
        if (await Transcribe(client,key,"cs-CZ",new byte[44]) != "Příští čtvrtek.") throw new Exception("Response parsing failed.");
        using var denied = new HttpClient(new FakeHandler(_ => new HttpResponseMessage(HttpStatusCode.Unauthorized) { Content = new StringContent(key) }));
        try { await Transcribe(denied,key,"en-GB",new byte[44]); throw new Exception("HTTP failure accepted."); }
        catch (ApiFailure error) { if (error.Status != 401 || error.Message.Contains(key)) throw new Exception("Unsafe error reporting."); }
        using var echoed = new HttpClient(new FakeHandler(_ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{\"text\":\"" + key + "\"}") }));
        try { await Transcribe(echoed,key,"en-GB",new byte[44]); throw new Exception("Credential echo accepted."); }
        catch (InvalidDataException) { }
        Console.WriteLine("PASS: scoring, fixed HTTPS destination, Bearer/multipart contract, response parsing, failed-request rejection and secret-free errors (mock only; no NVIDIA request).");
    }
}
