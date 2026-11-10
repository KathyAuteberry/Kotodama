using EchoXIV;
using Xunit;

namespace Kotodama.Tests;

public class CacheKeyTests
{
    [Fact]
    public void Different_engines_give_different_keys()
    {
        var google = CacheKey.Build(CacheKey.Variant(TranslationEngine.Google, ""), "軽減お願いします", "auto", "en");
        var local = CacheKey.Build(CacheKey.Variant(TranslationEngine.LocalAI, ""), "軽減お願いします", "auto", "en");

        Assert.NotEqual(google, local);
    }

    [Fact]
    public void Different_models_give_different_keys()
    {
        var a = CacheKey.Variant(TranslationEngine.LocalAI, "hf.co/pfnet/plamo-3-translate-8b-GGUF:Q4_K_M");
        var b = CacheKey.Variant(TranslationEngine.LocalAI, "qwen3:8b");

        Assert.NotEqual(CacheKey.Build(a, "x", "en", "ja"), CacheKey.Build(b, "x", "en", "ja"));
    }

    [Fact]
    public void Variant_includes_the_prompt_version()
    {
        Assert.Contains($"p{CacheKey.PromptVersion}", CacheKey.Variant(TranslationEngine.LocalAI, "m"));
    }

    [Fact]
    public void Model_whitespace_and_null_are_normalized()
    {
        Assert.Equal(CacheKey.Variant(TranslationEngine.LocalAI, " m "), CacheKey.Variant(TranslationEngine.LocalAI, "m"));
        Assert.Equal(CacheKey.Variant(TranslationEngine.Google, null), CacheKey.Variant(TranslationEngine.Google, ""));
    }

    [Fact]
    public void Language_pair_and_text_are_part_of_the_key()
    {
        var v = CacheKey.Variant(TranslationEngine.LocalAI, "m");

        Assert.NotEqual(CacheKey.Build(v, "stack", "en", "ja"), CacheKey.Build(v, "stack", "ja", "en"));
        Assert.NotEqual(CacheKey.Build(v, "stack", "en", "ja"), CacheKey.Build(v, "stacks", "en", "ja"));
    }

    [Fact]
    public void Field_boundaries_cannot_collide()
    {
        // Same concatenated characters, different field split.
        Assert.NotEqual(CacheKey.Build("v", "ab", "c", "d"), CacheKey.Build("v", "a", "bc", "d"));
        Assert.NotEqual(CacheKey.Build("v|1", "x", "en", "ja"), CacheKey.Build("v", "1|x", "en", "ja"));
    }

    [Theory]
    [InlineData("v2|5:hello2:en2:ja", false)]
    [InlineData("hello", false)]
    public void Old_format_keys_are_not_current(string key, bool expected)
    {
        Assert.Equal(expected, CacheKey.IsCurrent(key));
    }

    [Fact]
    public void Built_keys_are_current()
    {
        Assert.True(CacheKey.IsCurrent(CacheKey.Build("v", "t", "en", "ja")));
    }
}
