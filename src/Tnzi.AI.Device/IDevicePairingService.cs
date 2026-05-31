namespace Tnzi.AI.Device;

/// <summary>
/// Service for managing device pairing requests and approvals
/// </summary>
[ExperimentalApi(Reason = "Gateway/Device/Workspace API under active development")]
public interface IDevicePairingService
{
    /// <summary>
    /// Create a new pairing request for a device
    /// </summary>
    Task<PairingRequest> CreatePairingRequestAsync(string nodeId, string deviceName, DevicePlatform platform, CancellationToken cancellationToken = default);

    /// <summary>
    /// Approve a pending pairing request by its secret Code (device-side use)
    /// </summary>
    Task<DevicePairingInfo?> ApprovePairingAsync(string code, CancellationToken cancellationToken = default);

    /// <summary>
    /// Approve a pending pairing request by its public Id — safe for admin API endpoints
    /// </summary>
    Task<DevicePairingInfo?> ApprovePairingByIdAsync(string id, CancellationToken cancellationToken = default);

    /// <summary>
    /// Reject a pending pairing request by its secret Code (device-side use)
    /// </summary>
    Task<bool> RejectPairingAsync(string code, CancellationToken cancellationToken = default);

    /// <summary>
    /// Reject a pending pairing request by its public Id — safe for admin API endpoints
    /// </summary>
    Task<bool> RejectPairingByIdAsync(string id, CancellationToken cancellationToken = default);

    /// <summary>
    /// Get all pending pairing requests — Code is redacted from returned DTOs
    /// </summary>
    Task<IReadOnlyList<PendingPairingRequestDto>> GetPendingRequestsAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Check if a device node is approved
    /// </summary>
    Task<bool> IsApprovedAsync(string nodeId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Revoke an approved device
    /// </summary>
    Task<bool> RevokeDeviceAsync(string nodeId, CancellationToken cancellationToken = default);
}
