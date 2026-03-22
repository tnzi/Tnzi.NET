
namespace Tnzi.AspNetCore.Dtos;

/// <summary>
/// Module diagnostics information
/// </summary>
public class ModuleDiagnosticsDto
{
    public string Type { get; set; } = string.Empty;
    public string Assembly { get; set; } = string.Empty;
    public bool IsEnabled { get; set; }
    public string InitializationState { get; set; } = string.Empty;
    public int DependencyCount { get; set; }
    public ModuleManifestDto Manifest { get; set; } = new();
}

/// <summary>
/// Module manifest summary
/// </summary>
public class ModuleManifestDto
{
    public int ServiceCount { get; set; }
    public List<string> Controllers { get; set; } = [];
    public List<string> Events { get; set; } = [];
    public List<string> BackgroundTasks { get; set; } = [];
    public List<string> Options { get; set; } = [];
}
