using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Kotodama.Tests.Fixtures;

/// <summary>One translated case, as written by the live benchmark harness.</summary>
public sealed record ReviewRow(
    string CaseId,
    string Direction,
    string Profile,
    int Run,
    string Source,
    string Reference,
    string Note,
    string Model,
    string Output,
    bool Ok,
    double LatencyMs,
    int CompletionTokens);

/// <summary>
/// Writes the same files as the PLaMo benchmark script, so runs can be scored and compared by
/// hand: review.csv (identical columns and quoting) and results.jsonl (identical keys).
/// Translation quality is never scored automatically.
/// </summary>
public static class ReviewCsv
{
    public const string Header =
        "case_id,direction,profile,run,source,reference,model_output,latency_ms,completion_tokens,manual_score_0_to_2,review_note";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never,
    };

    /// <summary>
    /// One CSV line, quoted like jq's @csv in the benchmark script: strings in double quotes with
    /// embedded quotes doubled, run and completion_tokens as bare numbers, latency as a quoted
    /// decimal string, and the two manual columns as empty strings.
    /// </summary>
    public static string FormatCsvLine(ReviewRow row) => string.Join(',',
        Quote(row.CaseId),
        Quote(row.Direction),
        Quote(row.Profile),
        row.Run.ToString(CultureInfo.InvariantCulture),
        Quote(row.Source),
        Quote(row.Reference),
        Quote(row.Output),
        Quote(row.LatencyMs.ToString("0.0", CultureInfo.InvariantCulture)),
        row.CompletionTokens.ToString(CultureInfo.InvariantCulture),
        Quote(string.Empty),
        Quote(string.Empty));

    public static string FormatJsonLine(ReviewRow row) => JsonSerializer.Serialize(new
    {
        id = row.CaseId,
        direction = row.Direction,
        profile = row.Profile,
        model = row.Model,
        run = row.Run,
        source = row.Source,
        reference = row.Reference,
        note = row.Note,
        output = row.Output,
        ok = row.Ok,
        latency_ms = Math.Round(row.LatencyMs, 1),
        completion_tokens = row.CompletionTokens,
    }, JsonOptions);

    public static void Write(string directory, IEnumerable<ReviewRow> rows)
    {
        Directory.CreateDirectory(directory);
        var list = rows.ToList();
        var utf8 = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);
        File.WriteAllLines(Path.Combine(directory, "review.csv"), new[] { Header }.Concat(list.Select(FormatCsvLine)), utf8);
        File.WriteAllLines(Path.Combine(directory, "results.jsonl"), list.Select(FormatJsonLine), utf8);
    }

    private static string Quote(string value) => "\"" + value.Replace("\"", "\"\"") + "\"";
}
