using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Tnzi.AI.Cli.Controllers;

namespace Tnzi.AI.Cli.Tests.Controllers;

/// <summary>
/// The wire shape of the CLI run event stream. Its event type went out as a number
/// (<c>"type":0</c>) while every other JSON surface of the framework, the run's message history
/// included, writes enum names, and the published client and timeline switch on <c>'Text'</c> /
/// <c>'ToolUse'</c>: a consumer following the stream saw no text and an empty timeline.
/// </summary>
public class CliRunStreamWireFormatTests
{
    private static readonly Guid RunId = Guid.NewGuid();

    [Fact]
    public async Task UserStream_WritesTheEventTypeByName()
    {
        var controller = new DefaultCliRunController(DispatcherWithEvents());
        var body = Attach(controller);

        await controller.Stream(RunId);

        AssertNamedTypes(Read(body));
    }

    [Fact]
    public async Task AdminStream_WritesTheEventTypeByName()
    {
        var controller = new DefaultCliRunAdminController(DispatcherWithEvents());
        var body = Attach(controller);

        await controller.Stream(RunId);

        AssertNamedTypes(Read(body));
    }

    [Fact]
    public void EventType_SerializesByNameWhateverTheOptions()
    {
        var json = JsonSerializer.Serialize(new CliAgentEvent { Type = CliAgentEventType.ToolResult, Output = "ok" });

        json.ShouldContain("\"Type\":\"ToolResult\"", Case.Sensitive);
        JsonSerializer.Deserialize<CliAgentEvent>("""{"Type":3}""")!.Type.ShouldBe(CliAgentEventType.ToolResult, "a stored numeric value still reads");
    }

    private static void AssertNamedTypes(string body)
    {
        body.ShouldContain("\"type\":\"Text\"", Case.Sensitive);
        body.ShouldContain("\"type\":\"ToolUse\"", Case.Sensitive);
        body.ShouldNotContain("\"type\":0");
        body.ShouldNotContain("\"type\":2");
    }

    private static ICliAgentDispatcher DispatcherWithEvents()
    {
        var dispatcher = new Mock<ICliAgentDispatcher>();
        dispatcher.Setup(d => d.StreamAsync(RunId, 0, It.IsAny<CancellationToken>())).Returns(Events());
        return dispatcher.Object;
    }

    private static async IAsyncEnumerable<CliAgentEvent> Events()
    {
        yield return new CliAgentEvent { Type = CliAgentEventType.Text, Content = "Hel" };
        await Task.Yield();
        yield return new CliAgentEvent { Type = CliAgentEventType.ToolUse, Tool = "Read", CallId = "c1" };
    }

    private static MemoryStream Attach(ControllerBase controller)
    {
        var httpContext = new DefaultHttpContext();
        var body = new MemoryStream();
        httpContext.Response.Body = body;
        controller.ControllerContext = new ControllerContext { HttpContext = httpContext };
        return body;
    }

    private static string Read(MemoryStream body)
    {
        body.Position = 0;
        return new StreamReader(body).ReadToEnd();
    }
}
