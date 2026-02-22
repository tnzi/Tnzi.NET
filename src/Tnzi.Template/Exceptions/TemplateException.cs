namespace Tnzi.Template.Exceptions;

/// <summary>
/// 模板异常基类
/// </summary>
public class TemplateException : BusinessException
{
    /// <summary>
    /// 初始化一个<see cref="TemplateException"/>类型的新实例
    /// </summary>
    /// <param name="message">异常消息</param>
    /// <param name="errorCode">错误码</param>
    /// <param name="httpStatusCode">HTTP状态码</param>
    public TemplateException(
        string message,
        string errorCode = ErrorCodes.TEMPLATE_ERROR,
        int httpStatusCode = 400)
        : base(message, errorCode, httpStatusCode)
    {
    }

    /// <summary>
    /// 初始化一个<see cref="TemplateException"/>类型的新实例
    /// </summary>
    /// <param name="message">异常消息</param>
    /// <param name="innerException">内部异常</param>
    /// <param name="errorCode">错误码</param>
    /// <param name="httpStatusCode">HTTP状态码</param>
    public TemplateException(
        string message,
        Exception innerException,
        string errorCode = ErrorCodes.TEMPLATE_ERROR,
        int httpStatusCode = 400)
        : base(message, innerException, errorCode, httpStatusCode)
    {
    }
}

/// <summary>
/// 模板渲染异常
/// </summary>
public class TemplateRenderException : TemplateException
{
    /// <summary>
    /// 模板名称（如果有）
    /// </summary>
    public string? TemplateName { get; }

    /// <summary>
    /// 初始化一个<see cref="TemplateRenderException"/>类型的新实例
    /// </summary>
    /// <param name="message">异常消息</param>
    /// <param name="innerException">内部异常</param>
    /// <param name="templateName">模板名称</param>
    public TemplateRenderException(string message, Exception innerException, string? templateName = null)
        : base(message, innerException, ErrorCodes.TEMPLATE_RENDER_ERROR, 500)
    {
        TemplateName = templateName;
    }
}

/// <summary>
/// 模板未找到异常
/// </summary>
public class TemplateNotFoundException : TemplateException
{
    /// <summary>
    /// 模板路径
    /// </summary>
    public string TemplatePath { get; }

    /// <summary>
    /// 初始化一个<see cref="TemplateNotFoundException"/>类型的新实例
    /// </summary>
    /// <param name="templatePath">模板路径</param>
    public TemplateNotFoundException(string templatePath)
        : base($"Template file not found: {templatePath}", ErrorCodes.TEMPLATE_NOT_FOUND, 404)
    {
        TemplatePath = templatePath;
    }
}

/// <summary>
/// 模板编译异常
/// </summary>
public class TemplateCompilationException : TemplateException
{
    /// <summary>
    /// 编译错误列表
    /// </summary>
    public IEnumerable<string> CompilationErrors { get; }

    /// <summary>
    /// 初始化一个<see cref="TemplateCompilationException"/>类型的新实例
    /// </summary>
    /// <param name="message">异常消息</param>
    /// <param name="errors">编译错误列表</param>
    public TemplateCompilationException(string message, IEnumerable<string> errors)
        : base(message, ErrorCodes.TEMPLATE_COMPILATION_ERROR, 500)
    {
        CompilationErrors = errors;
    }

    /// <summary>
    /// 初始化一个<see cref="TemplateCompilationException"/>类型的新实例
    /// </summary>
    /// <param name="message">异常消息</param>
    /// <param name="innerException">内部异常</param>
    public TemplateCompilationException(string message, Exception innerException)
        : base(message, innerException, ErrorCodes.TEMPLATE_COMPILATION_ERROR, 500)
    {
        CompilationErrors = new[] { innerException.Message };
    }
}

/// <summary>
/// 模板安全异常（路径遍历等）
/// </summary>
public class TemplateSecurityException : TemplateException
{
    /// <summary>
    /// 检测到的恶意路径
    /// </summary>
    public string? MaliciousPath { get; }

    /// <summary>
    /// 初始化一个<see cref="TemplateSecurityException"/>类型的新实例
    /// </summary>
    /// <param name="message">异常消息</param>
    /// <param name="maliciousPath">恶意路径</param>
    public TemplateSecurityException(string message, string? maliciousPath = null)
        : base(message, ErrorCodes.TEMPLATE_SECURITY_ERROR, 403)
    {
        MaliciousPath = maliciousPath;
    }
}
