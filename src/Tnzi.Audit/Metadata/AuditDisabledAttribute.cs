namespace Tnzi.Audit.Metadata;

/// <summary>
/// Mark a controller or action to skip audit logging.
/// Useful for sensitive operations (e.g., password changes) or high-frequency health check endpoints.
/// Can be applied at the controller level (all actions) or individual action level.
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = false, Inherited = true)]
public sealed class AuditDisabledAttribute : Attribute
{
    /// <summary>
    /// Optional reason for disabling audit (for documentation purposes)
    /// </summary>
    public string? Reason { get; set; }

    /// <summary>
    /// Create an AuditDisabled attribute
    /// </summary>
    public AuditDisabledAttribute() { }

    /// <summary>
    /// Create an AuditDisabled attribute with reason
    /// </summary>
    /// <param name="reason">Reason for disabling audit</param>
    public AuditDisabledAttribute(string reason)
    {
        Reason = reason;
    }
}
