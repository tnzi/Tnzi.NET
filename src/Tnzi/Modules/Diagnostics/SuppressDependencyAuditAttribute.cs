namespace Tnzi.Modules.Diagnostics;

/// <summary>
/// Suppresses dependency audit warnings for legitimate implicit cross-module dependencies
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Constructor, AllowMultiple = true)]
public class SuppressDependencyAuditAttribute : Attribute
{
    /// <summary>
    /// Reason for suppressing the audit
    /// </summary>
    public string Reason { get; }

    /// <summary>
    /// Specific service type to ignore (null = suppress all for this module)
    /// </summary>
    public Type? IgnoredServiceType { get; set; }

    public SuppressDependencyAuditAttribute(string reason)
    {
        Reason = Check.NotNullOrWhiteSpace(reason);
    }
}
