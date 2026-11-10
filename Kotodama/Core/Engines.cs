using System;

namespace EchoXIV
{
    /// <summary>
    /// Translation engine types. Serialized as integers in the plugin config:
    /// append new values at the end, never reorder.
    /// </summary>
    public enum TranslationEngine
    {
        Google,
        Papago,
        LibreTranslate,
        Yandex,
        LocalAI,
        ChatGPT,
        Gemini,
        Claude,
        DeepSeek,
        OpenRouter,

        /// <summary>PLaMo 3 Translate on a self-hosted Ollama server (docs/PLAN.md P3).</summary>
        Plamo,
    }

    /// <summary>
    /// One configured engine instance in the ordered list. The list order is the
    /// fallback priority; the instance marked as primary is tried first.
    /// </summary>
    [Serializable]
    public class EngineInstance
    {
        public Guid Id { get; set; } = Guid.NewGuid();
        public TranslationEngine Type { get; set; }
        public string Name { get; set; } = "";
        public bool Enabled { get; set; } = true;

        // Per-instance credentials/model (cloud LLM engines).
        public string ApiKey { get; set; } = "";
        public string Model { get; set; } = "";
        public string Endpoint { get; set; } = "";
        public float Temperature { get; set; } = 0.2f;
        public int MaxTokens { get; set; } = 500;

        // PLaMo (Ollama) only.
        /// <summary>How long Ollama keeps the model loaded after a request, e.g. "30m". Empty uses the server default.</summary>
        public string KeepAlive { get; set; } = "30m";

        /// <summary>Per-request timeout in seconds.</summary>
        public int TimeoutSeconds { get; set; } = 8;
    }
}
