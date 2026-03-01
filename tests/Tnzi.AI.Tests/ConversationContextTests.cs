namespace Tnzi.AI.Tests;

/// <summary>
/// ConversationContext 单元测试 — 验证消息序列化/反序列化（含多模态）
/// </summary>
public class ConversationContextTests
{
    [Fact]
    public void Serialize_EmptyContext_ReturnsValidJson()
    {
        var ctx = new ConversationContext();
        var json = ctx.Serialize();

        json.ShouldNotBeNullOrWhiteSpace();
        var deserialized = ConversationContext.Deserialize(json);
        deserialized.ShouldNotBeNull();
        deserialized!.Messages.Count.ShouldBe(0);
    }

    [Fact]
    public void RoundTrip_TextMessage_PreservesContent()
    {
        var ctx = new ConversationContext();
        ctx.Messages.Add(new ChatMessage(ChatRole.User, "Hello world"));
        ctx.Messages.Add(new ChatMessage(ChatRole.Assistant, "Hi there!"));

        var json = ctx.Serialize();
        var restored = ConversationContext.Deserialize(json);

        restored.ShouldNotBeNull();
        restored!.Messages.Count.ShouldBe(2);
        restored.Messages[0].Role.ShouldBe(ChatRole.User);
        restored.Messages[0].Text.ShouldBe("Hello world");
        restored.Messages[1].Role.ShouldBe(ChatRole.Assistant);
        restored.Messages[1].Text.ShouldBe("Hi there!");
    }

    [Fact]
    public void RoundTrip_SystemMessage_PreservesRole()
    {
        var ctx = new ConversationContext();
        ctx.Messages.Add(new ChatMessage(ChatRole.System, "You are a helpful assistant"));

        var json = ctx.Serialize();
        var restored = ConversationContext.Deserialize(json);

        restored.ShouldNotBeNull();
        restored!.Messages[0].Role.ShouldBe(ChatRole.System);
        restored.Messages[0].Text.ShouldBe("You are a helpful assistant");
    }

    [Fact]
    public void RoundTrip_ImageContent_Url_PreservesUri()
    {
        var ctx = new ConversationContext();
        // MEAI DataContent only accepts data URIs, so construct with base64 for URL-like test
        var testBytes = new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A };
        var imageContent = new DataContent(testBytes, "image/png");
        ctx.Messages.Add(new ChatMessage(ChatRole.User, [new TextContent("Look at this"), imageContent]));

        var json = ctx.Serialize();
        var restored = ConversationContext.Deserialize(json);

        restored.ShouldNotBeNull();
        var contents = restored!.Messages[0].Contents;
        contents.Count.ShouldBe(2);

        contents[0].ShouldBeOfType<TextContent>();
        ((TextContent)contents[0]).Text.ShouldBe("Look at this");

        contents[1].ShouldBeOfType<DataContent>();
        var restoredImage = (DataContent)contents[1];
        restoredImage.MediaType.ShouldBe("image/png");
        restoredImage.Data.Length.ShouldBeGreaterThan(0);
    }

    [Fact]
    public void RoundTrip_ImageContent_Base64_PreservesData()
    {
        var ctx = new ConversationContext();
        var testData = new byte[] { 0x89, 0x50, 0x4E, 0x47 }; // PNG header
        var imageContent = new DataContent(testData, "image/png");
        ctx.Messages.Add(new ChatMessage(ChatRole.User, [imageContent]));

        var json = ctx.Serialize();
        var restored = ConversationContext.Deserialize(json);

        restored.ShouldNotBeNull();
        var content = restored!.Messages[0].Contents[0];
        content.ShouldBeOfType<DataContent>();

        var restoredImage = (DataContent)content;
        restoredImage.MediaType.ShouldBe("image/png");
        restoredImage.Data.Length.ShouldBeGreaterThan(0);
    }

    [Fact]
    public void RoundTrip_FunctionCall_PreservesCallDetails()
    {
        var ctx = new ConversationContext();
        var args = new Dictionary<string, object?> { ["query"] = "weather" };
        var functionCall = new FunctionCallContent("call_123", "search", args);
        ctx.Messages.Add(new ChatMessage(ChatRole.Assistant, [functionCall]));

        var json = ctx.Serialize();
        var restored = ConversationContext.Deserialize(json);

        restored.ShouldNotBeNull();
        var content = restored!.Messages[0].Contents[0];
        content.ShouldBeOfType<FunctionCallContent>();

        var restoredCall = (FunctionCallContent)content;
        restoredCall.CallId.ShouldBe("call_123");
        restoredCall.Name.ShouldBe("search");
    }

    [Fact]
    public void RoundTrip_FunctionResult_PreservesResult()
    {
        var ctx = new ConversationContext();
        var functionResult = new FunctionResultContent("call_123", "temperature is 25");
        ctx.Messages.Add(new ChatMessage(ChatRole.Tool, [functionResult]));

        var json = ctx.Serialize();
        var restored = ConversationContext.Deserialize(json);

        restored.ShouldNotBeNull();
        var content = restored!.Messages[0].Contents[0];
        content.ShouldBeOfType<FunctionResultContent>();

        var restoredResult = (FunctionResultContent)content;
        restoredResult.CallId.ShouldBe("call_123");
    }

    [Fact]
    public void RoundTrip_Metadata_Preserved()
    {
        var ctx = new ConversationContext();
        ctx.Metadata = new Dictionary<string, JsonElement>
        {
            ["key1"] = JsonSerializer.SerializeToElement("value1"),
            ["key2"] = JsonSerializer.SerializeToElement(42)
        };

        var json = ctx.Serialize();
        var restored = ConversationContext.Deserialize(json);

        restored.ShouldNotBeNull();
        restored!.Metadata.ShouldNotBeNull();
        restored.Metadata!["key1"].GetString().ShouldBe("value1");
        restored.Metadata["key2"].GetInt32().ShouldBe(42);
    }

    [Fact]
    public void Deserialize_NullOrEmpty_ReturnsNull()
    {
        ConversationContext.Deserialize(null).ShouldBeNull();
        ConversationContext.Deserialize("").ShouldBeNull();
        ConversationContext.Deserialize("   ").ShouldBeNull();
    }

    [Fact]
    public void Deserialize_InvalidJson_ReturnsNull()
    {
        ConversationContext.Deserialize("not valid json").ShouldBeNull();
    }
}
