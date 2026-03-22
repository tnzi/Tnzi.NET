
namespace Tnzi.AspNetCore.Dtos;

/// <summary>
/// Controller diagnostics result
/// </summary>
public class ControllerDiagnosticsResultDto
{
    public int TotalCount { get; set; }
    public List<ControllerInfoDto> Controllers { get; set; } = [];
}

/// <summary>
/// Individual controller info
/// </summary>
public class ControllerInfoDto
{
    public string Type { get; set; } = string.Empty;
    public string Route { get; set; } = string.Empty;
    public string Module { get; set; } = string.Empty;
    public bool IsDefault { get; set; }
    public List<string> Methods { get; set; } = [];
}
