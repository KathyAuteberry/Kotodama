using System.Text.RegularExpressions;
using Kotodama.Tests.Fixtures;
using Xunit;

namespace Kotodama.Tests;

public class FixtureTests
{
    private static readonly IReadOnlyList<BenchCase> Cases = BenchFixtures.LoadCases();
    private static readonly IReadOnlyList<RegressionCase> Regression = BenchFixtures.LoadRegression();

    [Fact]
    public void Bench_has_24_cases_per_direction_with_unique_ids()
    {
        Assert.Equal(48, Cases.Count);
        Assert.Equal(Cases.Count, Cases.Select(c => c.Id).Distinct().Count());
        Assert.All(Cases, c => Assert.Contains(c.Direction, BenchFixtures.Directions));
        Assert.Equal(24, Cases.Count(c => c.Direction == "jp-en"));
        Assert.Equal(24, Cases.Count(c => c.Direction == "en-jp"));
    }

    [Fact]
    public void Bench_ids_match_their_direction()
    {
        Assert.All(Cases, c => Assert.Matches(c.Direction == "jp-en" ? "^JP[0-9]{2}$" : "^EN[0-9]{2}$", c.Id));
    }

    [Fact]
    public void Regression_rows_have_unique_ids_and_point_at_matching_bench_cases()
    {
        Assert.Equal(7, Regression.Count);
        Assert.Equal(Regression.Count, Regression.Select(r => r.Id).Distinct().Count());

        var byId = Cases.ToDictionary(c => c.Id);
        Assert.All(Regression, r =>
        {
            Assert.True(byId.TryGetValue(r.BenchId, out var bench), $"{r.Id}: unknown bench id {r.BenchId}");
            Assert.Equal(bench!.Source, r.Source);
            Assert.Equal(bench.Direction, r.Direction);
        });
    }

    [Fact]
    public void Header_mismatch_is_rejected()
    {
        var path = Path.GetTempFileName();
        try
        {
            File.WriteAllText(path, "id\tdirection\tsource\n");
            Assert.Throws<InvalidDataException>(() => BenchFixtures.LoadCases(path).ToList());
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Wrong_column_count_is_rejected()
    {
        var path = Path.GetTempFileName();
        try
        {
            File.WriteAllText(path, string.Join('\t', BenchFixtures.CaseColumns) + "\nJP01\tjp-en\t軽減\n");
            Assert.Throws<InvalidDataException>(() => BenchFixtures.LoadCases(path).ToList());
        }
        finally
        {
            File.Delete(path);
        }
    }

    /// <summary>
    /// Guard against teaching to the test: no prompt, context, glossary or other plugin source may
    /// contain a full reference translation from the benchmark or regression set. Generalization
    /// is the goal (kickoff "Regression set"). Scans everything that ships in or builds the plugin.
    /// </summary>
    [Fact]
    public void No_plugin_source_or_default_contains_a_reference_translation()
    {
        var forbidden = ForbiddenReferenceSentences().ToList();
        Assert.NotEmpty(forbidden);

        var pluginDir = Path.Combine(BenchFixtures.RepoRoot, "Kotodama");
        var extensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            { ".cs", ".txt", ".tsv", ".md", ".json", ".resx" };
        var files = Directory.EnumerateFiles(pluginDir, "*", SearchOption.AllDirectories)
            .Where(f => extensions.Contains(Path.GetExtension(f)))
            .Where(f => !IsBuildOutput(Path.GetRelativePath(pluginDir, f)))
            .ToList();
        Assert.NotEmpty(files);

        var hits = new List<string>();
        foreach (var file in files)
        {
            var text = File.ReadAllText(file);
            foreach (var sentence in forbidden.Where(text.Contains))
                hits.Add($"{Path.GetRelativePath(BenchFixtures.RepoRoot, file)}: \"{sentence}\"");
        }

        Assert.True(hits.Count == 0, "Reference translations found in plugin sources:\n" + string.Join('\n', hits));
    }

    private static IEnumerable<string> ForbiddenReferenceSentences()
    {
        // Bench references, except the auto-translate case whose reference is the source itself.
        var references = Cases.Where(c => c.Reference != c.Source).Select(c => c.Reference);

        // Regression expectations are partly prose ("Kath stays as written"); only the Japanese
        // target phrasings are actual translations.
        var regression = Regression.Select(r => r.Expected).Where(ContainsJapanese);

        return references.Concat(regression)
            .Select(StripTrailingPunctuation)
            .Where(s => s.Length >= 6)
            .Distinct();
    }

    private static bool IsBuildOutput(string relativePath)
    {
        var first = relativePath.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)[0];
        return first is "bin" or "obj";
    }

    private static bool ContainsJapanese(string text) =>
        Regex.IsMatch(text, @"[぀-ヿ一-鿿]");

    private static string StripTrailingPunctuation(string text) =>
        text.TrimEnd('.', '。', '!', '！', '?', '？', '…', ' ');
}
