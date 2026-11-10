using System;
using System.Collections.Generic;
using System.Text;

namespace EchoXIV.Plamo
{
    /// <summary>JA⇄EN only: Kotodama always sends an explicit direction to PLaMo.</summary>
    public enum TranslationDirection
    {
        JaToEn,
        EnToJa,
    }

    /// <summary>A glossary entry as sent to PLaMo: a clean source term and a clean target term (no notes).</summary>
    public sealed record GlossaryTerm(string Source, string Target);

    /// <summary>A previous source/translation pair (conversational history; P5, not used yet).</summary>
    public sealed record TranslationPair(string Source, string Translation);

    public sealed record Pfml3Request(TranslationDirection Direction, string Text)
    {
        /// <summary>Free-text reference context (profile context, P4).</summary>
        public string? Context { get; init; }

        /// <summary>Glossary entries that apply to this message, in the request direction.</summary>
        public IReadOnlyList<GlossaryTerm> Glossary { get; init; } = Array.Empty<GlossaryTerm>();

        public IReadOnlyList<TranslationPair> History { get; init; } = Array.Empty<TranslationPair>();

        /// <summary>
        /// Japanese register ("polite" or "plain"); only sent for EN→JA. Null defaults to polite,
        /// which is what the benchmark used.
        /// </summary>
        public string? Form { get; init; }

        /// <summary>
        /// Adds the source language key to inputs. Off by default: the benchmark prompt omits it,
        /// and adding it measurably changes outputs (docs/AUDIT.md §4.1).
        /// </summary>
        public bool IncludeSourceLanguage { get; init; }
    }

    /// <summary>
    /// Builds PLaMo 3 Translate PFML3 prompts for Ollama's raw <c>/api/generate</c> (docs/AUDIT.md §4).
    /// Element order follows the GGUF chat template: dataset, context, history pairs, glossary, input,
    /// output. Without history the result is byte-identical to the benchmark script's prompt.
    /// </summary>
    public static class Pfml3PromptBuilder
    {
        public const string Tag = "<|plamo:tag|>";
        public const string Msg = "<|plamo:msg|>";
        public const string Key = "<|plamo:key|>";
        public const string Val = "<|plamo:val|>";
        public const string Index = "<|plamo:index|>";
        public const string Item = "<|plamo:item|>";

        /// <summary>Stop sequences every request must send (the model defines none).</summary>
        public static readonly IReadOnlyList<string> StopSequences = new[] { "<|plamo:tag|>", "<|plamo:bos|>" };

        private const string ReservedPrefix = "<|plamo:";

        public static string Build(Pfml3Request request)
        {
            var (sourceLang, targetLang) = Languages(request.Direction);
            var builder = new StringBuilder(256);

            builder.Append(Tag).Append("dataset").Append(Msg).Append("translation");

            if (!string.IsNullOrWhiteSpace(request.Context))
                builder.Append(Tag).Append("context").Append(Msg).Append(Neutralize(request.Context!));

            foreach (var pair in request.History)
            {
                AppendInput(builder, pair.Source, sourceLang, request.IncludeSourceLanguage);
                builder.Append(Tag).Append("output").Append(Key).Append("lang").Append(Val).Append(targetLang)
                    .Append(Msg).Append(Neutralize(pair.Translation));
            }

            if (request.Glossary.Count > 0)
            {
                builder.Append(Tag).Append("glossary");
                foreach (var term in request.Glossary)
                {
                    builder.Append(Index).Append(Neutralize(term.Source))
                        .Append(Item).Append(Neutralize(term.Target));
                }
            }

            AppendInput(builder, request.Text, sourceLang, request.IncludeSourceLanguage);
            builder.Append(Tag).Append("output").Append(Key).Append("lang").Append(Val).Append(targetLang);

            if (request.Direction == TranslationDirection.EnToJa)
                builder.Append(Key).Append("form").Append(Val).Append(NormalizeForm(request.Form));

            builder.Append(Msg);
            return builder.ToString();
        }

        public static (string Source, string Target) Languages(TranslationDirection direction) =>
            direction == TranslationDirection.JaToEn ? ("ja", "en") : ("en", "ja");

        /// <summary>
        /// Breaks any reserved PFML token in user-supplied text by inserting U+200B after '&lt;', so
        /// chat from other players can't inject prompt structure (and the GGUF template's check
        /// can't turn it into an HTTP 500).
        /// </summary>
        public static string Neutralize(string text) =>
            text.Contains(ReservedPrefix, StringComparison.Ordinal)
                ? text.Replace(ReservedPrefix, "<​|plamo:", StringComparison.Ordinal)
                : text;

        private static void AppendInput(StringBuilder builder, string text, string sourceLang, bool includeSourceLang)
        {
            builder.Append(Tag).Append("input");
            if (includeSourceLang)
                builder.Append(Key).Append("lang").Append(Val).Append(sourceLang);
            builder.Append(Msg).Append(Neutralize(text));
        }

        private static string NormalizeForm(string? form) => form is "plain" ? "plain" : "polite";
    }
}
