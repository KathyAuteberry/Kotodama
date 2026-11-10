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
        OpenRouter
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
    }
}
