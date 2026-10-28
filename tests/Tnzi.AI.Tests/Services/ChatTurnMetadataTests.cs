namespace Tnzi.AI.Tests.Services;

/// <summary>
/// A chat request's per-turn metadata: a small set of consumer-defined values, bounded in key count and size.
/// </summary>
public class ChatTurnMetadataTests
{
    private static readonly ChatMetadataOptions Limits = new() { MaxKeys = 3, MaxBytes = 64 };

    private static Dictionary<string, JsonElement> Of(params (string Key, string Json)[] entries)
        => entries.ToDictionary(e => e.Key, e => JsonDocument.Parse(e.Json).RootElement.Clone());

    [Fact]
    public void NoMetadata_IsAnEmptySet()
    {
        ChatTurnMetadata.Validate(null, Limits).Data.ShouldBeEmpty();
        ChatTurnMetadata.Validate(new Dictionary<string, JsonElement>(), Limits).Data.ShouldBeEmpty();
    }

    [Fact]
    public void AnyJsonValue_IsAccepted_AndCopied()
    {
        var read = ChatTurnMetadata.Validate(Of(("targets", """["a","b"]"""), ("count", "2"), ("scope.v1", """{"x":true}""")), Limits);

        read.Succeeded.ShouldBeTrue(read.Message);
        read.Data!["targets"].GetArrayLength().ShouldBe(2);
        read.Data["scope.v1"].GetProperty("x").GetBoolean().ShouldBeTrue();
    }

    [Fact]
    public void MoreKeysThanAllowed_IsRefused()
    {
        var read = ChatTurnMetadata.Validate(Of(("a", "1"), ("b", "1"), ("c", "1"), ("d", "1")), Limits);

        read.Code.ShouldBe(400);
        read.ErrorCode.ShouldBe(ErrorCodes.InvalidTurnMetadata);
    }

    [Theory]
    [InlineData("")]
    [InlineData("1abc")]
    [InlineData("has space")]
    [InlineData("<script>")]
    public void AMalformedKey_IsRefused(string key)
    {
        var read = ChatTurnMetadata.Validate(new Dictionary<string, JsonElement> { [key] = JsonDocument.Parse("1").RootElement.Clone() }, Limits);

        read.ErrorCode.ShouldBe(ErrorCodes.InvalidTurnMetadata);
    }

    [Fact]
    public void ALongKey_IsRefused()
    {
        var read = ChatTurnMetadata.Validate(new Dictionary<string, JsonElement>
        {
            ["k" + new string('x', 64)] = JsonDocument.Parse("1").RootElement.Clone()
        }, new ChatMetadataOptions());

        read.ErrorCode.ShouldBe(ErrorCodes.InvalidTurnMetadata);
    }

    [Fact]
    public void MoreBytesThanAllowed_IsRefused_CountingTheSerializedForm()
    {
        var read = ChatTurnMetadata.Validate(Of(("note", $"\"{new string('x', 80)}\"")), Limits);

        read.ErrorCode.ShouldBe(ErrorCodes.InvalidTurnMetadata);
        read.Message.ShouldNotBeNull().ShouldContain("64");
    }

    [Fact]
    public void Stored_AndReadBack_IsTheSameSet()
    {
        var json = ChatTurnMetadata.Serialize(Of(("targets", """["pc-1"]""")));

        var back = ChatTurnMetadata.Parse(json).ShouldNotBeNull();
        back["targets"][0].GetString().ShouldBe("pc-1");
        ChatTurnMetadata.Serialize(new Dictionary<string, JsonElement>()).ShouldBeNull("an empty set is not stored");
        ChatTurnMetadata.Parse("not json").ShouldBeNull("unreadable metadata must not make the message unreadable");
    }
}
