using System;

namespace EchoXIV.Services
{
    /// <summary>
    /// Excepción lanzada cuando un servicio de traducción alcanza su límite de peticiones (Rate Limit / 429)
    /// </summary>
    public class TranslationRateLimitException : Exception
    {
        public string EngineName { get; }

        public TranslationRateLimitException(string engineName, string message) : base(message)
        {
            EngineName = engineName;
        }
    }

    /// <summary>
    /// Thrown by <see cref="TranslationRouter"/> when no allowed engine produced a translation.
    /// Callers must treat this as a failure: never show, cache or send the input as if translated.
    /// </summary>
    public class AllEnginesFailedException : Exception
    {
        public AllEnginesFailedException(string message) : base(message)
        {
        }
    }

    /// <summary>
    /// Exception thrown when the Local AI server is unavailable
    /// (no network response, timeout, or HTTP error after retries).
    /// Inherits from TranslationRateLimitException to reuse the failover flow.
    /// </summary>
    public class LocalAiUnavailableException : TranslationRateLimitException
    {
        public LocalAiUnavailableException(string engineName, string message) : base(engineName, message)
        {
        }
    }
}
