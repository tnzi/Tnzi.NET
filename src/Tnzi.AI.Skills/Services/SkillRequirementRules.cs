namespace Tnzi.AI.Skills.Services;

/// <summary>
/// What a skill's <see cref="SkillRequirements"/> may name, and who may name it.
/// </summary>
/// <remarks>
/// <para>
/// Bins, envs and configs are probes of the host: activation answers "is this binary on the
/// PATH", "is this environment variable set", "is this configuration key set", and names every
/// one that is missing. On a skill written by an administrator (Tenant or System scope, or a
/// SKILL.md the operator installed) that is the point. On a personal skill any signed-in user
/// can write, it turns activation into an oracle: list <c>Jwt:SigningKey</c> or
/// <c>AWS_SECRET_ACCESS_KEY</c> and the answer tells whether the secret is configured, list
/// <c>/etc/shadow</c> or a UNC path as a "binary" and the server checks that path (and on
/// Windows opens an SMB connection to a host the caller chose).
/// </para>
/// <para>
/// So host requirements are refused on personal skills when they are written, and a personal
/// skill that already carries them (written before this rule) is never probed for them: they
/// count as unmet. A binary is only ever looked up as a bare file name inside the PATH
/// directories, whoever wrote the skill.
/// </para>
/// </remarks>
internal static partial class SkillRequirementRules
{
    private const int MaxNameLength = 128;
    private const int MaxConfigKeyLength = 256;

    [GeneratedRegex(@"^[A-Za-z_][A-Za-z0-9_]*$")]
    private static partial Regex EnvNamePattern();

    // Configuration keys are colon-separated sections of letters, digits, '_', '.' and '-'.
    [GeneratedRegex(@"^[A-Za-z0-9_.\-]+(:[A-Za-z0-9_.\-]+)*$")]
    private static partial Regex ConfigKeyPattern();

    /// <summary>
    /// Whether the skill comes from a user's own self-service write rather than from an
    /// administrator or the operator. Only database rows in the User scope qualify.
    /// </summary>
    public static bool IsSelfServiceAuthored(SkillDefinition skill)
        => skill.Scope == SkillScope.User && skill.Source == SkillSource.Database;

    /// <summary>
    /// A binary requirement names a program, never a path: no directory separators, no drive
    /// or rooted form, no "." or "..", nothing the file system would treat as a location.
    /// </summary>
    public static bool IsBareBinaryName(string? binary)
    {
        if (string.IsNullOrWhiteSpace(binary) || binary.Length > MaxNameLength)
            return false;

        if (binary is "." or "..")
            return false;

        if (binary.IndexOfAny(['/', '\\', ':']) >= 0 || binary.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
            return false;

        return !Path.IsPathRooted(binary);
    }

    /// <summary>
    /// Check requirements at the write boundary (create, update, import). Returns the reason
    /// they are refused, or null when they may be stored.
    /// </summary>
    public static string? Validate(SkillRequirements? requirements, SkillScope scope)
    {
        if (requirements == null)
            return null;

        var bins = requirements.Bins ?? [];
        var envs = requirements.Envs ?? [];
        var configs = requirements.Configs ?? [];

        if (scope == SkillScope.User && (bins.Count > 0 || envs.Count > 0 || configs.Count > 0))
            return "Personal skills cannot declare binary, environment variable or configuration requirements; only shared skills managed by administrators can.";

        var badBin = bins.FirstOrDefault(b => !IsBareBinaryName(b));
        if (badBin != null)
            return $"Binary requirement '{badBin}' must be a bare program name, not a path.";

        var badEnv = envs.FirstOrDefault(e => string.IsNullOrEmpty(e) || e.Length > MaxNameLength || !EnvNamePattern().IsMatch(e));
        if (badEnv != null)
            return $"Environment variable requirement '{badEnv}' is not a valid variable name.";

        var badConfig = configs.FirstOrDefault(c => string.IsNullOrEmpty(c) || c.Length > MaxConfigKeyLength || !ConfigKeyPattern().IsMatch(c));
        if (badConfig != null)
            return $"Configuration requirement '{badConfig}' is not a valid configuration key.";

        return null;
    }
}
