namespace Tnzi.AI.Infrastructure.Mcp;

/// <summary>
/// Wraps MCP tool calls with auth failure recovery: on 401/403, invalidates cached OAuth token
/// and MCP client connection, then retries the call once with a fresh token.
/// Only retries once to prevent infinite loops.
/// </summary>
public sealed class McpAuthRecoveryToolWrapper : DelegatingAIFunction
{
    private readonly string _serverName;
    private readonly McpOAuthClientHandler _oauthHandler;
    private readonly IMcpClientFactory _clientFactory;
    private readonly McpToolProvider _toolProvider;
    private readonly ILogger<McpAuthRecoveryToolWrapper>? _logger;

    public McpAuthRecoveryToolWrapper(
        AIFunction innerFunction,
        string serverName,
        McpOAuthClientHandler oauthHandler,
        IMcpClientFactory clientFactory,
        McpToolProvider toolProvider,
        ILogger<McpAuthRecoveryToolWrapper>? logger = null)
        : base(Check.NotNull(innerFunction))
    {
        _serverName = Check.NotNullOrEmpty(serverName);
        _oauthHandler = Check.NotNull(oauthHandler);
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
        string serverName,
        McpOAuthClientHandler oauthHandler,
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
                result.Add(new McpAuthRecoveryToolWrapper(aiFunction, serverName, oauthHandler, clientFactory, toolProvider, logger));
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
            _oauthHandler.InvalidateToken(_serverName);

            // Invalidate the cached MCP client connection so it reconnects with the new token
            try
            {
                await _clientFactory.InvalidateClientAsync(_serverName, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception invalidateEx)
            {
                _logger?.LogWarning(
                    invalidateEx,
                    "Failed to invalidate MCP client for server '{ServerName}' during auth recovery",
                    _serverName);
            }

            // Invalidate tool cache so tools are re-fetched from the reconnected client
            _toolProvider.InvalidateCache(_serverName);

            // Retry once — do not catch auth failures on retry to avoid infinite loops
            try
            {
                _logger?.LogDebug(
                    "Retrying MCP tool call for server '{ServerName}', tool '{ToolName}' after token refresh",
                    _serverName, InnerFunction.Name);

                return await base.InvokeCoreAsync(arguments ?? new AIFunctionArguments(), cancellationToken).ConfigureAwait(false);
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
    /// Determines whether an exception indicates an authentication/authorization failure (401/403).
    /// Checks HttpRequestException status codes and common error message patterns.
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
                if (message.Contains("401", StringComparison.OrdinalIgnoreCase)
                    || message.Contains("403", StringComparison.OrdinalIgnoreCase)
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
}
