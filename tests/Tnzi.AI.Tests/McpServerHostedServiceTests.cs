using Tnzi.AI.Mcp.Server;
using Tnzi.AI.Mcp.Options;
using MsOptions = Microsoft.Extensions.Options.Options;

namespace Tnzi.AI.Tests;

public class McpServerHostedServiceTests
{
    [Fact]
    public async Task StartAsync_WhenEnabled_DelegatesToServerHost()
    {
        using var cts = new CancellationTokenSource();
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        var host = new Mock<IMcpServerHost>();
        host.Setup(x => x.StartAsync(It.IsAny<CancellationToken>()))
            .Returns<CancellationToken>(async token =>
            {
                started.TrySetResult();
                await Task.Delay(Timeout.Infinite, token);
            });

        var service = new McpServerHostedService(
            host.Object,
            MsOptions.Create(new McpServerOptions { Enabled = true }),
            Mock.Of<ILogger<McpServerHostedService>>());

        await service.StartAsync(cts.Token);
        await started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        cts.Cancel();
        await service.StopAsync(CancellationToken.None);

        host.Verify(x => x.StartAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task StartAsync_WhenDisabled_DoesNotDelegateToServerHost()
    {
        var host = new Mock<IMcpServerHost>();
        var service = new McpServerHostedService(
            host.Object,
            MsOptions.Create(new McpServerOptions { Enabled = false }),
            Mock.Of<ILogger<McpServerHostedService>>());

        await service.StartAsync(CancellationToken.None);
        await service.StopAsync(CancellationToken.None);

        host.Verify(x => x.StartAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task StartAsync_WhenTransportIsSse_DoesNotStartHostedStdioServer()
    {
        var host = new Mock<IMcpServerHost>();
        var service = new McpServerHostedService(
            host.Object,
            MsOptions.Create(new McpServerOptions
            {
                Enabled = true,
                Transport = "sse"
            }),
            Mock.Of<ILogger<McpServerHostedService>>());

        await service.StartAsync(CancellationToken.None);
        await service.StopAsync(CancellationToken.None);

        host.Verify(x => x.StartAsync(It.IsAny<CancellationToken>()), Times.Never);
    }
}
