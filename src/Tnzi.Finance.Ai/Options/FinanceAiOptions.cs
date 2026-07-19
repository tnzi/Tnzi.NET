namespace Tnzi.Finance.Ai.Options;

/// <summary>
/// Configuration for the AI-backed receipt extractor.
/// </summary>
/// <remarks>
/// When <see cref="Provider"/>/<see cref="Model"/> are unset, the AI module's default provider/model
/// are used. Bound from the <c>Finance:Ai</c> configuration section.
/// </remarks>
[ConfigSection("Finance:Ai")]
public class FinanceAiOptions
{
    /// <summary>AI provider used for extraction (null falls back to the AI module default).</summary>
    public string? Provider { get; set; }

    /// <summary>Model used for extraction (null falls back to the provider default).</summary>
    public string? Model { get; set; }

    /// <summary>Maximum size (MB) of a single receipt file.</summary>
    public int MaxFileSizeMb { get; set; } = 20;
}
