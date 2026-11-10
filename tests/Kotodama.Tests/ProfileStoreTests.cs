using EchoXIV;
using EchoXIV.Plamo;
using EchoXIV.Profiles;
using Xunit;

namespace Kotodama.Tests;

public sealed class ProfileStoreTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("kotodama-store-").FullName;

    public void Dispose() => Directory.Delete(_root, recursive: true);

    private void Write(string profile, string file, string content)
    {
        Directory.CreateDirectory(Path.Combine(_root, profile));
        File.WriteAllText(Path.Combine(_root, profile, file), content);
    }

    [Fact]
    public void Get_is_case_insensitive_and_falls_back_to_empty()
    {
        Write("Raid", "context.txt", "raid");
        var store = new ProfileStore(_root);
        store.Reload();

        Assert.Equal("Raid", store.Get("raid").Name);
        Assert.Same(TranslationProfile.Empty, store.Get("Missing"));
        Assert.Same(TranslationProfile.Empty, store.Get(null));
    }

    [Fact]
    public void Reload_picks_up_edits_and_new_profiles()
    {
        Write("Raid", "glossary.tsv", "散開\tspread\n");
        var store = new ProfileStore(_root);
        store.Reload();
        var before = store.Get("Raid").ContentHash;

        Write("Raid", "glossary.tsv", "散開\tspread\n塔\ttower\n");
        Write("Casual", "context.txt", "casual");
        store.Reload();

        Assert.NotEqual(before, store.Get("Raid").ContentHash);
        Assert.Equal(2, store.Get("Raid").Glossary.Count);
        Assert.Equal(new[] { "Casual", "Raid" }, store.Profiles.Select(p => p.Name));
    }

    [Fact]
    public void Prompt_contains_direction_context_and_only_matching_terms()
    {
        Write("Raid", "context.txt", "Raid chat.");
        Write("Raid", "context.en-ja.txt", "PF register.");
        Write("Raid", "glossary.tsv", "ご飯\tfood\n頭割り\tstack\n散開\tspread\n");
        var store = new ProfileStore(_root);
        store.Reload();

        var prompt = ProfileStore.PromptFor(store.Get("Raid"), TranslationDirection.EnToJa, "Check your food before the stack.");

        Assert.Equal("Raid chat.\nPF register.", prompt.Context);
        Assert.Equal(new[] { new GlossaryTerm("food", "ご飯"), new GlossaryTerm("stack", "頭割り") }, prompt.Glossary);
    }

    [Fact]
    public void Extra_identity_terms_come_first_without_duplicates()
    {
        Write("Raid", "glossary.tsv", "MT\tMT\n");
        var store = new ProfileStore(_root);
        store.Reload();

        var prompt = ProfileStore.PromptFor(store.Get("Raid"), TranslationDirection.EnToJa, "Kath is MT",
            new[] { new GlossaryTerm("Kath", "Kath"), new GlossaryTerm("MT", "MT") });

        Assert.Equal(new[] { "Kath", "MT" }, prompt.Glossary.Select(t => t.Source));
    }

    [Fact]
    public void Empty_profile_gives_an_empty_prompt()
    {
        var prompt = ProfileStore.PromptFor(TranslationProfile.Empty, TranslationDirection.JaToEn, "散開");

        Assert.Equal(string.Empty, prompt.Context);
        Assert.Empty(prompt.Glossary);
    }

    [Fact]
    public void Profile_is_part_of_the_cache_variant()
    {
        var a = CacheKey.Variant(TranslationEngine.Plamo, "m", "Raid", "aaaa");
        var b = CacheKey.Variant(TranslationEngine.Plamo, "m", "Raid", "bbbb");
        var c = CacheKey.Variant(TranslationEngine.Plamo, "m", "Casual", "aaaa");

        Assert.NotEqual(a, b);
        Assert.NotEqual(a, c);
        Assert.StartsWith(CacheKey.Variant(TranslationEngine.Plamo, "m"), a);
    }
}
