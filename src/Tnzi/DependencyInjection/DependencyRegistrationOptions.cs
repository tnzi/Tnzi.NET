
namespace Tnzi.DependencyInjection;

/// <summary>
/// 依赖注入自动注册选项
/// </summary>
public class DependencyRegistrationOptions
{
    /// <summary>
    /// 要扫描的程序集列表（为空则扫描所有已加载的程序集）
    /// </summary>
    public List<Assembly> Assemblies { get; set; } = new();

    /// <summary>
    /// 是否启用自动注册（默认 true）
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// 是否注册没有接口的类型（默认 true）
    /// </summary>
    public bool RegisterTypesWithoutInterfaces { get; set; } = true;

    /// <summary>
    /// 类型过滤委托（返回 true 表示包含该类型）
    /// </summary>
    public Func<Type, bool>? TypeFilter { get; set; }

    /// <summary>
    /// 是否启用严格框架注册模式
    /// 默认值：false
    /// 
    /// 当为 true 时，如果检测到框架程序集（Tnzi.*）的类型使用自动注册，会抛出异常
    /// 当为 false 时，只输出警告但不阻止注册
    /// </summary>
    public bool StrictFrameworkRegistration { get; set; } = false;
}