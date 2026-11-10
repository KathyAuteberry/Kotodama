using EchoXIV;
using Xunit;

namespace Kotodama.Tests;

public class EngineLocalityTests
{
    public static TheoryData<TranslationEngine> AllEngines()
    {
        var data = new TheoryData<TranslationEngine>();
        foreach (var engine in Enum.GetValues<TranslationEngine>())
            data.Add(engine);
        return data;
    }

    [Theory]
    [MemberData(nameof(AllEngines))]
    public void Every_engine_type_is_classified(TranslationEngine engine)
    {
        var exception = Record.Exception(() => EngineLocality.IsLocal(engine));
        Assert.Null(exception);
    }

    [Fact]
    public void Local_ai_is_local()
    {
        Assert.True(EngineLocality.IsLocal(TranslationEngine.LocalAI));
    }

    [Theory]
    [InlineData(TranslationEngine.Google)]
    [InlineData(TranslationEngine.Papago)]
    [InlineData(TranslationEngine.Yandex)]
    [InlineData(TranslationEngine.LibreTranslate)]
    [InlineData(TranslationEngine.ChatGPT)]
    [InlineData(TranslationEngine.Gemini)]
    [InlineData(TranslationEngine.Claude)]
    [InlineData(TranslationEngine.DeepSeek)]
    [InlineData(TranslationEngine.OpenRouter)]
    public void Third_party_services_are_cloud(TranslationEngine engine)
    {
        Assert.False(EngineLocality.IsLocal(engine));
    }

    [Theory]
    [InlineData("http://fenchurch.lan:11434")]
    [InlineData("http://10.1.0.103:11434")]
    [InlineData("https://ollama.example.com")]
    [InlineData("")]
    public void Endpoint_never_changes_the_classification(string endpoint)
    {
        Assert.True(EngineLocality.IsLocal(new EngineInstance { Type = TranslationEngine.LocalAI, Endpoint = endpoint }));
        Assert.False(EngineLocality.IsLocal(new EngineInstance { Type = TranslationEngine.Google, Endpoint = endpoint }));
    }

    [Fact]
    public void Unknown_engine_value_is_rejected()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => EngineLocality.IsLocal((TranslationEngine)999));
    }
}
