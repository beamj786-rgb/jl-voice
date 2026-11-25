using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using JLVoice;

// Developer experiment: does the `language` form field of the hosted NVIDIA endpoint change the transcript?
// Identical public FLEURS audio is sent once per language variant. Without --send NOTHING is sent and no key is read.
// The API key comes only from the NVIDIA_API_KEY environment variable and is never printed or written.
internal static class LanguageExperiment
{
    internal sealed record Audio(string Id, string Group, string Reference, double Seconds, string Sha256, byte[] Wav);
    internal sealed record Row(int Sequence, string AudioId, string Group, string RequestedLanguage, double AudioSeconds, string AudioSha256,
        int? HttpStatus, string Outcome, string? ErrorCategory, double? RequestMs, string? Text, string Reference, int Words, int Errors, int Chars, int CharErrors,
        IReadOnlyDictionary<string, string>? ResponseFields, int CzechOnlyLetters, int PolishOnlyLetters)
    {
        internal double? Wer => Outcome == "ok" && Words > 0 ? (double)Errors / Words : null;
        internal double? Cer => Outcome == "ok" && Chars > 0 ? (double)CharErrors / Chars : null;
    }
    internal sealed record Run(IReadOnlyList<Row> Rows, int Planned, bool Truncated, string? AbortReason);

    internal const string Omitted = "(omitted)";
    internal static string Label(string? locale) => locale ?? Omitted;

    internal static string?[] ParseParams(string csv)
    {
        var result = new List<string?>();
        foreach (var name in csv.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var value = name is "omit" or "none" or Omitted ? null : name;
            if (!NvidiaSpeechClient.ExperimentLocales.Contains(value)) throw new ArgumentException("Unsupported language value: " + name);
            if (!result.Contains(value)) result.Add(value);
        }
        return result.Count == 0 ? throw new ArgumentException("No language values selected.") : result.ToArray();
    }

    internal static List<Audio> LoadCorpus(string root, string set)
    {
        using var corpus = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "corpus.json")));
        var result = new List<Audio>();
        foreach (var sample in corpus.RootElement.GetProperty("samples").EnumerateArray())
        {
            var tier = sample.TryGetProperty("tier", out var t) ? t.GetString() : "short";
            var wanted = set switch { "short" => tier == "short", "duration" => tier == "duration", "all" => tier is "short" or "duration", _ => throw new ArgumentException("--set must be short, duration or all") };
            if (!wanted || sample.TryGetProperty("group", out var g) && g.GetString() == "control") continue;
            var name = sample.GetProperty("audio").GetString()!;
            if (Path.GetFileName(name) != name) throw new InvalidDataException("Invalid sample name.");
            var wav = File.ReadAllBytes(Path.Combine(root, name));
            var digest = Convert.ToHexString(SHA256.HashData(wav)).ToLowerInvariant();
            if (digest != sample.GetProperty("audio_sha256").GetString()) throw new InvalidDataException("Public fixture integrity mismatch: " + name);
            var language = sample.GetProperty("language").GetString()!;
            result.Add(new(sample.GetProperty("id").GetString()!, tier == "duration" ? language + "-dur" : language, sample.GetProperty("reference").GetString()!, (wav.Length - 44) / 32000.0, digest, wav));
        }
        return result.Count == 0 ? throw new InvalidDataException("The corpus has no samples for set '" + set + "'.") : result;
    }

    internal static List<(Audio Audio, string? Locale)> Plan(IReadOnlyList<Audio> audio, IReadOnlyList<string?> locales, int maxRequests, out bool truncated)
    {
        var all = audio.SelectMany(a => locales.Select(l => (a, l))).ToList();
        truncated = all.Count > maxRequests;
        return all.Take(maxRequests).ToList();
    }

    private static (int Words, int Errors, int Chars, int CharErrors) Counts(string reference, string? text)
    {
        var expected = Program.Words(reference); var actual = Program.Words(text ?? "");
        var expectedChars = string.Join(' ', expected).EnumerateRunes().Select(r => r.ToString()).ToArray();
        var actualChars = string.Join(' ', actual).EnumerateRunes().Select(r => r.ToString()).ToArray();
        return (expected.Length, Program.Distance(expected, actual), expectedChars.Length, Program.Distance(expectedChars, actualChars));
    }
    // Coarse hint only: letters that occur in Czech but not Polish, and the reverse. Not language identification.
    private static int Count(string? text, string letters) => (text ?? "").Count(c => letters.Contains(char.ToLowerInvariant(c)));

    internal static async Task<Run> RunAsync(HttpClient http, string key, IReadOnlyList<(Audio Audio, string? Locale)> plan, bool truncated, TimeSpan pause, Action<string> log, CancellationToken token)
    {
        var rows = new List<Row>(); string? abort = null; var transport = 0;
        foreach (var (audio, locale) in plan)
        {
            if (rows.Count > 0 && pause > TimeSpan.Zero) await Task.Delay(pause, token);
            string outcome = "ok", text = "", category = ""; int? status = null; double? ms = null; IReadOnlyDictionary<string, string>? fields = null;
            try
            {
                var result = await NvidiaSpeechClient.TranscribeExperimentalAsync(http, audio.Wav, key, locale, token);
                status = 200; ms = result.RequestMs; text = result.Text; fields = result.Extra; transport = 0;
            }
            catch (CloudSpeechException error)
            {
                outcome = "error"; category = error.Failure.ToString(); status = error.StatusCode;
                transport = error.Failure is CloudFailure.Network or CloudFailure.Timeout ? transport + 1 : 0;
                if (error.Failure is CloudFailure.Unauthorized or CloudFailure.Forbidden or CloudFailure.RateLimited) abort = $"HTTP {error.StatusCode}: {error.Failure}; stopped to avoid wasting credits (no retry)";
                else if (transport >= 3) abort = "three consecutive network failures";
            }
            var shown = outcome == "ok" ? text : null;
            var c = Counts(audio.Reference, shown);
            var row = new Row(rows.Count + 1, audio.Id, audio.Group, Label(locale), Math.Round(audio.Seconds, 2), audio.Sha256, status, outcome, outcome == "ok" ? null : category, ms,
                shown, audio.Reference, c.Words, outcome == "ok" ? c.Errors : c.Words, c.Chars, outcome == "ok" ? c.CharErrors : c.Chars, fields, Count(shown, "ěřů"), Count(shown, "łąęśćńźż"));
            rows.Add(row);
            log($"{row.Sequence,3}/{plan.Count} {audio.Id,-10} {Label(locale),-9} {(status?.ToString() ?? "-"),3} {row.Outcome,-5} {(ms is { } m ? m / 1000 : 0):F2}s WER {(row.Wer is { } w ? w.ToString("P0") : "n/a")}  {(shown is null ? category : shown.Length > 70 ? shown[..70] + "…" : shown)}");
            if (abort is not null) break;
        }
        return new(rows, plan.Count, truncated, abort);
    }

    private static string Normalize(string text) => string.Join(' ', Program.Words(text));

    internal static object Analyze(Run run)
    {
        var ok = run.Rows.Where(r => r.Outcome == "ok").ToList();
        var perAudio = ok.GroupBy(r => r.AudioId).Select(g =>
        {
            var distinct = g.Select(r => Normalize(r.Text!)).Distinct().Count();
            return new { audio_id = g.Key, group = g.First().Group, successful_variants = g.Count(), distinct_outputs = distinct, identical_across_variants = g.Count() >= 2 && distinct == 1,
                outputs = g.ToDictionary(r => r.RequestedLanguage, r => r.Text) };
        }).ToList();
        var comparable = perAudio.Count(a => a.successful_variants >= 2); var identical = perAudio.Count(a => a.identical_across_variants);
        var byGroupParam = ok.GroupBy(r => (r.Group, r.RequestedLanguage)).OrderBy(g => g.Key.Group).ThenBy(g => g.Key.RequestedLanguage).Select(g => new
        {
            group = g.Key.Group, requested_language = g.Key.RequestedLanguage, n = g.Count(),
            words = g.Sum(r => r.Words), wer = g.Sum(r => r.Words) == 0 ? (double?)null : (double)g.Sum(r => r.Errors) / g.Sum(r => r.Words),
            cer = g.Sum(r => r.Chars) == 0 ? (double?)null : (double)g.Sum(r => r.CharErrors) / g.Sum(r => r.Chars),
            median_request_ms = g.Select(r => r.RequestMs!.Value).Order().ElementAt(g.Count() / 2),
            czech_only_letters = g.Sum(r => r.CzechOnlyLetters), polish_only_letters = g.Sum(r => r.PolishOnlyLetters)
        }).ToList();
        var rejected = run.Rows.Where(r => r.Outcome == "error" && r.HttpStatus is >= 400 and < 500 and not (401 or 403 or 429)).GroupBy(r => r.RequestedLanguage)
            .Select(g => new { requested_language = g.Key, statuses = g.Select(r => r.HttpStatus).Distinct().Order().ToArray(), count = g.Count() }).ToList();
        string effect;
        if (comparable == 0) effect = "UNKNOWN: no audio file has two successful variants to compare.";
        else if (identical == comparable) effect = $"NO EVIDENCE that the language field changes decoding: all successful variants were identical on {identical}/{comparable} audio files. The server may ignore the field or decide the language itself.";
        else effect = $"The language field CHANGES the transcript on {comparable - identical}/{comparable} audio files (see per-audio outputs and per-variant WER). That shows influence, not that the requested language is enforced.";
        var fieldNames = ok.Where(r => r.ResponseFields is not null).SelectMany(r => r.ResponseFields!.Keys).Distinct().Order().ToArray();
        return new
        {
            language_parameter_effect = effect, comparable_audio_files = comparable, identical_audio_files = identical,
            rejected_values = rejected, response_fields_besides_text = fieldNames,
            server_reports_detected_language = fieldNames.Any(n => n.Contains("lang", StringComparison.OrdinalIgnoreCase)),
            per_audio = perAudio, per_group_and_variant = byGroupParam
        };
    }

    internal static string BuildReport(Run run, string set, IReadOnlyList<string?> locales, string clientOs, string key)
    {
        var report = new
        {
            experiment = "NVIDIA hosted endpoint language-parameter contract",
            endpoint = NvidiaSpeechClient.Endpoint,
            model = "unknown: the hosted function does not state its model/version in the API response",
            documentation_note = "Self-hosted NIM docs describe omitting the language for auto-detection and passing a code to constrain decoding; this experiment tests the HOSTED endpoint, whose accepted values are not documented.",
            created_utc = DateTime.UtcNow, client_os = clientOs, set, language_variants = locales.Select(Label).ToArray(),
            planned_requests = run.Planned, executed_requests = run.Rows.Count, plan_truncated_by_max_requests = run.Truncated, aborted = run.AbortReason,
            complete = run.AbortReason is null && !run.Truncated && run.Rows.Count == run.Planned,
            requests = run.Rows.Select(r => new
            {
                sequence = r.Sequence, audio_id = r.AudioId, group = r.Group, requested_language = r.RequestedLanguage, audio_seconds = r.AudioSeconds, audio_sha256 = r.AudioSha256,
                http_status = r.HttpStatus, outcome = r.Outcome, error_category = r.ErrorCategory, request_ms = r.RequestMs,
                reference = r.Reference, text = r.Text, words = r.Words, word_errors = r.Outcome == "ok" ? r.Errors : (int?)null, wer = r.Wer, characters = r.Chars, character_errors = r.Outcome == "ok" ? r.CharErrors : (int?)null, cer = r.Cer,
                czech_only_letters = r.CzechOnlyLetters, polish_only_letters = r.PolishOnlyLetters, response_fields_besides_text = r.ResponseFields
            }),
            analysis = Analyze(run),
            limitations = new[]
            {
                "Public FLEURS read speech only; not owner dictation or microphone audio. Duration samples are concatenations of the selected clips (0.4 s silence between them).",
                "One request per audio/variant. Request time includes upload, queueing, inference and response; server inference time and the exact model are not exposed. Latency is only comparable within one run.",
                "Identical output across variants is evidence the field is not used for these inputs; different output shows influence but not enforcement. A rejected (4xx) value shows validation, not decoding.",
                "Letter counts are a coarse hint, not language identification. A failed or partial run is not a complete experiment."
            }
        };
        var json = JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping });
        if (json.Contains(key, StringComparison.Ordinal)) throw new InvalidOperationException("Report would contain the API key.");
        return json;
    }

    internal static async Task<int> MainAsync(string[] args)
    {
        string set = "short", paramsCsv = "cs-CZ,cs,multi,en-GB,en-US,omit", corpusRoot = Path.Combine(AppContext.BaseDirectory, "public-corpus");
        var send = false; var max = 100; var pauseMs = 1500;
        for (var i = 0; i < args.Length; ++i)
        {
            switch (args[i])
            {
                case "--send": send = true; break;
                case "--set" when i + 1 < args.Length: set = args[++i]; break;
                case "--params" when i + 1 < args.Length: paramsCsv = args[++i]; break;
                case "--max-requests" when i + 1 < args.Length && int.TryParse(args[i + 1], out max) && max is >= 1 and <= 200: ++i; break;
                case "--pause-ms" when i + 1 < args.Length && int.TryParse(args[i + 1], out pauseMs) && pauseMs is >= 0 and <= 60000: ++i; break;
                case "--corpus" when i + 1 < args.Length: corpusRoot = args[++i]; break;
                default: Console.Error.WriteLine("Usage: --language-experiment [--set short|duration|all] [--params cs-CZ,cs,multi,en-GB,en-US,omit] [--max-requests N] [--pause-ms N] [--corpus DIR] [--send]\nNo API key belongs on the command line: set NVIDIA_API_KEY."); return 1; }
        }
        var locales = ParseParams(paramsCsv); var audio = LoadCorpus(corpusRoot, set);
        var plan = Plan(audio, locales, max, out var truncated);
        Console.WriteLine($"Language experiment, set '{set}': {audio.Count} audio files x {locales.Length} language variants ({string.Join(", ", locales.Select(Label))}) = {audio.Count * locales.Length} requests" + (truncated ? $", limited to {plan.Count} by --max-requests" : ""));
        foreach (var a in audio) Console.WriteLine($"  {a.Id,-10} {a.Seconds,6:F1} s  sha256 {a.Sha256[..12]}…");
        if (!send)
        {
            Console.WriteLine("\nDRY RUN: nothing was sent and no key was read. To send these PUBLIC FLEURS recordings to NVIDIA with your key (counts against your NVIDIA account limits):");
            Console.WriteLine("  $k = Read-Host 'NVIDIA API key' -AsSecureString; $env:NVIDIA_API_KEY = [Runtime.InteropServices.Marshal]::PtrToStringBSTR([Runtime.InteropServices.Marshal]::SecureStringToBSTR($k))");
            Console.WriteLine($"  .\\JLVoice.NvidiaProbe.exe --language-experiment --set {set} --send");
            Console.WriteLine("  Remove-Item Env:NVIDIA_API_KEY");
            return 0;
        }
        var key = Environment.GetEnvironmentVariable("NVIDIA_API_KEY");
        if (!NvidiaSpeechClient.ValidKey(key)) { Console.Error.WriteLine("NVIDIA_API_KEY is not set or has an invalid format. Nothing was sent."); return 1; }
        Console.WriteLine("\nSending public FLEURS audio to the NVIDIA endpoint. Key stays in memory and is not written anywhere. Ctrl+C cancels.");
        using var handler = new HttpClientHandler { AllowAutoRedirect = false, UseCookies = false };
        using var http = new HttpClient(handler) { Timeout = Timeout.InfiniteTimeSpan };
        using var cancel = new CancellationTokenSource(); Console.CancelKeyPress += (_, e) => { e.Cancel = true; cancel.Cancel(); };
        Run run;
        try { run = await RunAsync(http, key!, plan, truncated, TimeSpan.FromMilliseconds(pauseMs), Console.WriteLine, cancel.Token); }
        catch (OperationCanceledException) { Console.Error.WriteLine("Cancelled. No report written."); return 1; }
        var output = Path.Combine(AppContext.BaseDirectory, "nvidia-language-experiment-" + DateTime.UtcNow.ToString("yyyyMMdd-HHmmss") + "-" + Guid.NewGuid().ToString("N")[..6] + ".json");
        File.WriteAllText(output, BuildReport(run, set, locales, Environment.OSVersion.ToString(), key!), new UTF8Encoding(false));
        Console.WriteLine("\nReport (no key): " + output);
        if (run.AbortReason is not null) Console.Error.WriteLine("Stopped early: " + run.AbortReason);
        return run.AbortReason is null ? 0 : 1;
    }

    // ---- contract tests with an in-memory server: no NVIDIA request is ever made ----
    private static string? FieldOf(HttpRequestMessage request, string name)
    {
        foreach (var part in ((MultipartFormDataContent)request.Content!))
            if (part.Headers.ContentDisposition?.Name?.Trim('"') == name) return part.ReadAsStringAsync().GetAwaiter().GetResult();
        return null;
    }
    private static void Require(bool condition, string what) { if (!condition) throw new Exception("Language experiment self-test failed: " + what); }

    internal static async Task SelfTestAsync()
    {
        const string key = "NOT-A-REAL-API-KEY";
        var cs = new Audio("cs-1652", "cs", "Hlavním náboženstvím Moldavska je ortodoxní křesťanství.", 3.5, "00", new byte[16044]);
        var en = new Audio("en-1554", "en", "He built a WiFi door bell, he said.", 3.3, "11", new byte[16044]);
        var locales = ParseParams("cs-CZ,cs,multi,en-GB,en-US,omit");
        Require(locales.Length == 6 && locales[^1] is null, "variant parsing (omit = no field)");
        try { ParseParams("fr-FR"); Require(false, "unknown language value accepted"); } catch (ArgumentException) { }
        var plan = Plan([cs, en], locales, 100, out var truncated);
        Require(plan.Count == 12 && !truncated && plan.Take(6).All(p => p.Audio == cs), "plan = every audio x every variant, identical audio per variant");
        Require(Plan([cs, en], locales, 5, out var cut).Count == 5 && cut, "max-requests cap");

        // 1) A server that honours the field: Czech for cs-*/multi/omitted, English text for en-*.
        var sent = new List<string>();
        using var honouring = new HttpClient(new Program.FakeHandler(request =>
        {
            Require(request.RequestUri!.AbsoluteUri == NvidiaSpeechClient.Endpoint && request.Headers.Authorization?.Parameter == key, "destination/Bearer contract");
            var language = FieldOf(request, "language"); sent.Add(language ?? Omitted);
            var czech = language is null or "cs-CZ" or "cs" or "multi";
            return new HttpResponseMessage(System.Net.HttpStatusCode.OK) { Content = new StringContent(JsonSerializer.Serialize(new { text = czech ? "Hlavním náboženstvím Moldavska je ortodoxní křesťanství." : "Main religion of Moldova is orthodox christianity", language = czech ? "cs" : "en" })) };
        }));
        var run = await RunAsync(honouring, key, plan, false, TimeSpan.Zero, _ => { }, CancellationToken.None);
        Require(sent.Take(6).SequenceEqual(["cs-CZ", "cs", "multi", "en-GB", "en-US", Omitted]), "each variant is sent as its own language value and the omitted variant has no language field");
        Require(run.Rows.Count == 12 && run.AbortReason is null && run.Rows.All(r => r.HttpStatus == 200), "all requests recorded with HTTP status");
        Require(run.Rows[0].Wer == 0 && run.Rows[3].Wer > 0.5, "WER per variant against the reference");
        var json = BuildReport(run, "short", locales, "test", key);
        Require(json.Contains("CHANGES the transcript") && json.Contains("\"server_reports_detected_language\": true") && json.Contains("\"language\": \"cs\""), "influence detected and response fields reported");
        Require(!json.Contains(key), "report is free of the API key");

        // 2) A server that ignores the field: identical output must be reported as no evidence of influence.
        using var ignoring = new HttpClient(new Program.FakeHandler(_ => new HttpResponseMessage(System.Net.HttpStatusCode.OK) { Content = new StringContent("{\"text\":\"Hlavní náboženství je ortodoxní.\"}") }));
        var ignored = BuildReport(await RunAsync(ignoring, key, plan, false, TimeSpan.Zero, _ => { }, CancellationToken.None), "short", locales, "test", key);
        Require(ignored.Contains("NO EVIDENCE") && ignored.Contains("\"server_reports_detected_language\": false"), "identical outputs are reported as no evidence of influence");

        // 3) Rejected values (400) are recorded and the run continues; 401 stops at once without retrying.
        using var picky = new HttpClient(new Program.FakeHandler(request => FieldOf(request, "language") == "multi"
            ? new HttpResponseMessage(System.Net.HttpStatusCode.BadRequest) { Content = new StringContent("secret-looking body " + key) }
            : new HttpResponseMessage(System.Net.HttpStatusCode.OK) { Content = new StringContent("{\"text\":\"Ahoj světe.\"}") }));
        var pickyRun = await RunAsync(picky, key, plan, false, TimeSpan.Zero, _ => { }, CancellationToken.None);
        Require(pickyRun.Rows.Count == 12 && pickyRun.Rows.Count(r => r.HttpStatus == 400 && r.Outcome == "error" && r.Text is null && r.Wer is null) == 2, "a rejected value is recorded with its status and the run continues");
        var pickyJson = BuildReport(pickyRun, "short", locales, "test", key);
        Require(pickyJson.Contains("rejected_values") && pickyJson.Contains("\"requested_language\": \"multi\"") && !pickyJson.Contains("secret-looking") && !pickyJson.Contains(key), "rejection is summarised without the server body or key");
        var calls = 0;
        using var denied = new HttpClient(new Program.FakeHandler(_ => { calls++; return new HttpResponseMessage(System.Net.HttpStatusCode.Unauthorized) { Content = new StringContent(key) }; }));
        var deniedRun = await RunAsync(denied, key, plan, false, TimeSpan.Zero, _ => { }, CancellationToken.None);
        Require(calls == 1 && deniedRun.Rows.Count == 1 && deniedRun.AbortReason!.Contains("401") && !BuildReport(deniedRun, "short", locales, "test", key).Contains("\"complete\": true"), "401 stops the run after one request with no retry");

        // 4) HTTP 200 with an empty transcript keeps its status and is not a transport failure.
        using var empty = new HttpClient(new Program.FakeHandler(_ => new HttpResponseMessage(System.Net.HttpStatusCode.OK) { Content = new StringContent("{\"text\":\"\"}") }));
        var emptyRun = await RunAsync(empty, key, Plan([cs], [null], 5, out _), false, TimeSpan.Zero, _ => { }, CancellationToken.None);
        Require(emptyRun.Rows[0].HttpStatus == 200 && emptyRun.Rows[0].Outcome == "error" && emptyRun.Rows[0].ErrorCategory == "Response", "empty transcript is recorded as HTTP 200 / Response");
        // 5) The experimental entry point accepts only the fixed language values.
        try { await NvidiaSpeechClient.TranscribeExperimentalAsync(honouring, new byte[16044], key, "xx-XX", CancellationToken.None); Require(false, "arbitrary language value accepted"); }
        catch (CloudSpeechException error) { Require(error.Failure == CloudFailure.Language, "arbitrary language value is refused before any request"); }
        Console.WriteLine("PASS: language experiment plan, per-variant request fields (omitted field), HTTP status/WER recording, influence analysis, rejected-value and 401 handling, empty responses, no key in reports (mock only; no NVIDIA request).");
    }
}
