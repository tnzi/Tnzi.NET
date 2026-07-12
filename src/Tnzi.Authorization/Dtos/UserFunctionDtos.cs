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
