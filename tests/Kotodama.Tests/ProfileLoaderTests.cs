using EchoXIV.Plamo;
using EchoXIV.Profiles;
using Xunit;

namespace Kotodama.Tests;

public sealed class ProfileLoaderTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("kotodama-profiles-").FullName;

    public void Dispose() => Directory.Delete(_root, recursive: true);

    private string WriteProfile(string name, Dictionary<string, string> files)
    {
        var dir = Path.Combine(_root, name);
        Directory.CreateDirectory(dir);
        foreach (var (file, content) in files)
            File.WriteAllText(Path.Combine(dir, file), content);
        return dir;
    }

    [Fact]
    public void Parses_terms_aliases_notes_and_directions()
    {
        var warnings = new List<string>();
        var entries = ProfileLoader.ParseGlossary(
            "ja\ten\tnote\tdirection\n" +
            "# comment\n" +
            "頭割り\tstack|stacks|stacking\tshared damage\n" +
            "\n" +
            "ナイト\tPLD\tjob\tja-en\n" +
            "落ちる\tcrash|crashed\t\ten-ja\n",
            warnings);

        Assert.Empty(warnings);
        Assert.Equal(3, entries.Count);
        Assert.Equal("stack", entries[0].CanonicalEn);
        Assert.Equal(new[] { "stack", "stacks", "stacking" }, entries[0].En);
        Assert.Equal("shared damage", entries[0].Note);
        Assert.Equal(GlossaryScope.Both, entries[0].Scope);
        Assert.Equal(GlossaryScope.JaToEn, entries[1].Scope);
        Assert.Equal(GlossaryScope.EnToJa, entries[2].Scope);
        Assert.Null(entries[2].Note);
        Assert.Equal(5, entries[1].Line);
    }

    [Theory]
    [InlineData("onlyone")]
    [InlineData("a\tb\tc\td\te")]
    [InlineData("\tstack")]
    [InlineData("頭割り\t | ")]
    [InlineData("頭割り\tstack\tnote\tsideways")]
    public void Bad_lines_become_warnings_not_exceptions(string line)
    {
        var warnings = new List<string>();
        var entries = ProfileLoader.ParseGlossary("散開\tspread\n" + line + "\n", warnings);

        Assert.Single(entries);
        Assert.Single(warnings);
        Assert.Contains("line 2", warnings[0]);
    }

    [Fact]
    public void Crlf_and_bom_are_handled()
    {
        var warnings = new List<string>();
        var entries = ProfileLoader.ParseGlossary("﻿散開\tspread\r\n塔\ttower\r\n", warnings);

        Assert.Empty(warnings);
        Assert.Equal(new[] { "散開", "塔" }, entries.Select(e => e.Ja));
        Assert.Equal("tower", entries[1].CanonicalEn);
    }

    [Theory]
    [InlineData(GlossaryScope.Both, TranslationDirection.JaToEn, true)]
    [InlineData(GlossaryScope.Both, TranslationDirection.EnToJa, true)]
    [InlineData(GlossaryScope.JaToEn, TranslationDirection.EnToJa, false)]
    [InlineData(GlossaryScope.EnToJa, TranslationDirection.EnToJa, true)]
    public void Scope_filters_by_direction(GlossaryScope scope, TranslationDirection direction, bool expected)
    {
        Assert.Equal(expected, new ProfileGlossaryEntry("a", new[] { "b" }, null, scope, 1).AppliesTo(direction));
    }

    [Fact]
    public void Loads_a_profile_directory_with_direction_specific_context()
    {
        var dir = WriteProfile("Raid", new()
        {
            ["context.txt"] = "FFXIV raid chat.\n",
            ["context.en-ja.txt"] = "Use PF register.",
            ["glossary.tsv"] = "散開\tspread\n",
        });

        var profile = ProfileLoader.Load(dir);

        Assert.Equal("Raid", profile.Name);
        Assert.Equal("FFXIV raid chat.\nUse PF register.", profile.ContextFor(TranslationDirection.EnToJa));
        Assert.Equal("FFXIV raid chat.", profile.ContextFor(TranslationDirection.JaToEn));
        Assert.Single(profile.Glossary);
    }

    [Fact]
    public void Missing_files_give_an_empty_but_valid_profile()
    {
        var profile = ProfileLoader.Load(WriteProfile("Bare", new()));

        Assert.Equal(string.Empty, profile.ContextFor(TranslationDirection.JaToEn));
        Assert.Empty(profile.Glossary);
        Assert.Empty(profile.Warnings);
    }

    [Fact]
    public void Content_hash_changes_when_any_file_changes()
    {
        var dir = WriteProfile("Raid", new() { ["context.txt"] = "a", ["glossary.tsv"] = "散開\tspread\n" });
        var before = ProfileLoader.Load(dir).ContentHash;

        File.WriteAllText(Path.Combine(dir, "glossary.tsv"), "散開\tspreads\n");
        var afterGlossary = ProfileLoader.Load(dir).ContentHash;
        File.WriteAllText(Path.Combine(dir, "context.ja-en.txt"), "x");
        var afterContext = ProfileLoader.Load(dir).ContentHash;

        Assert.NotEqual(before, afterGlossary);
        Assert.NotEqual(afterGlossary, afterContext);
        Assert.Equal(afterContext, ProfileLoader.Load(dir).ContentHash);
    }

    [Fact]
    public void LoadAll_skips_dot_directories_and_sorts_by_name()
    {
        WriteProfile("Raid", new());
        WriteProfile("Casual", new());
        WriteProfile(".defaults", new());

        Assert.Equal(new[] { "Casual", "Raid" }, ProfileLoader.LoadAll(_root).Select(p => p.Name));
    }

    [Fact]
    public void LoadAll_of_a_missing_root_is_empty()
    {
        Assert.Empty(ProfileLoader.LoadAll(Path.Combine(_root, "nope")));
    }
}
