using Tnzi.AI.Channels.Gateway;
using Tnzi.AI.Channels.Gateway.Models;

namespace Tnzi.AI.Tests.Channels.Gateway;

public class GatewayWebSocketTests
{
    [Fact]
    public void ParseMessage_ValidRequest_Parses()
    {
        // Arrange
        var json = """
        {
            "type": "request",
            "id": "req-1",
            "method": "chat.send",
            "payload": { "text": "Hello", "channel": "web" }
        }
        """;

        // Act
        var msg = JsonSerializer.Deserialize<GatewayMessage>(json);

        // Assert
        msg.ShouldNotBeNull();
        msg.Type.ShouldBe("request");
        msg.Id.ShouldBe("req-1");
        msg.Method.ShouldBe("chat.send");
        msg.Payload.ShouldNotBeNull();
        msg.Payload.Value.TryGetProperty("text", out var textElem).ShouldBeTrue();
        textElem.GetString().ShouldBe("Hello");
    }

    [Fact]
    public void ParseMessage_MissingFields_DefaultsApplied()
    {
        // Arrange
        var json = """{ "type": "request" }""";

        // Act
        var msg = JsonSerializer.Deserialize<GatewayMessage>(json);

        // Assert
        msg.ShouldNotBeNull();
        msg.Type.ShouldBe("request");
        msg.Id.ShouldBeNull();
        msg.Method.ShouldBeNull();
        msg.Payload.ShouldBeNull();
        msg.Error.ShouldBeNull();
    }

    [Fact]
    public void BuildResponse_Success()
    {
        // Arrange
        var payload = JsonSerializer.SerializeToElement(new { data = "ok" });

        // Act
        var response = GatewayWebSocketHandler.BuildResponseMessage("req-1", "session.list", payload);

        // Assert
        response.Type.ShouldBe("response");
        response.Id.ShouldBe("req-1");
        response.Method.ShouldBe("session.list");
        response.Payload.ShouldNotBeNull();
        response.Error.ShouldBeNull();
    }

    [Fact]
    public void BuildResponse_Error()
    {
        // Act
        var response = GatewayWebSocketHandler.BuildResponseMessage("req-2", "chat.send", error: "Something went wrong");

        // Assert
        response.Type.ShouldBe("response");
        response.Id.ShouldBe("req-2");
        response.Method.ShouldBe("chat.send");
        response.Payload.ShouldBeNull();
        response.Error.ShouldBe("Something went wrong");
    }

    [Theory]
    [InlineData("chat.send")]
    [InlineData("session.list")]
    [InlineData("session.get")]
    [InlineData("session.prune")]
    public void IsValidMethod_KnownMethods_ReturnsTrue(string method)
    {
        GatewayWebSocketHandler.IsValidMethod(method).ShouldBeTrue();
    }

    [Theory]
    [InlineData("unknown.method")]
    [InlineData("chat.delete")]
    [InlineData("device.register")]
    [InlineData("")]
    [InlineData("foo")]
    public void IsValidMethod_UnknownMethod_ReturnsFalse(string method)
    {
        GatewayWebSocketHandler.IsValidMethod(method).ShouldBeFalse();
    }
}
