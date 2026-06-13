using System.Reflection;
using Tnzi.AI.Mcp.Server;

namespace Tnzi.AI.Tests.Mcp;

/// <summary>
/// McpServerHost.ExtractMessage（私有静态）多形态参数解析测试。
/// 该方法决定 MCP tools/call 的 arguments 如何折叠为 Agent 的 UserMessage。
/// </summary>
public class McpServerHostExtractMessageTests
{
    [Fact]
    public void NullArguments_ReturnsEmpty()
    {
        InvokeExtractMessage(null).ShouldBe(string.Empty);
    }

    [Fact]
    public void EmptyArguments_ReturnsEmpty()
    {
        InvokeExtractMessage(new Dictionary<string, JsonElement>()).ShouldBe(string.Empty);
    }

    [Fact]
    public void MessageKey_StringValue_Returned()
    {
        var args = Args(("message", "hello"), ("other", "ignored"));

        InvokeExtractMessage(args).ShouldBe("hello");
    }

    [Fact]
    public void InputKey_StringValue_ReturnedWhenNoMessage()
    {
        var args = Args(("input", "world"), ("other", "ignored"));

        InvokeExtractMessage(args).ShouldBe("world");
    }

    [Fact]
    public void MessageKey_PreferredOverInputKey()
    {
        var args = Args(("input", "second"), ("message", "first"));

        InvokeExtractMessage(args).ShouldBe("first");
    }

    [Fact]
    public void SingleNonStandardKey_StringValue_Returned()
    {
        var args = Args(("prompt", "single value"));

        InvokeExtractMessage(args).ShouldBe("single value");
    }

    [Fact]
    public void SingleNonStringValue_ReturnsRawText()
    {
        var args = new Dictionary<string, JsonElement>
        {
            ["count"] = JsonSerializer.SerializeToElement(42)
        };

        InvokeExtractMessage(args).ShouldBe("42");
    }

    [Fact]
    public void SingleMessageKey_NonStringValue_ReturnsRawText()
    {
        // message 非字符串时跳过 message 分支，落入「唯一参数取原文」分支
        var args = new Dictionary<string, JsonElement>
        {
            ["message"] = JsonSerializer.SerializeToElement(5)
        };

        InvokeExtractMessage(args).ShouldBe("5");
    }

    [Fact]
    public void SingleObjectValue_ReturnsRawJson()
    {
        var args = new Dictionary<string, JsonElement>
        {
            ["payload"] = JsonSerializer.SerializeToElement(new { city = "Auckland" })
        };

        var result = InvokeExtractMessage(args);

        using var doc = JsonDocument.Parse(result);
        doc.RootElement.GetProperty("city").GetString().ShouldBe("Auckland");
    }

    [Fact]
    public void MultipleKeys_NoMessageOrInput_SerializesAllArguments()
    {
        var args = Args(("a", "1"), ("b", "2"));

        var result = InvokeExtractMessage(args);

        using var doc = JsonDocument.Parse(result);
        doc.RootElement.GetProperty("a").GetString().ShouldBe("1");
        doc.RootElement.GetProperty("b").GetString().ShouldBe("2");
    }

    [Fact]
    public void MultipleKeys_MessageNonString_SerializesAllArguments()
    {
        var args = new Dictionary<string, JsonElement>
        {
            ["message"] = JsonSerializer.SerializeToElement(123),
            ["extra"] = JsonSerializer.SerializeToElement("x")
        };

        var result = InvokeExtractMessage(args);

        using var doc = JsonDocument.Parse(result);
        doc.RootElement.GetProperty("message").GetInt32().ShouldBe(123);
        doc.RootElement.GetProperty("extra").GetString().ShouldBe("x");
    }

    // ─── Helpers ─────────────────────────────────────────────────────────────

    private static Dictionary<string, JsonElement> Args(params (string Key, string Value)[] pairs)
        => pairs.ToDictionary(
            p => p.Key,
            p => JsonSerializer.SerializeToElement(p.Value));

    private static string InvokeExtractMessage(IDictionary<string, JsonElement>? arguments)
    {
        var method = typeof(McpServerHost).GetMethod(
            "ExtractMessage",
            BindingFlags.NonPublic | BindingFlags.Static);

        method.ShouldNotBeNull("McpServerHost.ExtractMessage was renamed or removed — update these tests.");
        return (string)method!.Invoke(null, [arguments])!;
    }
}
