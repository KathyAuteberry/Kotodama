using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using EchoXIV.Plamo;

namespace EchoXIV.Profiles
{
    /// <summary>Which translation direction a glossary entry applies to.</summary>
    public enum GlossaryScope
    {
        Both,
        JaToEn,
        EnToJa,
    }

    /// <summary>
    /// One terminology entry: a Japanese term and its English term(s). The first English alias is
    /// what JA→EN translation uses; every alias is matched for EN→JA. The note is for humans only and
    /// is never sent to the model (explanations leaked into output in the benchmark).
    /// </summary>
    public sealed record ProfileGlossaryEntry(string Ja, IReadOnlyList<string> En, string? Note, GlossaryScope Scope, int Line)
    {
        public string CanonicalEn => En[0];

        public bool AppliesTo(TranslationDirection direction) => Scope switch
        {
            GlossaryScope.JaToEn => direction == TranslationDirection.JaToEn,
            GlossaryScope.EnToJa => direction == TranslationDirection.EnToJa,
            _ => true,
        };
    }

    /// <summary>A translation profile (Raid, Casual, …) loaded from plain files (docs/PLAN.md P4.1).</summary>
    public sealed record TranslationProfile(
        string Name,
        string Context,
        string ContextJaToEn,
        string ContextEnToJa,
        IReadOnlyList<ProfileGlossaryEntry> Glossary,
        IReadOnlyList<string> Warnings,
        string ContentHash)
    {
        public static readonly TranslationProfile Empty = new("None", string.Empty, string.Empty, string.Empty,
            Array.Empty<ProfileGlossaryEntry>(), Array.Empty<string>(), "none");

        /// <summary>Shared context followed by the direction-specific context (blank parts skipped).</summary>
        public string ContextFor(TranslationDirection direction)
        {
            var specific = direction == TranslationDirection.JaToEn ? ContextJaToEn : ContextEnToJa;
            return string.Join("\n", new[] { Context, specific }.Where(s => !string.IsNullOrWhiteSpace(s)).Select(s => s.Trim()));
        }
    }

    /// <summary>
    /// Loads profiles from <c>profiles/&lt;name&gt;/</c>: <c>context.txt</c>, optional
    /// <c>context.ja-en.txt</c> / <c>context.en-ja.txt</c>, and <c>glossary.tsv</c>. Tolerant:
    /// bad glossary lines become warnings, never exceptions.
    /// </summary>
    public static class ProfileLoader
    {
        public const string ContextFile = "context.txt";
        public const string ContextJaEnFile = "context.ja-en.txt";
        public const string ContextEnJaFile = "context.en-ja.txt";
        public const string GlossaryFile = "glossary.tsv";

        public static readonly string[] ProfileFiles = { ContextFile, ContextJaEnFile, ContextEnJaFile, GlossaryFile };

        /// <summary>Every profile directory under <paramref name="profilesRoot"/>; dot-directories (.defaults) are skipped.</summary>
        public static IReadOnlyList<TranslationProfile> LoadAll(string profilesRoot)
        {
            if (!Directory.Exists(profilesRoot))
                return Array.Empty<TranslationProfile>();

            return Directory.EnumerateDirectories(profilesRoot)
                .Where(d => !Path.GetFileName(d).StartsWith('.'))
                .OrderBy(d => Path.GetFileName(d), StringComparer.OrdinalIgnoreCase)
                .Select(Load)
                .ToList();
        }

        public static TranslationProfile Load(string directory)
        {
            var name = Path.GetFileName(directory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
            var context = ReadOptional(Path.Combine(directory, ContextFile));
            var contextJaEn = ReadOptional(Path.Combine(directory, ContextJaEnFile));
            var contextEnJa = ReadOptional(Path.Combine(directory, ContextEnJaFile));
            var glossaryText = ReadOptional(Path.Combine(directory, GlossaryFile));

            var warnings = new List<string>();
            var glossary = ParseGlossary(glossaryText, warnings);
            var hash = Hash(context, contextJaEn, contextEnJa, glossaryText);

            return new TranslationProfile(name, context.Trim(), contextJaEn.Trim(), contextEnJa.Trim(), glossary, warnings, hash);
        }

        /// <summary>
        /// Parses glossary.tsv: <c>ja⇥en⇥note⇥direction</c>, where en may list aliases as
        /// <c>stack|stacks</c> and direction is both (default), ja-en or en-ja. Lines starting with
        /// '#' and blank lines are ignored; a first line of <c>ja⇥en…</c> is treated as a header.
        /// </summary>
        public static IReadOnlyList<ProfileGlossaryEntry> ParseGlossary(string text, List<string> warnings)
        {
            var entries = new List<ProfileGlossaryEntry>();
            var lines = text.TrimStart('﻿').Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');

            for (var i = 0; i < lines.Length; i++)
            {
                var lineNumber = i + 1;
                var line = lines[i];
                if (string.IsNullOrWhiteSpace(line) || line.TrimStart().StartsWith('#'))
                    continue;

                var columns = line.Split('\t');
                if (entries.Count == 0 && columns.Length >= 2 &&
                    columns[0].Trim().Equals("ja", StringComparison.OrdinalIgnoreCase) &&
                    columns[1].Trim().Equals("en", StringComparison.OrdinalIgnoreCase))
                {
                    continue; // header
                }

                if (columns.Length < 2 || columns.Length > 4)
                {
                    warnings.Add($"glossary.tsv line {lineNumber}: expected 2–4 tab-separated columns (ja, en, note, direction), found {columns.Length}.");
                    continue;
                }

                var ja = columns[0].Trim();
                var en = columns[1].Split('|').Select(a => a.Trim()).Where(a => a.Length > 0).ToList();
                if (ja.Length == 0 || en.Count == 0)
                {
                    warnings.Add($"glossary.tsv line {lineNumber}: both the Japanese and the English term are required.");
                    continue;
                }

                var note = columns.Length > 2 && columns[2].Trim().Length > 0 ? columns[2].Trim() : null;
                var scopeText = columns.Length > 3 ? columns[3].Trim().ToLowerInvariant() : string.Empty;
                GlossaryScope scope;
                switch (scopeText)
                {
                    case "":
                    case "both":
                        scope = GlossaryScope.Both;
                        break;
                    case "ja-en":
                        scope = GlossaryScope.JaToEn;
                        break;
                    case "en-ja":
                        scope = GlossaryScope.EnToJa;
                        break;
                    default:
                        warnings.Add($"glossary.tsv line {lineNumber}: direction must be both, ja-en or en-ja (found '{columns[3].Trim()}').");
                        continue;
                }

                entries.Add(new ProfileGlossaryEntry(ja, en, note, scope, lineNumber));
            }

            return entries;
        }

        private static string ReadOptional(string path)
        {
            try
            {
                return File.Exists(path) ? File.ReadAllText(path, Encoding.UTF8).TrimStart('﻿') : string.Empty;
            }
            catch (IOException)
            {
                return string.Empty;
            }
            catch (UnauthorizedAccessException)
            {
                return string.Empty;
            }
        }

        private static string Hash(params string[] parts)
        {
            var joined = string.Join("\u0000", parts);
            var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(joined));
            return Convert.ToHexString(bytes, 0, 8).ToLowerInvariant();
        }
    }
}
