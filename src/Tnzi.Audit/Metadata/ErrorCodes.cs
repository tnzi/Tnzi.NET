namespace Tnzi.Audit.Metadata;

/// <summary>
/// Audit module error code constants.
/// </summary>
public static class ErrorCodes
{
    /// <summary>
    /// Audit operation not found.
    /// </summary>
    public const string AuditOperationNotFound = "AUDIT_OPERATION_NOT_FOUND";

    /// <summary>
    /// Failed to delete expired audit data.
    /// </summary>
    public const string AuditDeleteExpiredFailed = "AUDIT_DELETE_EXPIRED_FAILED";
}
