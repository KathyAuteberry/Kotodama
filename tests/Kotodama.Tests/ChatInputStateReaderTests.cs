using EchoXIV.Interop;
using Xunit;

namespace Kotodama.Tests;

public sealed class ChatInputStateReaderTests
{
    private enum FakeChatType : ushort { Party = 14 }

    /// <summary>Stands in for Dalamud's JObject: a foreign type whose ToString() is its JSON.</summary>
    private sealed class ForeignJson(string json)
    {
        public override string ToString() => json;
    }

    [Fact]
    public void Reads_a_boxed_tuple() =>
        Assert.Equal((true, false), ChatInputStateReader.TryRead((true, true, false, false, 0, FakeChatType.Party)));

    [Fact]
    public void Reads_a_foreign_json_object() =>
        Assert.Equal((true, true), ChatInputStateReader.TryRead(new ForeignJson(
            """{"Item1":true,"Item2":true,"Item3":true,"Item4":true,"Item5":4,"Item6":14}""")));

    [Fact]
    public void Reads_indented_json_as_produced_by_JObject_ToString() =>
        Assert.Equal((true, false), ChatInputStateReader.TryRead(new ForeignJson(
            "{\r\n  \"Item1\": true,\r\n  \"Item2\": true,\r\n  \"Item3\": false,\r\n  \"Item4\": false,\r\n  \"Item5\": 0,\r\n  \"Item6\": 14\r\n}")));

    [Fact]
    public void Reads_an_array_shape() =>
        Assert.Equal((false, false), ChatInputStateReader.TryRead(new ForeignJson("[true,false,false,false,0,14]")));

    [Fact]
    public void Rejects_values_without_four_booleans()
    {
        Assert.Null(ChatInputStateReader.TryRead(null));
        // What re-serializing Dalamud's foreign JObject produced in game.
        Assert.Null(ChatInputStateReader.TryRead(new ForeignJson("""{"Item1":[],"Item2":[],"Item3":[],"Item4":[],"Item5":[],"Item6":[]}""")));
        Assert.Null(ChatInputStateReader.TryRead(new ForeignJson("[true,false,0]")));
        Assert.Null(ChatInputStateReader.TryRead("nope"));
    }
}
