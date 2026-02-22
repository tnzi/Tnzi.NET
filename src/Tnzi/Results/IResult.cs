namespace Tnzi.Results;

/// <summary>
/// 结果接口（最基础的结果接口）
/// 定义所有结果类必须实现的基本属性
/// </summary>
/// <typeparam name="TData">数据类型</typeparam>
public interface IResult<out TData>
{
    /// <summary>
    /// 是否成功
    /// </summary>
    bool Succeeded { get; }

    /// <summary>
    /// 返回消息
    /// </summary>
    string? Message { get; }

    /// <summary>
    /// 结果数据
    /// </summary>
    TData? Data { get; }
}

/// <summary>
/// 无数据版本的结果接口
/// </summary>
public interface IResult : IResult<object>
{
}
