using Microsoft.Extensions.Hosting;

namespace Tnzi.AI.Infrastructure.Mcp.Server;

/// <summary>
/// MCP Server 后台宿主服务。
/// </summary>
public class McpServerHostedService : BackgroundService
{
    private readonly IMcpServerHost _mcpServerHost;
    private readonly IOptions<Options.McpServerOptions> _options;
    private readonly ILogger<McpServerHostedService> _logger;

    public McpServerHostedService(
        IMcpServerHost mcpServerHost,
        IOptions<Options.McpServerOptions> options,
        ILogger<McpServerHostedService> logger)
    {
        _mcpServerHost = Check.NotNull(mcpServerHost);
        _options = Check.NotNull(options);
        _logger = Check.NotNull(logger);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_options.Value.Enabled)
        {
            _logger.LogDebug("MCP Server hosted service is disabled.");
            return;
        }

        if (!string.Equals(_options.Value.Transport, "stdio", StringComparison.OrdinalIgnoreCase))
        {
            _logger.LogDebug(
                "MCP Server hosted service skips non-stdio transport '{Transport}'. HTTP/SSE transport is started by ASP.NET Core endpoint mapping.",
                _options.Value.Transport);
            return;
        }

        await _mcpServerHost.StartAsync(stoppingToken);
    }
}
