namespace Tnzi.AI.Skills;

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

        foreach (var bin in skill.Requirements.Bins)
        {
            if (!IsBinaryAvailable(bin))
            {
                result.IsValid = false;
                result.MissingBins.Add(bin);
            }
        }

        foreach (var env in skill.Requirements.Envs)
        {
            if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable(env)))
            {
                result.IsValid = false;
                result.MissingEnvs.Add(env);
            }
        }

        if (_configuration != null)
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
