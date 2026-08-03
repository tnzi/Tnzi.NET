namespace Tnzi.Authorization.Dtos;

/// <summary>
/// 设置用户直授功能请求（覆盖原有直授集）
/// </summary>
public class SetUserFunctionsRequest
{
    /// <summary>
    /// 功能ID列表
    /// </summary>
    public IEnumerable<Guid> FunctionIds { get; set; } = null!;
}

/// <summary>
/// 在给定切片内设置用户直授/否定集的请求（只覆盖切片内，切片外原样保留）
/// </summary>
/// <remarks>
/// 供"只掌握功能目录一个子集"的消费方使用（例如只渲染自己那几个 <c>xxx.*</c>
/// 码的权限矩阵）：<see cref="ScopeFunctionIds"/> 声明本次写入允许触碰的边界，
/// 边界外的直授行由服务端保证不受影响。<see cref="FunctionIds"/> 必须是
/// <see cref="ScopeFunctionIds"/> 的子集，否则 400。
/// </remarks>
public class SetUserFunctionsInScopeRequest
{
    /// <summary>
    /// 切片：本次写入允许触碰的功能ID全集
    /// </summary>
    public IEnumerable<Guid> ScopeFunctionIds { get; set; } = null!;

    /// <summary>
    /// 切片内的新集合（必须是切片的子集）
    /// </summary>
    public IEnumerable<Guid> FunctionIds { get; set; } = null!;
}
