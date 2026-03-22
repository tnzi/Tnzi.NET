using ModelContextProtocol.Protocol;


namespace Tnzi.AI.Mcp.Server;

/// <summary>
/// 连接 ASP.NET Core MCP handlers 与运行时 IMcpServerHost。
/// </summary>
public sealed class McpServerHttpHandlerBridge
{
    private readonly McpServerServiceProviderAccessor _serviceProviderAccessor;

    public McpServerHttpHandlerBridge(McpServerServiceProviderAccessor serviceProviderAccessor)
    {
        _serviceProviderAccessor = Check.NotNull(serviceProviderAccessor);
    }

    public async ValueTask<ListToolsResult> HandleListToolsAsync(
        RequestContext<ListToolsRequestParams> request,
        CancellationToken cancellationToken)
    {
        _ = request;

        var host = GetHost();
        var tools = await host.ListToolsAsync(cancellationToken);
        return new ListToolsResult
        {
            Tools = tools.ToList()
        };
    }

    public ValueTask<CallToolResult> HandleCallToolAsync(
        RequestContext<CallToolRequestParams> request,
        CancellationToken cancellationToken)
    {
        var host = GetHost();
        var args = request.Params?.Arguments ?? new Dictionary<string, JsonElement>();
        return new ValueTask<CallToolResult>(
            host.CallToolAsync(request.Params?.Name ?? string.Empty, args, cancellationToken));
    }

    private IMcpServerHost GetHost()
    {
        var serviceProvider = _serviceProviderAccessor.ServiceProvider
            ?? throw new InvalidOperationException("MCP HTTP handlers were invoked before IServiceProvider was initialized.");
        return serviceProvider.GetRequiredService<IMcpServerHost>();
    }
}
