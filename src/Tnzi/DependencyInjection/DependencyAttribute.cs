
namespace Tnzi.DependencyInjection;

/// <summary>
/// 依赖注入行为特性
/// 提供比标记接口更细粒度的控制
/// </summary>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false)]
public class DependencyAttribute : Attribute
{
    /// <summary>
    /// 生命周期类型
    /// </summary>
    public ServiceLifetime Lifetime { get; }

    /// <summary>
    /// 是否为 TryAdd 方式注册（默认服务，可被替换）
    /// </summary>
    public bool TryAdd { get; set; }

    /// <summary>
    /// 是否替换已存在的服务实现（主要服务，优先使用）
    /// </summary>
    public bool ReplaceExisting { get; set; }

    /// <summary>
    /// 是否注册自身类型（即使有接口也注册自身）
    /// </summary>
    public bool AddSelf { get; set; }

    public DependencyAttribute(ServiceLifetime lifetime)
    {
        Lifetime = lifetime;
    }
}