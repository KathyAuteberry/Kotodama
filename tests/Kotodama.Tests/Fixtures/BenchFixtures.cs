using System.Text;

namespace Kotodama.Tests.Fixtures;

/// <summary>One benchmark case from bench/cases.tsv (same schema as the PLaMo benchmark script).</summary>
public sealed record BenchCase(string Id, string Direction, string Source, string Reference, string Note);

/// <summary>One known failure from bench/regression.tsv.</summary>
public sealed record RegressionCase(
    string Id, string BenchId, string Direction, string Source, string WrongOutput, string Expected, string Note);

/// <summary>
/// Loads the TSV fixtures under bench/. Kept free of test-framework types so the opt-in live
/// benchmark harness (docs/PLAN.md P3.5) can link this file too.
/// </summary>
public static class BenchFixtures
{
    public static readonly string[] CaseColumns = { "id", "direction", "source", "reference", "note" };
    public static readonly string[] RegressionColumns =
        { "id", "bench_id", "direction", "source", "wrong_output", "expected", "note" };
    public static readonly string[] Directions = { "jp-en", "en-jp" };

    public static string RepoRoot { get; } = FindRepoRoot();

    public static string BenchPath(string fileName) => Path.Combine(RepoRoot, "bench", fileName);

    public static IReadOnlyList<BenchCase> LoadCases(string? path = null) =>
        ReadRows(path ?? BenchPath("cases.tsv"), CaseColumns)
            .Select(r => new BenchCase(r[0], r[1], r[2], r[3], r[4]))
            .ToList();

    public static IReadOnlyList<RegressionCase> LoadRegression(string? path = null) =>
        ReadRows(path ?? BenchPath("regression.tsv"), RegressionColumns)
            .Select(r => new RegressionCase(r[0], r[1], r[2], r[3], r[4], r[5], r[6]))
            .ToList();

    /// <summary>
    /// Reads a UTF-8 TSV with a mandatory header row. Blank lines and lines starting with '#'
    /// are skipped. Every data row must have exactly the header's column count; no quoting.
    /// </summary>
    public static IEnumerable<string[]> ReadRows(string path, IReadOnlyList<string> expectedHeader)
    {
        var lines = File.ReadAllLines(path, Encoding.UTF8);
        if (lines.Length == 0)
            throw new InvalidDataException($"{path}: empty file");

        var header = lines[0].TrimStart('\uFEFF').Split('\t');
        if (!header.SequenceEqual(expectedHeader))
            throw new InvalidDataException(
                $"{path}: header is '{string.Join(",", header)}', expected '{string.Join(",", expectedHeader)}'");

        for (var i = 1; i < lines.Length; i++)
        {
            var line = lines[i].TrimEnd('\r');
            if (line.Length == 0 || line.StartsWith('#'))
                continue;

            var fields = line.Split('\t');
            if (fields.Length != header.Length)
                throw new InvalidDataException(
                    $"{path}:{i + 1}: {fields.Length} columns, expected {header.Length}");
            if (fields.Any(string.IsNullOrWhiteSpace))
                throw new InvalidDataException($"{path}:{i + 1}: empty field");

            yield return fields;
        }
    }

    private static string FindRepoRoot()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir != null; dir = dir.Parent)
        {
            if (File.Exists(Path.Combine(dir.FullName, "Kotodama.sln")))
                return dir.FullName;
        }

        throw new DirectoryNotFoundException("Kotodama.sln not found above " + AppContext.BaseDirectory);
    }
}
