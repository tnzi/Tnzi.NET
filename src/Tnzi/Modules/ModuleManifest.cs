namespace Tnzi.Modules;

/// <summary>
/// 模块架构清单 - 描述一个模块所导出的内容
/// </summary>
public record ModuleManifest
{
    /// <summary>
    /// Empty manifest instance (default for all modules)
    /// </summary>
    public static readonly ModuleManifest Empty = new();

    /// <summary>
    /// Services registered by this module
    /// </summary>
    public IReadOnlyList<ServiceExport> Services { get; init; } = [];

    /// <summary>
    /// Controller types exposed by this module
    /// </summary>
    public IReadOnlyList<string> Controllers { get; init; } = [];

    /// <summary>
    /// Event handler types registered by this module
    /// </summary>
    public IReadOnlyList<string> Events { get; init; } = [];

    /// <summary>
    /// Background task (IHostedService) types registered by this module
    /// </summary>
    public IReadOnlyList<string> BackgroundTasks { get; init; } = [];

    /// <summary>
    /// Options types configured by this module
    /// </summary>
    public IReadOnlyList<string> Options { get; init; } = [];
}

/// <summary>
/// 描述模块导出的单个服务注册
/// </summary>
public record ServiceExport(Type InterfaceType, Type ImplementationType, ServiceLifetime Lifetime);
