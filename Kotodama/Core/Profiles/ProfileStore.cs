using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using EchoXIV.Plamo;

namespace EchoXIV.Profiles
{
    /// <summary>What a profile adds to one PLaMo request.</summary>
    public sealed record ProfilePrompt(string Context, IReadOnlyList<GlossaryTerm> Glossary);

    /// <summary>
    /// Holds the loaded profiles. <see cref="Reload"/> does the file I/O (call it off the framework
    /// thread) and swaps in a new immutable snapshot; readers on any thread see either the old or
    /// the new snapshot, never a half-loaded one.
    /// </summary>
    public sealed class ProfileStore
    {
        private readonly string _root;
        private IReadOnlyList<TranslationProfile> _profiles = Array.Empty<TranslationProfile>();

        public ProfileStore(string root)
        {
            _root = root;
        }

        public string Root => _root;

        public IReadOnlyList<TranslationProfile> Profiles => Volatile.Read(ref _profiles);

        public IReadOnlyList<TranslationProfile> Reload()
        {
            var loaded = ProfileLoader.LoadAll(_root);
            Volatile.Write(ref _profiles, loaded);
            return loaded;
        }

        /// <summary>The named profile (case-insensitive), or <see cref="TranslationProfile.Empty"/>.</summary>
        public TranslationProfile Get(string? name)
        {
            if (string.IsNullOrWhiteSpace(name))
                return TranslationProfile.Empty;

            return Profiles.FirstOrDefault(p => string.Equals(p.Name, name.Trim(), StringComparison.OrdinalIgnoreCase))
                   ?? TranslationProfile.Empty;
        }

        /// <summary>Context and matching glossary terms for one message.</summary>
        public static ProfilePrompt PromptFor(
            TranslationProfile profile,
            TranslationDirection direction,
            string text,
            IEnumerable<GlossaryTerm>? extraTerms = null,
            int cap = GlossaryMatcher.DefaultCap)
        {
            var terms = GlossaryMatcher.Match(text, direction, profile.Glossary, cap).ToList();

            // Extra identity terms (player names, protected tokens) go first, without duplicates.
            if (extraTerms != null)
            {
                var extras = extraTerms
                    .Where(e => !terms.Any(t => string.Equals(t.Source, e.Source, StringComparison.OrdinalIgnoreCase)))
                    .ToList();
                terms = extras.Concat(terms).Take(cap).ToList();
            }

            return new ProfilePrompt(profile.ContextFor(direction), terms);
        }
    }
}
