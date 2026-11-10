using EchoXIV;
using EchoXIV.Services;
using Xunit;

namespace Kotodama.Tests;

public sealed class TranslationCacheTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("kotodama-cache-").FullName;
    private static readonly string Plamo = CacheKey.Variant(TranslationEngine.LocalAI, "plamo");
    private static readonly string Google = CacheKey.Variant(TranslationEngine.Google, "");

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private string CacheFile => Path.Combine(_dir, "translation_cache.json");

    [Fact]
    public void Entries_are_scoped_to_the_engine_variant()
    {
        using var cache = new TranslationCache(_dir);
        cache.Add(Google, "軽減お願いします", "auto", "en", "Please reduce the amount.");

        Assert.False(cache.TryGetWithRomanization(Plamo, "軽減お願いします", "auto", "en", out _, out _));
        Assert.True(cache.TryGetWithRomanization(Google, "軽減お願いします", "auto", "en", out var translated, out _));
        Assert.Equal("Please reduce the amount.", translated);
    }

    [Fact]
    public void Entries_survive_save_and_reload()
    {
        using (var cache = new TranslationCache(_dir))
        {
            cache.Add(Plamo, "stack", "en", "ja", "頭割り", "atamawari");
            cache.Save();
        }

        using var reloaded = new TranslationCache(_dir);
        Assert.True(reloaded.TryGetWithRomanization(Plamo, "stack", "en", "ja", out var translated, out var romanized));
        Assert.Equal("頭割り", translated);
        Assert.Equal("atamawari", romanized);
    }

    [Fact]
    public void Old_format_entries_without_an_engine_are_dropped_on_load()
    {
        // Upstream v2 keys: no engine, so they may hold a cloud engine's output.
        File.WriteAllText(CacheFile, """
            { "translations": { "v2|5:hello4:auto2:ja": "こんにちは" }, "romanizations": {} }
            """);

        using (var cache = new TranslationCache(_dir))
        {
            cache.Save(); // load marked the file dirty because entries were dropped
        }

        Assert.DoesNotContain("v2|", File.ReadAllText(CacheFile));
    }

    [Fact]
    public void Legacy_flat_format_is_dropped()
    {
        File.WriteAllText(CacheFile, """{ "hello|auto|ja": "こんにちは" }""");

        using var cache = new TranslationCache(_dir);
        cache.Save();

        Assert.DoesNotContain("hello", File.ReadAllText(CacheFile));
    }

    [Fact]
    public void Background_save_writes_the_file_without_blocking_the_caller()
    {
        using var cache = new TranslationCache(_dir);
        cache.Add(Plamo, "food", "en", "ja", "ご飯");

        cache.SaveInBackground();

        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (!File.Exists(CacheFile) && DateTime.UtcNow < deadline)
            Thread.Sleep(20);
        Assert.True(File.Exists(CacheFile));
    }

    [Fact]
    public void Corrupt_file_does_not_throw()
    {
        File.WriteAllText(CacheFile, "{ not json");

        using var cache = new TranslationCache(_dir);
        Assert.False(cache.TryGetWithRomanization(Plamo, "x", "en", "ja", out _, out _));
    }
}
