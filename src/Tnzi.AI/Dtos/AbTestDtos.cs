namespace Tnzi.AI.Dtos;

/// <summary>
/// A/B 测试配置 DTO（存储在 Agent.Configuration JSON 中）
/// </summary>
public class AbTestConfig
{
    /// <summary>Whether A/B test is enabled</summary>
    [JsonPropertyName("enabled")]
    public bool Enabled { get; set; }

    /// <summary>Version number for variant A (control group)</summary>
    [JsonPropertyName("versionA")]
    public int VersionA { get; set; }

    /// <summary>Version number for variant B (experiment group)</summary>
    [JsonPropertyName("versionB")]
    public int VersionB { get; set; }

    /// <summary>Traffic percentage routed to variant B (0-100)</summary>
    [JsonPropertyName("trafficPercentB")]
    [Range(0, 100)]
    public int TrafficPercentB { get; set; }
}

/// <summary>
/// 配置 A/B 测试请求 DTO
/// </summary>
public class ConfigureAbTestDto
{
    /// <summary>Version number for variant A (control group)</summary>
    [Required]
    [Range(1, int.MaxValue)]
    public int VersionA { get; set; }

    /// <summary>Version number for variant B (experiment group)</summary>
    [Required]
    [Range(1, int.MaxValue)]
    public int VersionB { get; set; }

    /// <summary>Traffic percentage routed to variant B (0-100)</summary>
    [Required]
    [Range(0, 100)]
    public int TrafficPercentB { get; set; }
}
