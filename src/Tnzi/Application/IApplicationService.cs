namespace Tnzi.Application;

/// <summary>
/// 应用服务接口（标记接口）
/// </summary>
public interface IApplicationService
{
    /// <summary>
    /// 服务名称（可选，用于日志和诊断）
    /// 默认返回实现类的名称（不含命名空间）
    /// </summary>
    string? ServiceName => null;

    /// <summary>
    /// 所属模块名称（可选，用于日志和诊断）
    /// 默认从命名空间推断
    /// </summary>
    string? ModuleName => null;
}

