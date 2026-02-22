namespace Tnzi.AspNetCore.Middleware;

/// <summary>
/// 自定义异常处理器接口
/// </summary>
/// <typeparam name="T">异常类型</typeparam>
public interface IExceptionHandler<T> where T : Exception
{
    /// <summary>
    /// 处理异常
    /// </summary>
    /// <param name="exception">异常对象</param>
    /// <param name="context">HTTP 上下文</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>异常处理结果，null 表示继续使用默认处理</returns>
    Task<ExceptionHandlingResult?> HandleAsync(T exception, HttpContext context, CancellationToken cancellationToken = default);
}

/// <summary>
/// 异常处理结果
/// </summary>
public class ExceptionHandlingResult
{
    /// <summary>
    /// HTTP 状态码
    /// </summary>
    public int? StatusCode { get; set; }

    /// <summary>
    /// 错误消息
    /// </summary>
    public string? Message { get; set; }

    /// <summary>
    /// 错误代码
    /// </summary>
    public string? ErrorCode { get; set; }

    /// <summary>
    /// 错误详情
    /// </summary>
    public object? ErrorDetails { get; set; }

    /// <summary>
    /// 上下文数据
    /// </summary>
    public object? ContextData { get; set; }

    /// <summary>
    /// 是否可重试
    /// </summary>
    public bool? IsRetryable { get; set; }

    /// <summary>
    /// 错误详情（开发环境）
    /// </summary>
    public string? ErrorDetail { get; set; }

    /// <summary>
    /// 是否继续使用默认处理逻辑
    /// </summary>
    public bool ShouldContinueHandling { get; set; } = false;
}
