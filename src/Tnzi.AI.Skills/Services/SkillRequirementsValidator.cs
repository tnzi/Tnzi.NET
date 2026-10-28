namespace Tnzi.AI.Skills.Services;

/// <summary>
/// Validates skill requirements (bins, envs, configs, os, toolGroups).
/// </summary>
public class SkillRequirementsValidator : ISkillRequirementsValidator
{
    private readonly IOptionsMonitor<AIOptions> _options;
    private readonly IConfiguration? _configuration;
    private readonly IToolRegistry? _toolRegistry;

    private SkillsOptions Options => _options.CurrentValue.ContextProviders.Skills;

    public SkillRequirementsValidator(
        IOptionsMonitor<AIOptions> options,
        IConfiguration? configuration = null,
        IToolRegistry? toolRegistry = null)
    {
        _options = Check.NotNull(options);
        _configuration = configuration;
        _toolRegistry = toolRegistry;
    }

    /// <inheritdoc/>
    public SkillValidationResult ValidateRequirements(SkillDefinition skill)
    {
        Check.NotNull(skill);

        if (!Options.RequireChecksEnabled || skill.Requirements == null)
            return new SkillValidationResult { IsValid = true };

        var result = new SkillValidationResult { IsValid = true };

        // Host requirements of a personal skill are never probed: whoever can write one could
        // otherwise ask the host which secrets are configured and which files exist (see
        // SkillRequirementRules). They count as unmet, so such a skill does not activate.
        var probeHost = !SkillRequirementRules.IsSelfServiceAuthored(skill);

        foreach (var bin in skill.Requirements.Bins)
        {
            if (!probeHost || !SkillRequirementRules.IsBareBinaryName(bin) || !IsBinaryAvailable(bin))
            {
                result.IsValid = false;
                result.MissingBins.Add(bin);
            }
        }

        foreach (var env in skill.Requirements.Envs)
        {
            if (!probeHost || string.IsNullOrEmpty(Environment.GetEnvironmentVariable(env)))
            {
                result.IsValid = false;
                result.MissingEnvs.Add(env);
            }
        }

        if (!probeHost)
        {
            if (skill.Requirements.Configs.Count > 0)
            {
                result.IsValid = false;
                result.MissingConfigs.AddRange(skill.Requirements.Configs);
            }
        }
        else if (_configuration != null)
        {
            foreach (var config in skill.Requirements.Configs)
            {
                if (string.IsNullOrEmpty(_configuration[config]))
                {
                    result.IsValid = false;
                    result.MissingConfigs.Add(config);
                }
            }
        }

        if (_toolRegistry != null)
        {
            foreach (var group in skill.Requirements.ToolGroups)
            {
                var tools = _toolRegistry.GetToolsByGroup(group);
                if (tools.Count == 0)
                {
                    result.IsValid = false;
                    result.MissingToolGroups.Add(group);
                }
            }
        }

        if (skill.Requirements.Os.Count > 0)
        {
            var currentOs = GetCurrentOs();
            if (!skill.Requirements.Os.Contains(currentOs, StringComparer.OrdinalIgnoreCase))
            {
                result.IsValid = false;
                result.UnsupportedOs = currentOs;
            }
        }

        return result;
    }

    /// <summary>
    /// Look a bare program name up in the PATH directories. Callers pass only names that
    /// passed <see cref="SkillRequirementRules.IsBareBinaryName"/>: <c>Path.Combine</c> returns
    /// its second argument unchanged when that is rooted, so a path here would be checked as-is.
    /// </summary>
    private static bool IsBinaryAvailable(string binary)
    {
        try
        {
            var pathEnv = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
            var paths = pathEnv.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries);

            var extensions = OperatingSystem.IsWindows()
                ? new[] { ".exe", ".cmd", ".bat", ".ps1", "" }
                : new[] { "" };

            foreach (var path in paths)
            {
                foreach (var ext in extensions)
                {
                    var fullPath = Path.Combine(path, binary + ext);
                    if (File.Exists(fullPath))
                        return true;
                }
            }

            return false;
        }
        catch
        {
            return false;
        }
    }

    private static string GetCurrentOs()
    {
        if (OperatingSystem.IsWindows()) return "windows";
        if (OperatingSystem.IsLinux()) return "linux";
        if (OperatingSystem.IsMacOS()) return "macos";
        return "unknown";
    }
}
