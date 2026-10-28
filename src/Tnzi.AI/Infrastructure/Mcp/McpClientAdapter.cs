
namespace Tnzi.AI.Infrastructure.Mcp;

/// <summary>
/// 包装 SDK McpClient，暴露 ListToolsAsync 返回 AITool 列表并实现 IAsyncDisposable。
/// 工具即 SDK 的 McpClientTool（本身就是 AIFunction，绑定在本适配器持有的 McpClient 上）。
/// </summary>
/// <remarks>
/// 按职责拆分为多个 partial 文件：
/// <list type="bullet">
///   <item><c>McpClientAdapter.cs</c> - 字段/构造</item>
///   <item><c>McpClientAdapter.Tools.cs</c> - 工具列举（可选服务器名前缀）</item>
///   <item><c>McpClientAdapter.Resources.cs</c> - 资源/提示（Resource/Prompt）列举与读取</item>
///   <item><c>McpClientAdapter.Lifecycle.cs</c> - 释放（DisposeAsync）</item>
/// </list>
/// </remarks>
internal sealed partial class McpClientAdapter : IMcpClientAdapter
{
    private readonly McpClient _client;
    private readonly string _serverName;
    private readonly ILogger _logger;
    private readonly bool _prefixToolNameWithServer;

    public McpClientAdapter(McpClient client, string serverName, ILogger logger, bool prefixToolNameWithServer)
    {
        _client = Check.NotNull(client);
        _serverName = serverName ?? string.Empty;
        _logger = Check.NotNull(logger);
        _prefixToolNameWithServer = prefixToolNameWithServer;
    }
}
