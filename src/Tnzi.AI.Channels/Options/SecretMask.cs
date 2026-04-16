namespace Tnzi.AI.Channels.Options;

/// <summary>
/// Helper for masking secret values in ToString / log output.
/// Used by adapter option classes to prevent accidental leakage of tokens,
/// app secrets, and signing keys via logs, exception dumps, or diagnostic tools.
/// </summary>
internal static class SecretMask
{
    /// <summary>
    /// Returns a fixed-length redaction marker when the value is non-empty,
    /// and "<null>" / "<empty>" otherwise. Uses a constant length so the
    /// output reveals neither the secret nor its length.
    /// </summary>
    public static string Mask(string? value)
    {
        if (value is null) return "<null>";
        if (value.Length == 0) return "<empty>";
        return "***REDACTED***";
    }
}
