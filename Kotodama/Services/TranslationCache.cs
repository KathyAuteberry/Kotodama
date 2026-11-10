using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace EchoXIV.Services
{
    /// <summary>
    /// Persistent translation cache. Keys come from <see cref="CacheKey"/> and include the variant
    /// (engine, model, prompt version) that produced the translation. Only successful translations
    /// may be added; callers must never cache a failure.
    /// </summary>
    public sealed class TranslationCache : IDisposable
    {
        private const int MaxEntries = 5000;
        private static readonly TimeSpan MaxAge = TimeSpan.FromDays(30);

        private readonly string _cacheFilePath;
        private readonly object _gate = new();
        private readonly object _fileGate = new();
        private readonly Dictionary<string, string> _cache = new();
        private readonly Dictionary<string, string> _romanizationCache = new();
        private bool _isDirty;
        private long _changeVersion;
        private int _backgroundSaveRunning;

        public TranslationCache(string configDir)
        {
            _cacheFilePath = Path.Combine(configDir, "translation_cache.json");
            Load();
        }

        private void Load()
        {
            try
            {
                if (!File.Exists(_cacheFilePath)) return;
                if (DateTime.UtcNow - File.GetLastWriteTimeUtc(_cacheFilePath) > MaxAge) return;

                var root = JObject.Parse(File.ReadAllText(_cacheFilePath));
                var translations = root["translations"]?.ToObject<Dictionary<string, string>>();
                var romanizations = root["romanizations"]?.ToObject<Dictionary<string, string>>();

                // Entries written with an older key format (no engine variant) are dropped: they may
                // hold another engine's output, e.g. from a cloud service.
                var dropped = false;
                foreach (var kvp in translations ?? new Dictionary<string, string>())
                {
                    if (CacheKey.IsCurrent(kvp.Key)) _cache[kvp.Key] = kvp.Value;
                    else dropped = true;
                }
                foreach (var kvp in romanizations ?? new Dictionary<string, string>())
                {
                    if (CacheKey.IsCurrent(kvp.Key)) _romanizationCache[kvp.Key] = kvp.Value;
                    else dropped = true;
                }

                // Legacy flat format or dropped entries: rewrite the file on the next flush.
                _isDirty = dropped || translations == null;
            }
            catch (Exception)
            {
                // A corrupt cache must not break the plugin.
            }
        }

        public bool TryGetWithRomanization(
            string variant,
            string text,
            string sourceLang,
            string targetLang,
            out string? translated,
            out string? romanized)
        {
            translated = null;
            romanized = null;

            var key = CacheKey.Build(variant, text, sourceLang, targetLang);
            lock (_gate)
            {
                if (!_cache.TryGetValue(key, out var cachedTranslation))
                    return false;

                translated = cachedTranslation;
                _romanizationCache.TryGetValue(key, out romanized);
                return true;
            }
        }

        public void Add(string variant, string text, string sourceLang, string targetLang, string translated, string? romanized = null)
        {
            var key = CacheKey.Build(variant, text, sourceLang, targetLang);
            lock (_gate)
            {
                if (!_cache.TryGetValue(key, out var existingTranslation) || existingTranslation != translated)
                {
                    _cache[key] = translated;
                    _isDirty = true;
                    _changeVersion++;
                }

                if (!string.IsNullOrEmpty(romanized) &&
                    (!_romanizationCache.TryGetValue(key, out var existingRomanized) || existingRomanized != romanized))
                {
                    _romanizationCache[key] = romanized!;
                    _isDirty = true;
                    _changeVersion++;
                }

                TrimToLimit(_cache);
                TrimToLimit(_romanizationCache);
            }
        }

        /// <summary>
        /// Flushes on a worker thread so the framework thread never blocks on file I/O.
        /// At most one background flush runs at a time; no-op when nothing changed.
        /// </summary>
        public void SaveInBackground()
        {
            lock (_gate)
            {
                if (!_isDirty) return;
            }

            if (Interlocked.Exchange(ref _backgroundSaveRunning, 1) != 0)
                return;

            _ = Task.Run(() =>
            {
                try
                {
                    Save();
                }
                finally
                {
                    Volatile.Write(ref _backgroundSaveRunning, 0);
                }
            });
        }

        /// <summary>Synchronous flush (plugin unload).</summary>
        public void Save()
        {
            JObject root;
            long snapshotVersion;
            lock (_gate)
            {
                if (!_isDirty) return;
                root = BuildSnapshot();
                snapshotVersion = _changeVersion;
            }

            try
            {
                lock (_fileGate)
                {
                    var temporaryPath = _cacheFilePath + ".tmp";
                    File.WriteAllText(temporaryPath, root.ToString(Formatting.Indented));
                    File.Move(temporaryPath, _cacheFilePath, true);
                }

                lock (_gate)
                {
                    // Keep dirty state when a write raced with a new cache entry.
                    if (_changeVersion == snapshotVersion)
                        _isDirty = false;
                }
            }
            catch (Exception)
            {
                // Keep the dirty flag so the next scheduled flush retries.
            }
        }

        private static void TrimToLimit(Dictionary<string, string> cache)
        {
            while (cache.Count > MaxEntries)
                cache.Remove(cache.Keys.First());
        }

        private JObject BuildSnapshot()
        {
            return new JObject
            {
                ["translations"] = JObject.FromObject(_cache),
                ["romanizations"] = _romanizationCache.Count > 0
                    ? JObject.FromObject(_romanizationCache)
                    : new JObject()
            };
        }

        public void Dispose() => Save();
    }
}
