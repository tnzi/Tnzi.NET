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

    /// <summary>
    /// An export matched more rows than <c>Audit:ExportMaxRows</c> allows; it was refused rather than truncated.
    /// </summary>
    public const string AuditExportTooLarge = "AUDIT_EXPORT_TOO_LARGE";

    /// <summary>
    /// Another data destruction run holds the lock; this run was skipped, nothing was destroyed and nothing is wrong.
    /// The background service keys its "skip this cycle" decision on this code and on nothing else:
    /// every other 409 from <c>RunAsync</c> (duplicate policy names, lock lost mid-run) is a failure that must stay visible.
    /// </summary>
    public const string AuditDestructionRunInProgress = "AUDIT_DESTRUCTION_RUN_IN_PROGRESS";
}
