using EchoXIV.Plamo;
using EchoXIV.Profiles;
using Kotodama.Tests.Fixtures;
using Xunit;

namespace Kotodama.Tests;

public sealed class ProfileInstallerTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("kotodama-install-").FullName;

    public void Dispose() => Directory.Delete(_root, recursive: true);

    private static readonly DefaultProfileFile[] Defaults =
    {
        new("Raid", "context.txt", "raid context v1"),
        new("Raid", "glossary.tsv", "散開\tspread\n"),
        new("Casual", "context.txt", "casual context v1"),
    };

    [Fact]
    public void Fresh_install_creates_every_profile_file_and_the_defaults_copy()
    {
        var result = ProfileInstaller.Install(_root, Defaults);

        Assert.Empty(result.Errors);
        Assert.Equal(3, result.Created.Count);
        Assert.Equal("raid context v1", File.ReadAllText(Path.Combine(_root, "Raid", "context.txt")));
        Assert.Equal("raid context v1", File.ReadAllText(Path.Combine(_root, ".defaults", "Raid", "context.txt")));
        Assert.True(File.Exists(Path.Combine(_root, ".defaults", "README.txt")));
    }

    [Fact]
    public void User_edits_are_never_overwritten_but_defaults_copy_is_refreshed()
    {
        ProfileInstaller.Install(_root, Defaults);
        File.WriteAllText(Path.Combine(_root, "Raid", "context.txt"), "my edits");

        var upgraded = Defaults.Select(d => d with { Content = d.Content.Replace("v1", "v2") }).ToArray();
        var result = ProfileInstaller.Install(_root, upgraded);

        Assert.Empty(result.Created);
        Assert.Equal("my edits", File.ReadAllText(Path.Combine(_root, "Raid", "context.txt")));
        Assert.Equal("raid context v2", File.ReadAllText(Path.Combine(_root, ".defaults", "Raid", "context.txt")));
    }

    [Fact]
    public void A_deleted_file_is_restored_and_a_new_default_file_appears()
    {
        ProfileInstaller.Install(_root, Defaults);
        File.Delete(Path.Combine(_root, "Raid", "glossary.tsv"));

        var result = ProfileInstaller.Install(_root, Defaults.Append(new DefaultProfileFile("Raid", "context.en-ja.txt", "new")));

        Assert.Equal(new[] { "Raid/glossary.tsv", "Raid/context.en-ja.txt" }, result.Created);
    }

    [Theory]
    [InlineData("..", "context.txt")]
    [InlineData(".defaults", "context.txt")]
    [InlineData("Raid", "../escape.txt")]
    public void Unsafe_names_are_rejected(string profile, string file)
    {
        var result = ProfileInstaller.Install(_root, new[] { new DefaultProfileFile(profile, file, "x") });

        Assert.Single(result.Errors);
        Assert.Empty(result.Created);
    }

    [Fact]
    public void Defaults_directory_is_not_loaded_as_a_profile()
    {
        ProfileInstaller.Install(_root, Defaults);

        Assert.Equal(new[] { "Casual", "Raid" }, ProfileLoader.LoadAll(_root).Select(p => p.Name));
    }

    /// <summary>The real shipped defaults (Kotodama/Profiles/Defaults) parse cleanly and are well-formed.</summary>
    [Fact]
    public void Shipped_default_profiles_load_without_warnings()
    {
        var defaultsDir = Path.Combine(BenchFixtures.RepoRoot, "Kotodama", "Profiles", "Defaults");
        var profiles = ProfileLoader.LoadAll(defaultsDir);

        Assert.Equal(new[] { "Casual", "Raid" }, profiles.Select(p => p.Name));
        Assert.All(profiles, p => Assert.Empty(p.Warnings));
        Assert.All(profiles, p => Assert.False(string.IsNullOrWhiteSpace(p.ContextFor(TranslationDirection.EnToJa))));

        var raid = profiles.Single(p => p.Name == "Raid");
        Assert.True(raid.Glossary.Count >= 60);
        // Targets are terms, not explanations: no sentence punctuation and short.
        Assert.All(raid.Glossary, e => Assert.True(e.En.All(a => a.Length <= 30 && !a.EndsWith('.')), $"line {e.Line}"));
        Assert.All(raid.Glossary, e => Assert.True(e.Ja.Length <= 12, $"line {e.Line}"));
    }

    [Theory]
    [InlineData("頭割り", "stack")]
    [InlineData("軽減", "mitigation")]
    [InlineData("無敵", "invuln")]
    [InlineData("牽制", "Feint")]
    [InlineData("アムレン", "Arm's Length")]
    [InlineData("リビデ", "Living Dead")]
    [InlineData("ご飯", "food")]
    [InlineData("薬", "pot")]
    [InlineData("散開", "spread")]
    [InlineData("タゲサ", "hitbox")]
    public void Raid_glossary_covers_the_requested_terms(string ja, string en)
    {
        var raid = ProfileLoader.Load(Path.Combine(BenchFixtures.RepoRoot, "Kotodama", "Profiles", "Defaults", "Raid"));
        Assert.Contains(raid.Glossary, e => e.Ja == ja && e.En.Contains(en));
    }
}
