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

    private readonly ConcurrentDictionary<string, PairingRequest> _pendingRequests = new(StringComparer.OrdinalIgnoreCase);
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

        return Task.FromResult<DevicePairingInfo?>(info);
    }

    public Task<bool> RejectPairingAsync(string code, CancellationToken cancellationToken = default)
    {
        Check.NotNullOrWhiteSpace(code);
        var removed = _pendingRequests.TryRemove(code, out _);

        if (removed)
        {
            _logger.LogInformation("Pairing rejected for code {Code}", code);
        }

        return Task.FromResult(removed);
    }

    public Task<IReadOnlyList<PairingRequest>> GetPendingRequestsAsync(CancellationToken cancellationToken = default)
    {
        CleanupExpired();
        IReadOnlyList<PairingRequest> result = _pendingRequests.Values.ToList().AsReadOnly();
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
    /// Remove expired pairing requests
    /// </summary>
    private void CleanupExpired()
    {
        var now = DateTimeOffset.UtcNow;
        var expired = _pendingRequests
            .Where(kvp => kvp.Value.ExpiresAt < now)
            .Select(kvp => kvp.Key)
            .ToList();

        foreach (var code in expired)
        {
            _pendingRequests.TryRemove(code, out _);
        }
    }

    /// <summary>
    /// Generate a random pairing code using the safe alphabet
    /// </summary>
    private static string GeneratePairingCode(int length)
    {
        var chars = new char[length];
        for (var i = 0; i < length; i++)
        {
            chars[i] = PairingAlphabet[Random.Shared.Next(PairingAlphabet.Length)];
        }

        return new string(chars);
    }
}
