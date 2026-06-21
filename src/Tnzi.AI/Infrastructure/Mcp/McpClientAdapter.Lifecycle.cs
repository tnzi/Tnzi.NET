
namespace Tnzi.AI.Infrastructure.Mcp;

/// <summary>
/// <see cref="McpClientAdapter"/> 的生命周期 partial — 释放底层 SDK McpClient。
/// </summary>
internal sealed partial class McpClientAdapter
{
    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        await _client.DisposeAsync().ConfigureAwait(false);
    }
}
