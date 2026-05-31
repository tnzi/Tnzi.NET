using System.Security.Cryptography;
using Tnzi.AI.Device.Events;
using Tnzi.AI.Device.Options;
using Tnzi.EventBus;
using Tnzi.Exceptions;

namespace Tnzi.AI.Device.Services;

/// <summary>
/// In-memory device pairing service with code generation and approval tracking
/// </summary>
public class DevicePairingService : IDevicePairingService
{
    /// <summary>
    /// Pairing code alphabet — excludes I, O, S, Z, 0, 1 to avoid visual confusion
    /// </summary>
    private const string PairingAlphabet = "ABCDEFGHJKLMNPQRTUVWXY23456789";

    // Keyed by secret Code (fast approval by code from device); Id→Code mapping for admin approve/reject
    private readonly ConcurrentDictionary<string, PairingRequest> _pendingRequests = new(StringComparer.OrdinalIgnoreCase);
    // Lookup from public Id → secret Code, so admins can approve/reject by Id without the Code appearing in URLs
    private readonly ConcurrentDictionary<string, string> _idToCode = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, DevicePairingInfo> _approvedDevices = new(StringComparer.OrdinalIgnoreCase);
    private readonly DeviceOptions _options;
    private readonly ILogger<DevicePairingService> _logger;
    private readonly IEventBus? _eventBus;

    public DevicePairingService(IOptions<DeviceOptions> options, ILogger<DevicePairingService> logger, IEventBus? eventBus = null)
    {
        _options = Check.NotNull(options).Value;
        _logger = Check.NotNull(logger);
        _eventBus = eventBus;
    }

    public Task<PairingRequest> CreatePairingRequestAsync(string nodeId, string deviceName, DevicePlatform platform, CancellationToken cancellationToken = default)
    {
        Check.NotNullOrWhiteSpace(nodeId);
        Check.NotNullOrWhiteSpace(deviceName);

        // 清理过期请求
        CleanupExpired();

        // 检查待处理请求数量限制
        if (_pendingRequests.Count >= _options.MaxPendingPairings)
        {
            throw new BusinessException(
                $"Maximum pending pairings ({_options.MaxPendingPairings}) exceeded. Please approve or reject existing requests first.",
                httpStatusCode: 429);
        }

        var code = GeneratePairingCode(_options.PairingCodeLength);
        var now = DateTimeOffset.UtcNow;
        var request = new PairingRequest
        {
            Id = Guid.NewGuid().ToString("N"),
            Code = code,
            NodeId = nodeId,
            DeviceName = deviceName,
            Platform = platform,
            CreatedAt = now,
            ExpiresAt = now.AddMinutes(_options.PairingCodeTtlMinutes)
        };

        _pendingRequests[code] = request;
        _idToCode[request.Id] = code;

        _logger.LogInformation("Pairing request created for device {NodeId} with code {Code}",
            nodeId, code);

        // 发布事件（fire-and-forget，不影响主流程）
        try
        {
            _ = _eventBus?.PublishAsync(new DevicePairingRequestedEvent
            {
                NodeId = nodeId,
                PairingCode = code
            });
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to publish DevicePairingRequestedEvent for nodeId={NodeId}", nodeId);
        }

        return Task.FromResult(request);
    }

    public Task<DevicePairingInfo?> ApprovePairingAsync(string code, CancellationToken cancellationToken = default)
    {
        Check.NotNullOrWhiteSpace(code);
        CleanupExpired();

        if (!_pendingRequests.TryRemove(code, out var request))
        {
            return Task.FromResult<DevicePairingInfo?>(null);
        }

        _idToCode.TryRemove(request.Id, out _);
        return Task.FromResult<DevicePairingInfo?>(ApproveRequest(request));
    }

    /// <summary>
    /// Approve a pending pairing request by its public Id — safe for use in URLs/logs
    /// </summary>
    public Task<DevicePairingInfo?> ApprovePairingByIdAsync(string id, CancellationToken cancellationToken = default)
    {
        Check.NotNullOrWhiteSpace(id);
        CleanupExpired();

        if (!_idToCode.TryRemove(id, out var code))
        {
            return Task.FromResult<DevicePairingInfo?>(null);
        }

        if (!_pendingRequests.TryRemove(code, out var request))
        {
            return Task.FromResult<DevicePairingInfo?>(null);
        }

        return Task.FromResult<DevicePairingInfo?>(ApproveRequest(request));
    }

    private DevicePairingInfo ApproveRequest(PairingRequest request)
    {
        var info = new DevicePairingInfo
        {
            NodeId = request.NodeId,
            Name = request.DeviceName,
            Platform = request.Platform,
            Capabilities = []
        };

        _approvedDevices[request.NodeId] = info;

        _logger.LogInformation("Pairing approved for device {NodeId}", request.NodeId);

        // 发布事件（fire-and-forget，不影响主流程）
        try
        {
            _ = _eventBus?.PublishAsync(new DevicePairingApprovedEvent
            {
                NodeId = request.NodeId
            });
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to publish DevicePairingApprovedEvent for nodeId={NodeId}", request.NodeId);
        }

        return info;
    }

    public Task<bool> RejectPairingAsync(string code, CancellationToken cancellationToken = default)
    {
        Check.NotNullOrWhiteSpace(code);
        if (_pendingRequests.TryRemove(code, out var req))
        {
            _idToCode.TryRemove(req.Id, out _);
            _logger.LogInformation("Pairing rejected for code [redacted]");
            return Task.FromResult(true);
        }

        return Task.FromResult(false);
    }

    /// <summary>
    /// Reject a pending pairing request by its public Id — safe for use in URLs/logs
    /// </summary>
    public Task<bool> RejectPairingByIdAsync(string id, CancellationToken cancellationToken = default)
    {
        Check.NotNullOrWhiteSpace(id);

        if (!_idToCode.TryRemove(id, out var code))
        {
            return Task.FromResult(false);
        }

        var removed = _pendingRequests.TryRemove(code, out _);

        if (removed)
        {
            _logger.LogInformation("Pairing rejected for request {RequestId}", id);
        }

        return Task.FromResult(removed);
    }

    public Task<IReadOnlyList<PendingPairingRequestDto>> GetPendingRequestsAsync(CancellationToken cancellationToken = default)
    {
        CleanupExpired();
        IReadOnlyList<PendingPairingRequestDto> result = _pendingRequests.Values
            .Select(r => new PendingPairingRequestDto
            {
                Id = r.Id,
                NodeId = r.NodeId,
                DeviceName = r.DeviceName,
                Platform = r.Platform,
                CreatedAt = r.CreatedAt,
                ExpiresAt = r.ExpiresAt
            })
            .ToList()
            .AsReadOnly();
        return Task.FromResult(result);
    }

    public Task<bool> IsApprovedAsync(string nodeId, CancellationToken cancellationToken = default)
    {
        Check.NotNullOrWhiteSpace(nodeId);
        return Task.FromResult(_approvedDevices.ContainsKey(nodeId));
    }

    public Task<bool> RevokeDeviceAsync(string nodeId, CancellationToken cancellationToken = default)
    {
        Check.NotNullOrWhiteSpace(nodeId);
        var removed = _approvedDevices.TryRemove(nodeId, out _);

        if (removed)
        {
            _logger.LogInformation("Device approval revoked for {NodeId}", nodeId);
        }

        return Task.FromResult(removed);
    }

    /// <summary>
    /// Remove expired pairing requests and their Id→Code mappings
    /// </summary>
    private void CleanupExpired()
    {
        var now = DateTimeOffset.UtcNow;
        var expired = _pendingRequests
            .Where(kvp => kvp.Value.ExpiresAt < now)
            .Select(kvp => kvp.Value)
            .ToList();

        foreach (var req in expired)
        {
            _pendingRequests.TryRemove(req.Code, out _);
            _idToCode.TryRemove(req.Id, out _);
        }
    }

    /// <summary>
    /// Generate a cryptographically-random pairing code using the safe alphabet
    /// </summary>
    private static string GeneratePairingCode(int length)
    {
        var chars = new char[length];
        for (var i = 0; i < length; i++)
        {
            chars[i] = PairingAlphabet[RandomNumberGenerator.GetInt32(PairingAlphabet.Length)];
        }

        return new string(chars);
    }
}
