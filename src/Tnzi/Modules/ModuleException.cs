namespace Tnzi.Modules;

/// <summary>
/// 模块异常基类
/// </summary>
public class ModuleException : Tnzi.Exceptions.TnziException
{
    /// <summary>
    /// 模块类型
    /// </summary>
    public Type ModuleType { get; }
    
    /// <summary>
    /// 异常阶段
    /// </summary>
    public string Phase { get; }
    
    /// <summary>
    /// 初始化模块异常
    /// </summary>
    /// <param name="moduleType">模块类型</param>
    /// <param name="phase">异常阶段</param>
    /// <param name="message">异常消息</param>
    /// <param name="innerException">内部异常</param>
    public ModuleException(Type moduleType, string phase, string message, Exception? innerException = null)
        : base("MODULE_ERROR", $"[{moduleType.Name}] {phase}: {message}", innerException)
    {
        ModuleType = moduleType;
        Phase = phase;
    }
}

/// <summary>
/// 模块循环依赖异常
/// </summary>
public class ModuleCircularDependencyException : ModuleException
{
    /// <summary>
    /// 循环依赖路径
    /// </summary>
    public IReadOnlyList<Type> CyclePath { get; }
    
    /// <summary>
    /// 初始化循环依赖异常
    /// </summary>
    /// <param name="message">异常消息</param>
    /// <param name="cyclePath">循环路径</param>
    public ModuleCircularDependencyException(string message, IReadOnlyList<Type> cyclePath)
        : base(cyclePath.LastOrDefault() ?? typeof(object), "DependencyResolution", message)
    {
        CyclePath = cyclePath;
    }
}

/// <summary>
/// 模块缺失依赖异常
/// </summary>
public class ModuleMissingDependencyException : ModuleException
{
    /// <summary>
    /// 缺失的依赖模块类型
    /// </summary>
    public IReadOnlyList<Type> MissingDependencies { get; }
    
    /// <summary>
    /// 初始化缺失依赖异常
    /// </summary>
    /// <param name="moduleType">模块类型</param>
    /// <param name="message">异常消息</param>
    /// <param name="missingDependencies">缺失的依赖</param>
    public ModuleMissingDependencyException(Type moduleType, string message, IReadOnlyList<Type> missingDependencies)
        : base(moduleType, "DependencyResolution", message)
    {
        MissingDependencies = missingDependencies;
    }
}

