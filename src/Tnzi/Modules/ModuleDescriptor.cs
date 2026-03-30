
namespace Tnzi.Modules;

/// <summary>
/// 模块初始化状态
/// </summary>
public enum ModuleInitializationState
{
    /// <summary>
    /// 未开始
    /// </summary>
    NotStarted,

    /// <summary>
    /// 初始化成功
    /// </summary>
    Succeeded,

    /// <summary>
    /// 初始化失败
    /// </summary>
    Failed
}

public interface IModuleDescriptor
{
    Type Type { get; }
    ITnziModule Instance { get; }
    Assembly Assembly { get; }
    IReadOnlyList<IModuleDescriptor> Dependencies { get; }
    
    /// <summary>
    /// 模块是否已启用（在 OnApplicationInitializationAsync 完成后为 true）
    /// </summary>
    bool IsEnabled { get; }
    
    /// <summary>
    /// 模块初始化状态
    /// </summary>
    ModuleInitializationState InitializationState { get; }
    
    /// <summary>
    /// 初始化失败时的异常信息
    /// </summary>
    Exception? InitializationError { get; }

    /// <summary>
    /// Module architecture manifest — auto-generated during initialization
    /// </summary>
    ModuleManifest Manifest { get; }

    void AddDependency(IModuleDescriptor descriptor);
}

public class ModuleDescriptor : IModuleDescriptor
{
    public Type Type { get; }
    public ITnziModule Instance { get; }
    public Assembly Assembly { get; }
    
    private readonly List<IModuleDescriptor> _dependencies;
    private readonly HashSet<Type> _dependencyTypes = [];
    public IReadOnlyList<IModuleDescriptor> Dependencies => _dependencies;

    /// <summary>
    /// 模块是否已启用（在 OnApplicationInitializationAsync 完成后为 true）
    /// </summary>
    public bool IsEnabled { get; internal set; }

    /// <summary>
    /// 模块初始化状态
    /// </summary>
    public ModuleInitializationState InitializationState { get; internal set; } = ModuleInitializationState.NotStarted;

    /// <summary>
    /// 初始化失败时的异常信息
    /// </summary>
    public Exception? InitializationError { get; internal set; }

    /// <summary>
    /// 模块初始化开始时间
    /// </summary>
    public DateTime? InitializationStartTime { get; internal set; }

    /// <summary>
    /// 模块初始化完成时间
    /// </summary>
    public DateTime? InitializationEndTime { get; internal set; }

    /// <summary>
    /// Module architecture manifest — auto-generated during initialization
    /// </summary>
    public ModuleManifest Manifest { get; internal set; } = ModuleManifest.Empty;

    public ModuleDescriptor(Type type, ITnziModule instance)
    {
        Type = Check.NotNull(type);
        Instance = Check.NotNull(instance);
        Assembly = type.Assembly;
        _dependencies = new List<IModuleDescriptor>();
    }

    public void AddDependency(IModuleDescriptor descriptor)
    {
        if (descriptor != null && _dependencyTypes.Add(descriptor.Type))
        {
            _dependencies.Add(descriptor);
        }
    }
}

