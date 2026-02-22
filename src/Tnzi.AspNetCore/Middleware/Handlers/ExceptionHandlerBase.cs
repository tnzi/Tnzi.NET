
namespace Tnzi.AspNetCore.Middleware.Handlers;

/// <summary>
/// 异常处理器基类
/// 提供通用的异常处理逻辑
/// </summary>
public abstract class ExceptionHandlerBase
{
    protected readonly IWebHostEnvironment Environment;
    protected readonly IOptionsMonitor<ExceptionHandlingOptions> Options;
    protected readonly ILogger Logger;

    protected ExceptionHandlerBase(
        IWebHostEnvironment environment,
        IOptionsMonitor<ExceptionHandlingOptions> options,
        ILogger logger)
    {
        Environment = Check.NotNull(environment);
        Options = Check.NotNull(options);
        Logger = Check.NotNull(logger);
    }

    /// <summary>
    /// 处理器优先级（值越大优先级越高，优先处理）
    /// 默认为 0，子类可通过覆盖提升优先级
    /// </summary>
    public virtual int Priority => 0;

    /// <summary>
    /// 处理异常
    /// </summary>
    /// <param name="context">HTTP上下文</param>
    /// <param name="exception">异常对象</param>
    /// <returns>异常处理结果</returns>
    public abstract Task<ExceptionHandlingResult> HandleAsync(HttpContext context, Exception exception);

    /// <summary>
    /// 判断是否可以处理该异常
    /// </summary>
    /// <param name="exception">异常对象</param>
    /// <returns>是否可以处理</returns>
    public abstract bool CanHandle(Exception exception);

    /// <summary>
    /// 获取本地化消息
    /// </summary>
    protected string? GetLocalizedMessage(HttpContext context, string? errorCode, string defaultMessage)
    {
        if (string.IsNullOrEmpty(errorCode))
            return defaultMessage;

        var factory = context.RequestServices.GetService<IStringLocalizerFactory>();
        if (factory == null)
            return defaultMessage;

        var sharedResourceType = Type.GetType("Tnzi.Localization.Resources.SharedResource, Tnzi.Localization");
        if (sharedResourceType == null)
            return defaultMessage;

        var localizer = factory.Create(sharedResourceType);
        var localized = localizer[errorCode];
        return localized.ResourceNotFound ? defaultMessage : localized.Value;
    }

    /// <summary>
    /// 获取错误代码
    /// </summary>
    protected string? GetErrorCode(Exception exception)
    {
        return exception switch
        {
            TnziException tnziException => tnziException.Code,
            _ => null
        };
    }

    /// <summary>
    /// 获取HTTP状态码
    /// </summary>
    protected int GetHttpStatusCode(Exception exception)
    {
        return exception switch
        {
            BusinessException businessException => businessException.HttpStatusCode,
            InfrastructureException => 503, // Service Unavailable
            TnziException => 500,
            _ => 500
        };
    }

    /// <summary>
    /// 是否开发环境
    /// </summary>
    protected bool IsDevelopment => Environment.IsDevelopment();

    /// <summary>
    /// 获取当前配置选项
    /// </summary>
    protected ExceptionHandlingOptions CurrentOptions => Options.CurrentValue;
}