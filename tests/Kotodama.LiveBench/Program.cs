using System.Diagnostics;
using System.Globalization;
using EchoXIV.Plamo;
using EchoXIV.Profiles;
using EchoXIV.Services;
using Kotodama.Tests.Fixtures;

// Kotodama live PLaMo benchmark. Runs bench/cases.tsv and bench/regression.tsv through the plugin's
// own PLaMo path and writes review.csv + results.jsonl for manual scoring. No automatic judging.

var options = BenchOptions.Parse(args);
if (options == null)
    return 2;

var outDir = options.OutputDir ?? Path.Combine(BenchFixtures.RepoRoot, "bench", "runs", DateTime.Now.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture));
var settings = new PlamoSettings(options.Url, options.Model, "30m", 0f, PlamoSettings.DefaultNumPredict, TimeSpan.FromSeconds(options.TimeoutSeconds));

using var client = new PlamoClient();

// "plain" = no profile; any other name is loaded from --profile-dir (default: the shipped defaults).
var profileStore = new ProfileStore(options.ProfileDir ?? Path.Combine(BenchFixtures.RepoRoot, "Kotodama", "Profiles", "Defaults"));
profileStore.Reload();
var runProfiles = options.Profiles.Select(name => name == "plain" ? (Name: "plain", Profile: (TranslationProfile?)null) : (Name: name, Profile: profileStore.Get(name))).ToList();
foreach (var (name, profile) in runProfiles)
{
    if (profile != null && ReferenceEquals(profile, TranslationProfile.Empty))
    {
        Console.Error.WriteLine($"Profile '{name}' not found in {profileStore.Root}");
        return 2;
    }
}

var cases = new List<(string Id, string Direction, string Source, string Reference, string Note)>();
if (options.Set is "all" or "bench")
    cases.AddRange(BenchFixtures.LoadCases().Select(c => (c.Id, c.Direction, c.Source, c.Reference, c.Note)));
if (options.Set is "all" or "regression")
    cases.AddRange(BenchFixtures.LoadRegression().Select(r => (r.Id, r.Direction, r.Source, r.Expected, $"{r.Note} (was: {r.WrongOutput})")));

cases = cases
    .Where(c => options.Direction == "both" || c.Direction == options.Direction)
    .GroupBy(c => c.Direction)
    .SelectMany(g => options.Limit > 0 ? g.Take(options.Limit) : g)
    .ToList();

Console.WriteLine($"Kotodama live bench: {cases.Count} cases x {options.Runs} run(s), model {options.Model} on {options.Url}");

if (options.WarmUp)
{
    var warm = Stopwatch.StartNew();
    await PlamoWarmUp(client, settings);
    Console.WriteLine($"Warm-up: {warm.ElapsedMilliseconds} ms");
}

var rows = new List<ReviewRow>();
var index = 0;
foreach (var (profileName, profile) in runProfiles)
foreach (var c in cases)
{
    var target = c.Direction == "jp-en" ? "en" : "ja";
    var service = new PlamoTranslatorService(client, () => settings, profile == null
        ? null
        : (direction, text) =>
        {
            var prompt = ProfileStore.PromptFor(profile, direction, text);
            return new PlamoPromptExtras(prompt.Context, prompt.Glossary, null);
        });
    for (var run = 1; run <= options.Runs; run++)
    {
        index++;
        string output;
        bool ok;
        double latency;
        int tokens;
        try
        {
            var result = await service.TranslateDetailedAsync(c.Source, target);
            output = result.Direction == null ? $"SKIPPED: already {target} ({result.Text})" : result.Text;
            ok = result.Direction != null;
            latency = result.Latency.TotalMilliseconds;
            tokens = result.CompletionTokens;
        }
        catch (Exception ex)
        {
            output = $"ERROR: {ex.Message}";
            ok = false;
            latency = 0;
            tokens = 0;
        }

        rows.Add(new ReviewRow(c.Id, c.Direction, profileName, run, c.Source, c.Reference, c.Note, options.Model, output, ok, latency, tokens));
        if (!options.Quiet)
            Console.WriteLine($"[{index,3}] {c.Id,-5} {c.Direction} {profileName,-6} #{run} {latency,7:0.0}ms | {output.Replace('\n', ' ')}");
    }
}

ReviewCsv.Write(outDir, rows);

Console.WriteLine();
foreach (var group in rows.GroupBy(r => r.Profile))
{
    var okRows = group.Where(r => r.Ok).Select(r => r.LatencyMs).OrderBy(x => x).ToList();
    Console.Write($"{group.Key}: successful {okRows.Count}/{group.Count()}");
    if (okRows.Count > 0)
        Console.Write($", latency avg {okRows.Average():0} ms, median {okRows[okRows.Count / 2]:0} ms, p95 {okRows[(int)Math.Ceiling((okRows.Count - 1) * 0.95)]:0} ms");
    Console.WriteLine();
}
Console.WriteLine($"Saved {Path.Combine(outDir, "review.csv")} and results.jsonl. Score model_output by hand (0 wrong, 1 usable, 2 correct/natural).");
return rows.All(r => r.Ok) ? 0 : 1;

static async Task PlamoWarmUp(PlamoClient client, PlamoSettings settings)
{
    var prompt = Pfml3PromptBuilder.Build(new Pfml3Request(TranslationDirection.JaToEn, "了解"));
    using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(60));
    await client.GenerateAsync(settings with { NumPredict = 8 }, prompt, cts.Token);
}

internal sealed record BenchOptions(
    string Url, string Model, int Runs, int Limit, string Set, string Direction, string? OutputDir, int TimeoutSeconds, bool WarmUp, bool Quiet,
    IReadOnlyList<string> Profiles, string? ProfileDir)
{
    public static BenchOptions? Parse(string[] args)
    {
        var url = Environment.GetEnvironmentVariable("KOTODAMA_OLLAMA_URL") ?? string.Empty;
        var model = Environment.GetEnvironmentVariable("KOTODAMA_OLLAMA_MODEL") ?? PlamoSettings.DefaultModel;
        int runs = 1, limit = 0, timeout = 90;
        string set = "all", direction = "both";
        string? outDir = null;
        bool warmUp = true, quiet = false;
        var profiles = new List<string>();
        string? profileDir = null;

        for (var i = 0; i < args.Length; i++)
        {
            string Next() => i + 1 < args.Length ? args[++i] : throw new ArgumentException($"Missing value for {args[i]}");
            switch (args[i])
            {
                case "--url": url = Next(); break;
                case "--model": model = Next(); break;
                case "--runs": runs = int.Parse(Next(), CultureInfo.InvariantCulture); break;
                case "--limit": limit = int.Parse(Next(), CultureInfo.InvariantCulture); break;
                case "--set": set = Next(); break;
                case "--direction": direction = Next(); break;
                case "--out": outDir = Next(); break;
                case "--timeout": timeout = int.Parse(Next(), CultureInfo.InvariantCulture); break;
                case "--no-warmup": warmUp = false; break;
                case "--profile": profiles.Add(Next()); break;
                case "--profile-dir": profileDir = Next(); break;
                case "--quiet": quiet = true; break;
                case "-h":
                case "--help":
                    PrintHelp();
                    return null;
                default:
                    Console.Error.WriteLine($"Unknown option: {args[i]} (try --help)");
                    return null;
            }
        }

        if (string.IsNullOrWhiteSpace(url))
        {
            Console.Error.WriteLine("Set KOTODAMA_OLLAMA_URL or pass --url http://host:11434");
            return null;
        }
        if (set is not ("all" or "bench" or "regression") || direction is not ("both" or "jp-en" or "en-jp") || runs < 1)
        {
            Console.Error.WriteLine("Invalid --set, --direction or --runs (try --help)");
            return null;
        }

        if (profiles.Count == 0)
            profiles.Add("plain");

        return new BenchOptions(url, model, runs, limit, set, direction, outDir, timeout, warmUp, quiet, profiles, profileDir);
    }

    private static void PrintHelp() => Console.WriteLine("""
        Kotodama live PLaMo benchmark (opt-in; needs a running Ollama server)

          dotnet run --project tests/Kotodama.LiveBench -- [options]

          --url URL            Ollama server (default: $KOTODAMA_OLLAMA_URL)
          --model MODEL        Model (default: $KOTODAMA_OLLAMA_MODEL or the PLaMo 3 Translate Q4_K_M GGUF)
          --set all|bench|regression
          --direction both|jp-en|en-jp
          --runs N             Repeat each case N times (default 1)
          --limit N            First N cases per direction (0 = all)
          --out DIR            Output directory (default bench/runs/<timestamp>, git-ignored)
          --timeout SECONDS    Per-request timeout (default 90)
          --profile NAME       plain (no profile) or a profile name; repeat to compare (default: plain)
          --profile-dir DIR    Where profiles live (default: Kotodama/Profiles/Defaults)
          --no-warmup          Skip loading the model first
          --quiet              No per-case output

        Writes review.csv (same columns as the earlier benchmark) and results.jsonl.
        Translation quality is never scored automatically.
        """);
}
