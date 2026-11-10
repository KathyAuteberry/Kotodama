using System.Text.Json.Nodes;
using EchoXIV.Plamo;
using EchoXIV.Services;
using Kotodama.Tests.Fixtures;
using Xunit;

namespace Kotodama.Tests;

public class ReviewCsvTests
{
    private static ReviewRow Row(string output = "Please use mitigation.") => new(
        "JP01", "jp-en", "plain", 1, "軽減お願いします", "Please use mitigation.", "note", "model", output, true, 213.46, 5);

    [Fact]
    public void Header_matches_the_benchmark_review_csv()
    {
        Assert.Equal(
            "case_id,direction,profile,run,source,reference,model_output,latency_ms,completion_tokens,manual_score_0_to_2,review_note",
            ReviewCsv.Header);
    }

    [Fact]
    public void Line_is_quoted_like_jq_csv()
    {
        Assert.Equal(
            "\"JP01\",\"jp-en\",\"plain\",1,\"軽減お願いします\",\"Please use mitigation.\",\"Please use mitigation.\",\"213.5\",5,\"\",\"\"",
            ReviewCsv.FormatCsvLine(Row()));
    }

    [Fact]
    public void Quotes_and_newlines_are_escaped_inside_fields()
    {
        var line = ReviewCsv.FormatCsvLine(Row("He said \"hi\"\nthen left"));
        Assert.Contains("\"He said \"\"hi\"\"\nthen left\"", line);
    }

    [Fact]
    public void Json_line_has_the_benchmark_keys()
    {
        var json = JsonNode.Parse(ReviewCsv.FormatJsonLine(Row()))!.AsObject();

        Assert.Equal(
            new[] { "id", "direction", "profile", "model", "run", "source", "reference", "note", "output", "ok", "latency_ms", "completion_tokens" },
            json.Select(p => p.Key));
        Assert.Equal("軽減お願いします", json["source"]!.GetValue<string>());
        Assert.Equal(213.5, json["latency_ms"]!.GetValue<double>());
    }

    [Fact]
    public void Write_creates_both_files_without_a_bom()
    {
        var dir = Directory.CreateTempSubdirectory("kotodama-review-").FullName;
        try
        {
            ReviewCsv.Write(dir, new[] { Row(), Row() });

            var csv = File.ReadAllBytes(Path.Combine(dir, "review.csv"));
            Assert.NotEqual(0xEF, csv[0]);
            Assert.Equal(3, File.ReadAllLines(Path.Combine(dir, "review.csv")).Length);
            Assert.Equal(2, File.ReadAllLines(Path.Combine(dir, "results.jsonl")).Length);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    /// <summary>
    /// Opt-in live smoke test: does nothing unless KOTODAMA_OLLAMA_URL is set (it never runs in CI).
    /// The full benchmark is tests/Kotodama.LiveBench.
    /// </summary>
    [Fact]
    public async Task Live_smoke_when_KOTODAMA_OLLAMA_URL_is_set()
    {
        var url = Environment.GetEnvironmentVariable("KOTODAMA_OLLAMA_URL");
        if (string.IsNullOrWhiteSpace(url))
            return;

        using var client = new PlamoClient();
        var settings = new PlamoSettings(url, PlamoSettings.DefaultModel, "30m", 0f, 192, TimeSpan.FromSeconds(60));
        var service = new PlamoTranslatorService(client, () => settings);

        var english = await service.TranslateDetailedAsync("軽減お願いします", "en");
        var japanese = await service.TranslateDetailedAsync("Please wait, my game crashed.", "ja");

        Assert.Equal(TranslationDirection.JaToEn, english.Direction);
        Assert.Equal(ScriptLanguage.En, ScriptLanguageClassifier.Classify(english.Text));
        Assert.Equal(ScriptLanguage.Ja, ScriptLanguageClassifier.Classify(japanese.Text));
    }
}
