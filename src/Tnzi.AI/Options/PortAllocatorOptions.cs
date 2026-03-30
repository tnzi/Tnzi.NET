namespace Tnzi.AI.Options;

/// <summary>
/// 端口分配器配置
/// </summary>
public class PortAllocatorOptions
{
    /// <summary>起始端口号（默认 18000）</summary>
    public int StartPort { get; set; } = 18000;

    /// <summary>最大扫描范围（默认 1000）</summary>
    public int MaxRange { get; set; } = 1000;
}

/// <summary>
/// 端口分配器配置验证
/// </summary>
public class PortAllocatorOptionsValidator : OptionsValidatorBase<PortAllocatorOptions>
{
    protected override void ValidateOptions(PortAllocatorOptions options, List<string> errors)
    {
        if (options.StartPort is < 1024 or > 65535)
            errors.Add("StartPort must be between 1024 and 65535");

        if (options.MaxRange <= 0)
            errors.Add("MaxRange must be greater than 0");

        if (options.StartPort + options.MaxRange > 65536)
            errors.Add($"StartPort({options.StartPort}) + MaxRange({options.MaxRange}) exceeds port range 65535");
    }
}
