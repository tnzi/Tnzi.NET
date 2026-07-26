using System.Text.Json;
using Tnzi.AI.Channels.Gateway.Models;

namespace Tnzi.AI.Tests.Channels.Gateway;

public class GatewayMessageSerializationTests
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
    };

    [Fact]
    public void GatewayMessage_Request_RoundTrip()
    {
        // Arrange
        var payload = JsonSerializer.SerializeToElement(new { text = "hello" });
        var message = new GatewayMessage
        {
            Type = "request",
            Id = "req-001",
            Method = "chat.send",
            Payload = payload
        };

        // Act
        var json = JsonSerializer.Serialize(message, JsonOptions);
        var deserialized = JsonSerializer.Deserialize<GatewayMessage>(json, JsonOptions);

        // Assert
        Assert.NotNull(deserialized);
        Assert.Equal("request", deserialized.Type);
        Assert.Equal("req-001", deserialized.Id);
        Assert.Equal("chat.send", deserialized.Method);
        Assert.NotNull(deserialized.Payload);
        Assert.Equal("hello", deserialized.Payload.Value.GetProperty("text").GetString());
        Assert.Null(deserialized.Error);
    }

    [Fact]
    public void GatewayMessage_Response_WithError()
    {
        // Arrange
        var message = new GatewayMessage
        {
            Type = "response",
            Id = "req-001",
            Error = "Agent not found"
        };

        // Act
        var json = JsonSerializer.Serialize(message, JsonOptions);
        var deserialized = JsonSerializer.Deserialize<GatewayMessage>(json, JsonOptions);

        // Assert
        Assert.NotNull(deserialized);
        Assert.Equal("response", deserialized.Type);
        Assert.Equal("req-001", deserialized.Id);
        Assert.Equal("Agent not found", deserialized.Error);
        Assert.Null(deserialized.Method);
        Assert.Null(deserialized.Payload);
    }

    [Fact]
    public void GatewayMessage_Event_NoId()
    {
        // Arrange
        var message = new GatewayMessage
        {
            Type = "event",
            Method = "presence.changed",
            Payload = JsonSerializer.SerializeToElement(new { userId = "u1", isOnline = true })
        };

        // Act
        var json = JsonSerializer.Serialize(message, JsonOptions);

        // Assert - event 类型不需要 id，序列化后 id 字段应被省略
        Assert.DoesNotContain("\"id\"", json);

        var deserialized = JsonSerializer.Deserialize<GatewayMessage>(json, JsonOptions);
        Assert.NotNull(deserialized);
        Assert.Equal("event", deserialized.Type);
        Assert.Null(deserialized.Id);
        Assert.Equal("presence.changed", deserialized.Method);
    }

    [Fact]
    public void SessionScope_HasExpectedValues()
    {
        // Assert - 验证枚举包含所有预期值
        var values = Enum.GetValues<SessionScope>();
        Assert.Equal(4, values.Length);
        Assert.Contains(SessionScope.Global, values);
        Assert.Contains(SessionScope.PerPeer, values);
        Assert.Contains(SessionScope.PerChannelPeer, values);
        Assert.Contains(SessionScope.PerThread, values);
    }

    [Fact]
    public void GatewayRequest_JsonSerialization()
    {
        // Arrange
        var threadId = Guid.NewGuid();
        var request = new GatewayRequest
        {
            Channel = "telegram",
            ChatId = "chat-123",
            UserId = "user-456",
            TopicId = "topic-1",
            PeerKind = "user",
            UserMessage = "Hello, agent!",
            AgentId = Guid.Parse("11111111-1111-1111-1111-111111111111"),
            ThreadId = threadId,
            Metadata = new Dictionary<string, object> { ["lang"] = "zh" }
        };

        // Act
        var json = JsonSerializer.Serialize(request, JsonOptions);
        var deserialized = JsonSerializer.Deserialize<GatewayRequest>(json, JsonOptions);

        // Assert
        Assert.NotNull(deserialized);
        Assert.Equal("telegram", deserialized.Channel);
        Assert.Equal("chat-123", deserialized.ChatId);
        Assert.Equal("user-456", deserialized.UserId);
        Assert.Equal("topic-1", deserialized.TopicId);
        Assert.Equal("user", deserialized.PeerKind);
        Assert.Equal("Hello, agent!", deserialized.UserMessage);
        Assert.Equal(Guid.Parse("11111111-1111-1111-1111-111111111111"), deserialized.AgentId);
        Assert.Equal(threadId, deserialized.ThreadId);
        Assert.NotNull(deserialized.Metadata);
    }

    [Fact]
    public void GatewayStreamChunk_Properties()
    {
        // Arrange
        var threadId = Guid.NewGuid();
        var chunk = new GatewayStreamChunk
        {
            TextDelta = "Hello",
            IsFinal = false,
            ThreadId = threadId
        };

        var finalChunk = new GatewayStreamChunk
        {
            IsFinal = true,
            ThreadId = threadId,
            FinishReason = "stop"
        };

        // Assert - 普通块
        Assert.Equal("Hello", chunk.TextDelta);
        Assert.False(chunk.IsFinal);
        Assert.Equal(threadId, chunk.ThreadId);
        Assert.Null(chunk.FinishReason);

        // Assert - 最终块
        Assert.Null(finalChunk.TextDelta);
        Assert.True(finalChunk.IsFinal);
        Assert.Equal("stop", finalChunk.FinishReason);

        // Verify JSON roundtrip
        var json = JsonSerializer.Serialize(finalChunk, JsonOptions);
        Assert.Contains("\"isFinal\":true", json);
        Assert.Contains("\"finishReason\":\"stop\"", json);
        Assert.DoesNotContain("\"textDelta\"", json);
    }
}
