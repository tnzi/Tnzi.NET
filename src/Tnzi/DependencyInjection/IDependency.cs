namespace Tnzi.DependencyInjection;

/// <summary>
/// 标记服务为单例生命周期 (Singleton)
/// </summary>
public interface ISingletonDependency { }

/// <summary>
/// 标记服务为作用域生命周期 (Scoped)
/// </summary>
public interface IScopedDependency { }

/// <summary>
/// 标记服务为瞬态生命周期 (Transient)
/// </summary>
public interface ITransientDependency { }

/// <summary>
/// 忽略自动依赖注入注册
/// 用于标记不应自动注册的类型
/// </summary>
[AttributeUsage(AttributeTargets.Class)]
public class IgnoreDependencyAttribute : Attribute { }

