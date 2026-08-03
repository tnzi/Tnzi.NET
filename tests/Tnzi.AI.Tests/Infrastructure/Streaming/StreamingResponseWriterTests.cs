using Microsoft.AspNetCore.Http;

namespace Tnzi.AI.Tests.Infrastructure.Streaming;

/// <summary>
/// F6: StreamingResponseWriter serializer is injectable - DefaultJsonOptions is
/// public and WriteEventAsync accepts an optional JsonSerializerOptions override.
/// </summary>
public class StreamingResponseWriterTests
{
    private static (DefaultHttpContext ctx, MemoryStream body) NewResponse()
    {
        var ctx = new DefaultHttpContext();
        var body = new MemoryStream();
        ctx.Response.Body = body;
        return (ctx, body);
    }

    private static string ReadBody(MemoryStream body)
    {
        body.Position = 0;
        return new StreamReader(body).ReadToEnd();
    }

    [Fact]
    public void DefaultJsonOptions_IsCamelCase()
    {
        Assert.Same(JsonNamingPolicy.CamelCase, StreamingResponseWriter.DefaultJsonOptions.PropertyNamingPolicy);
    }

    [Fact]
    public async Task WriteEvent_DefaultsToCamelCase()
    {
        var (ctx, body) = NewResponse();

        await StreamingResponseWriter.WriteEventAsync(ctx.Response, new { FooBar = 1 }, StreamingFormat.NDJSON);

        Assert.Contains("\"fooBar\"", ReadBody(body));
    }

    [Fact]
    public async Task WriteEvent_RespectsCustomJsonOptions()
    {
        var (ctx, body) = NewResponse();
        var pascal = new JsonSerializerOptions { PropertyNamingPolicy = null };

        await StreamingResponseWriter.WriteEventAsync(ctx.Response, new { FooBar = 1 }, StreamingFormat.NDJSON, jsonOptions: pascal);

        Assert.Contains("\"FooBar\"", ReadBody(body));
    }
}
