
namespace Tnzi.AspNetCore.Mvc.Filters;

/// <summary>
/// 工作单元特性，用于可选标记模式
/// 用户可以在 Controller 或 Action 方法上标记此特性，该方法中的所有操作都会被包含在一个事务中
/// 任何异常都会自动回滚
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method)]
public class UnitOfWorkAttribute : ServiceFilterAttribute
{
    /// <summary>
    /// 获取或设置 是否禁用工作单元（默认false）
    /// 用于在全局过滤器模式下禁用特定方法的事务
    /// </summary>
    public bool IsDisabled { get; set; } = false;

    /// <summary>
    /// 工作单元特性构造函数
    /// </summary>
    public UnitOfWorkAttribute()
        : base(typeof(UnitOfWorkActionFilter))
    {
    }
}