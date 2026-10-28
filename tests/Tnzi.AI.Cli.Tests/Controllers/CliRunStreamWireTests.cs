using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Tnzi.AI.Cli.Controllers;

namespace Tnzi.AI.Cli.Tests.Controllers;

/// <summary>
/// 实时事件流的 wire 形状：<c>type</c> 必须是成员名，与 REST 回放（<c>/messages</c>）一致。
/// </summary>
/// <remarks>
/// 前端 <c>streamCliRun</c> 按 <c>event.type === 'Text'</c> 累积文本、按 <c>'Error'</c> 认错误。
/// 流式端点绕过 MVC 的全局枚举转换器自己序列化，曾经写出 <c>"type":0</c>：
/// 文本与思考从不累积、错误从不浮现，而服务端与前端都没有任何报错。
/// </remarks>
public class CliRunStreamWireTests
{
    private static readonly Guid RunId = Guid.NewGuid();

    private static async IAsyncEnumerable<CliAgentEvent> Events()
    {
        yield return new CliAgentEvent { Type = CliAgentEventType.Text, Content = "hello" };
        yield return new CliAgentEvent { Type = CliAgentEventType.Error, Content = "boom" };
        await Task.CompletedTask;
    }

    private static Mock<ICliAgentDispatcher> Dispatcher()
    {
        var dispatcher = new Mock<ICliAgentDispatcher>();
        dispatcher
            .Setup(d => d.StreamAsync(RunId, 0, It.IsAny<CancellationToken>()))
            .Returns(Events());
        return dispatcher;
    }

    private static (ControllerContext context, MemoryStream body) NewContext()
    {
        var http = new DefaultHttpContext();
        var body = new MemoryStream();
        http.Response.Body = body;
        http.Request.Headers.Accept = "text/event-stream";
        return (new ControllerContext { HttpContext = http }, body);
    }

    private static string Read(MemoryStream body)
    {
        body.Position = 0;
        return new StreamReader(body).ReadToEnd();
    }

    [Fact]
    public async Task UserStream_WritesEventTypesByMemberName()
    {
        var (context, body) = NewContext();
        var controller = new DefaultCliRunController(Dispatcher().Object) { ControllerContext = context };

        await controller.Stream(RunId);

        var written = Read(body);
        written.ShouldContain("\"type\":\"Text\"");
        written.ShouldContain("\"type\":\"Error\"");
        written.ShouldNotContain("\"type\":0");
    }

    [Fact]
    public async Task AdminStream_WritesEventTypesByMemberName()
    {
        var (context, body) = NewContext();
        var controller = new DefaultCliRunAdminController(Dispatcher().Object) { ControllerContext = context };

        await controller.Stream(RunId);

        var written = Read(body);
        written.ShouldContain("\"type\":\"Text\"");
        written.ShouldContain("\"type\":\"Error\"");
        written.ShouldNotContain("\"type\":5");
    }
}
