namespace Tnzi.Results;

/// <summary>
/// 结果基类（抽象基类）
/// 提供所有结果类的基础实现，定义通用的成功/失败逻辑
/// </summary>
/// <typeparam name="TData">数据类型</typeparam>
/// <remarks>
/// 用途：
/// - 作为 Result 和 ApiResult 的基类，提供统一的实现
/// - 定义通用的成功/失败判断逻辑
/// - 提供基础的消息和数据存储
/// </remarks>
public abstract class BaseResult<TData> : IResult<TData>
{
    /// <summary>
    /// 是否成功
    /// </summary>
    public bool Succeeded { get; protected set; }

    /// <summary>
    /// 是否失败（向后兼容）
    /// </summary>
    public bool Failed => !Succeeded;

    /// <summary>
    /// 返回消息
    /// </summary>
    public string? Message { get; protected set; }

    /// <summary>
    /// 结果数据
    /// </summary>
    public TData? Data { get; protected set; }

    /// <summary>
    /// 初始化结果基类
    /// </summary>
    /// <param name="succeeded">是否成功</param>
    /// <param name="data">数据</param>
    /// <param name="message">消息</param>
    protected BaseResult(bool succeeded, TData? data = default, string? message = null)
    {
        Succeeded = succeeded;
        Data = data;
        Message = message;
    }
}

/// <summary>
/// 无数据版本的结果基类
/// </summary>
public abstract class BaseResult : BaseResult<object>
{
    /// <summary>
    /// 初始化结果基类
    /// </summary>
    /// <param name="succeeded">是否成功</param>
    /// <param name="message">消息</param>
    protected BaseResult(bool succeeded, string? message = null)
        : base(succeeded, null, message)
    {
    }
}
