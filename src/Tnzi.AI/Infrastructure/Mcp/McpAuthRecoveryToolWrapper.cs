namespace Tnzi.AI.Infrastructure.Mcp;

/// <summary>
/// Wraps MCP tool calls with auth failure recovery: on 401/403, invalidates cached OAuth token
/// and MCP client connection, then retries the call once with a fresh token.
/// Only retries once to prevent infinite loops.
/// </summary>
/// <remarks>
/// 两类服务器会用到它：配置了 OAuth client_credentials 的（失效 token 缓存后重连），
/// 以及用户专属服务器（<see cref="UserMcpServerConfig"/>）：失效<b>这个用户的</b>那条连接，并让重连时向消费方的
/// 凭据回调带 <c>Refresh = true</c> 要刷新过的凭据，然后重试一次。
/// </remarks>
/// <remarks>
/// The retry cannot go through the wrapped function: an SDK <see cref="McpClientTool"/> is bound to the
/// client it was listed from, and recovery has just disposed that client. The retry reconnects through
/// <see cref="IMcpClientFactory"/> and calls the same tool (matched by its server-side name) on the new connection.
/// </remarks>
public sealed partial class McpAuthRecoveryToolWrapper : DelegatingAIFunction
{
    private readonly McpServerConfig _server;
    private readonly string _serverName;
    private readonly McpOAuthClientHandler? _oauthHandler;
    private readonly IMcpClientFactory _clientFactory;
    private readonly McpToolProvider _toolProvider;
    private readonly ILogger<McpAuthRecoveryToolWrapper>? _logger;

    public McpAuthRecoveryToolWrapper(
        AIFunction innerFunction,
        McpServerConfig server,
        McpOAuthClientHandler? oauthHandler,
        IMcpClientFactory clientFactory,
        McpToolProvider toolProvider,
        ILogger<McpAuthRecoveryToolWrapper>? logger = null)
        : base(Check.NotNull(innerFunction))
    {
        _server = Check.NotNull(server);
        _serverName = Check.NotNullOrEmpty(server.Name);
        _oauthHandler = oauthHandler;
        _clientFactory = Check.NotNull(clientFactory);
        _toolProvider = Check.NotNull(toolProvider);
        _logger = logger;
    }

    /// <summary>
    /// Wraps AIFunction tools in the list with auth recovery; non-AIFunction tools pass through unchanged.
    /// Only applies to servers with OAuth configured.
    /// </summary>
    public static IList<AITool> Wrap(
        IList<AITool> tools,
        McpServerConfig server,
        McpOAuthClientHandler? oauthHandler,
        IMcpClientFactory clientFactory,
        McpToolProvider toolProvider,
        ILogger<McpAuthRecoveryToolWrapper>? logger = null)
    {
        if (tools == null || tools.Count == 0)
        {
            return tools ?? (IList<AITool>)new List<AITool>();
        }

        var result = new List<AITool>(tools.Count);
        foreach (var tool in tools)
        {
            if (tool is AIFunction aiFunction)
            {
                result.Add(new McpAuthRecoveryToolWrapper(aiFunction, server, oauthHandler, clientFactory, toolProvider, logger));
            }
            else
            {
                result.Add(tool);
            }
        }

        return result;
    }

    /// <inheritdoc />
    protected override async ValueTask<object?> InvokeCoreAsync(
        AIFunctionArguments arguments,
        CancellationToken cancellationToken)
    {
        try
        {
            return await base.InvokeCoreAsync(arguments ?? new AIFunctionArguments(), cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (IsAuthFailure(ex) && !cancellationToken.IsCancellationRequested)
        {
            _logger?.LogWarning(
                ex,
                "MCP tool call auth failure detected for server '{ServerName}', tool '{ToolName}'. Invalidating token and retrying once",
                _serverName, InnerFunction.Name);

            // Invalidate cached OAuth token so a fresh token is obtained on next request
            _oauthHandler?.InvalidateToken(_serverName);

            // Invalidate the cached MCP client connection so it reconnects with the new token.
            // 用户专属服务器只失效这个用户的连接（按名字会让别的用户一起掉线），并要求下一次建连向消费方要刷新过的凭据
            try
            {
                if (_server is UserMcpServerConfig)
                {
                    await _clientFactory.InvalidateClientAsync(_server, refreshCredential: true, cancellationToken).ConfigureAwait(false);
                }
                else
                {
                    await _clientFactory.InvalidateClientAsync(_serverName, cancellationToken).ConfigureAwait(false);
                }
            }
            catch (Exception invalidateEx)
            {
                _logger?.LogWarning(
                    invalidateEx,
                    "Failed to invalidate MCP client for server '{ServerName}' during auth recovery",
                    _serverName);
            }

            // Invalidate tool cache so tools are re-fetched from the reconnected client
            if (_server is UserMcpServerConfig)
            {
                _toolProvider.InvalidateServerCache(_server);
            }
            else
            {
                _toolProvider.InvalidateCache(_serverName);
            }

            // Retry once on the reconnected client - do not catch auth failures on retry to avoid infinite loops
            try
            {
                _logger?.LogDebug(
                    "Retrying MCP tool call for server '{ServerName}', tool '{ToolName}' after token refresh",
                    _serverName, InnerFunction.Name);

                var reconnected = await ResolveOnNewConnectionAsync(cancellationToken).ConfigureAwait(false);
                return await reconnected.InvokeAsync(arguments ?? new AIFunctionArguments(), cancellationToken).ConfigureAwait(false);
            }
            catch (Exception retryEx)
            {
                _logger?.LogError(
                    retryEx,
                    "MCP tool call retry failed for server '{ServerName}', tool '{ToolName}' after auth recovery",
                    _serverName, InnerFunction.Name);

                throw new InvalidOperationException(
                    $"MCP tool '{InnerFunction.Name}' on server '{_serverName}' failed due to authentication error. "
                    + "Token was refreshed but the retry also failed. Please verify the OAuth credentials.",
                    retryEx);
            }
        }
    }

    /// <summary>
    /// Connects again (with the refreshed token) and finds this tool on the new connection by its server-side name.
    /// </summary>
    private async Task<AIFunction> ResolveOnNewConnectionAsync(CancellationToken cancellationToken)
    {
        var originalName = McpToolNames.OriginalName(InnerFunction);
        var adapter = await _clientFactory.GetOrCreateClientAsync(_server, cancellationToken).ConfigureAwait(false);
        var tools = await adapter.ListToolsAsync(cancellationToken).ConfigureAwait(false);

        return tools.OfType<AIFunction>().FirstOrDefault(t => string.Equals(McpToolNames.OriginalName(t), originalName, StringComparison.Ordinal))
            ?? throw new InvalidOperationException(
                $"MCP server '{_serverName}' no longer offers the tool '{originalName}' after reconnecting.");
    }

    /// <summary>
    /// Determines whether an exception indicates an authentication/authorization failure (401/403).
    /// Checks HttpRequestException status codes and common error message patterns
    /// (status codes in a message only count as whole numbers: "4031" is not a 403).
    /// </summary>
    public static bool IsAuthFailure(Exception ex)
    {
        // Check the full exception chain (including InnerException)
        var current = ex;
        while (current != null)
        {
            // HttpRequestException with status code (preferred check)
            if (current is HttpRequestException httpEx && httpEx.StatusCode.HasValue)
            {
                var code = (int)httpEx.StatusCode.Value;
                if (code is 401 or 403)
                {
                    return true;
                }
            }

            // Check exception message for auth-related keywords
            var message = current.Message;
            if (!string.IsNullOrEmpty(message))
            {
                if (AuthStatusCodeRegex().IsMatch(message)
                    || message.Contains("Unauthorized", StringComparison.OrdinalIgnoreCase)
                    || message.Contains("Forbidden", StringComparison.OrdinalIgnoreCase)
                    || message.Contains("authentication failed", StringComparison.OrdinalIgnoreCase)
                    || message.Contains("token expired", StringComparison.OrdinalIgnoreCase)
                    || message.Contains("invalid_token", StringComparison.OrdinalIgnoreCase)
                    || message.Contains("invalid_grant", StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            current = current.InnerException;
        }

        return false;
    }

    [GeneratedRegex(@"(?<!\d)40[13](?!\d)")]
    private static partial Regex AuthStatusCodeRegex();
}
