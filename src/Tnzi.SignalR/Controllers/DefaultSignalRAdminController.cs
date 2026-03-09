namespace Tnzi.SignalR.Controllers;

/// <summary>
/// SignalR admin controller base class
/// Provides connection statistics, online user management, and connection detail endpoints
/// </summary>
[Route("admin/signalr")]
[DefaultController]
public class DefaultSignalRAdminController : ApiAdminControllerBase
{
    protected readonly IConnectionManager ConnectionManager;

    /// <summary>
    /// Initialize SignalR admin controller
    /// </summary>
    public DefaultSignalRAdminController(IConnectionManager connectionManager)
    {
        ConnectionManager = Check.NotNull(connectionManager);
    }

    /// <summary>
    /// Get SignalR connection statistics
    /// </summary>
    [HttpGet("stats")]
    public virtual async Task<ApiResult<SignalRStatsDto>> GetStats()
    {
        var onlineCount = await ConnectionManager.GetOnlineUserCountAsync();
        var totalConnections = await ConnectionManager.GetTotalConnectionCountAsync();

        var dto = new SignalRStatsDto
        {
            OnlineUserCount = onlineCount,
            TotalConnectionCount = totalConnections,
            Timestamp = DateTime.UtcNow,
        };

        return ApiResult<SignalRStatsDto>.Ok(dto);
    }

    /// <summary>
    /// Get all online users with their connection details
    /// </summary>
    [HttpGet("online-users")]
    public virtual async Task<ApiResult<List<OnlineUserDto>>> GetOnlineUsers()
    {
        var userIds = await ConnectionManager.GetAllOnlineUserIdsAsync();
        var result = new List<OnlineUserDto>();

        foreach (var userId in userIds)
        {
            var connections = await ConnectionManager.GetUserConnectionsAsync(userId);
            var connectionInfos = new List<ConnectionInfoDto>();

            foreach (var connId in connections)
            {
                var info = await BuildConnectionInfoAsync(connId);
                connectionInfos.Add(info);
            }

            result.Add(new OnlineUserDto
            {
                UserId = userId,
                ConnectionCount = connectionInfos.Count,
                Connections = connectionInfos,
            });
        }

        return ApiResult<List<OnlineUserDto>>.Ok(result);
    }

    /// <summary>
    /// Check if a specific user is online
    /// </summary>
    [HttpGet("users/{userId:guid}/online")]
    public virtual async Task<ApiResult<bool>> IsUserOnline(Guid userId)
    {
        var isOnline = await ConnectionManager.IsUserOnlineAsync(userId);
        return ApiResult<bool>.Ok(isOnline);
    }

    /// <summary>
    /// Get connection details for a specific user
    /// </summary>
    [HttpGet("users/{userId:guid}/connections")]
    public virtual async Task<ApiResult<OnlineUserDto>> GetUserConnections(Guid userId)
    {
        var connections = await ConnectionManager.GetUserConnectionsAsync(userId);
        var connectionInfos = new List<ConnectionInfoDto>();

        foreach (var connId in connections)
        {
            var info = await BuildConnectionInfoAsync(connId);
            connectionInfos.Add(info);
        }

        var dto = new OnlineUserDto
        {
            UserId = userId,
            ConnectionCount = connectionInfos.Count,
            Connections = connectionInfos,
        };

        return ApiResult<OnlineUserDto>.Ok(dto);
    }

    /// <summary>
    /// Get connection details by connection ID
    /// </summary>
    [HttpGet("connections/{connectionId}")]
    public virtual async Task<ApiResult<ConnectionInfoDto>> GetConnection(string connectionId)
    {
        var info = await BuildConnectionInfoAsync(connectionId);
        return ApiResult<ConnectionInfoDto>.Ok(info);
    }

    /// <summary>
    /// Force disconnect all connections for a specific user
    /// </summary>
    [HttpDelete("users/{userId:guid}/connections")]
    public virtual async Task<ApiResult> DisconnectUser(Guid userId)
    {
        await ConnectionManager.RemoveUserConnectionsAsync(userId);
        return ApiResult.Ok();
    }

    /// <summary>
    /// Get members of a specific group
    /// </summary>
    [HttpGet("groups/{groupName}/connections")]
    public virtual async Task<ApiResult<List<string>>> GetGroupConnections(string groupName)
    {
        var connections = await ConnectionManager.GetGroupConnectionsAsync(groupName);
        return ApiResult<List<string>>.Ok(connections.ToList());
    }

    /// <summary>
    /// Build connection info DTO from connection ID
    /// </summary>
    private async Task<ConnectionInfoDto> BuildConnectionInfoAsync(string connectionId)
    {
        var metadata = await ConnectionManager.GetConnectionMetadataAsync(connectionId);
        var groups = await ConnectionManager.GetConnectionGroupsAsync(connectionId);

        if (metadata != null)
        {
            return new ConnectionInfoDto
            {
                ConnectionId = connectionId,
                UserId = metadata.UserId,
                UserName = metadata.UserName,
                ConnectedAt = metadata.ConnectedAt,
                IpAddress = metadata.IpAddress,
                UserAgent = metadata.UserAgent,
                HubName = metadata.HubName,
                Groups = groups.ToList(),
            };
        }

        // Fallback: only userId from reverse index
        var userId = await ConnectionManager.GetUserIdByConnectionAsync(connectionId);
        return new ConnectionInfoDto
        {
            ConnectionId = connectionId,
            UserId = userId,
            Groups = groups.ToList(),
        };
    }
}
