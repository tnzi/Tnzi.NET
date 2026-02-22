namespace Tnzi.Exceptions;

/// <summary>
/// 业务异常基类（用于业务逻辑错误）
/// 所有业务相关的异常都应继承此类
/// </summary>
public class BusinessException : TnziException
{
    /// <summary>
    /// HTTP状态码
    /// </summary>
    public int HttpStatusCode { get; }

    /// <summary>
    /// 初始化一个<see cref="BusinessException"/>类型的新实例
    /// </summary>
    /// <param name="message">错误消息</param>
    /// <param name="errorCode">错误码</param>
    /// <param name="httpStatusCode">HTTP状态码，默认400</param>
    public BusinessException(string message, string errorCode = ErrorCodes.BUSINESS_ERROR, int httpStatusCode = 400)
        : base(errorCode, message)
    {
        HttpStatusCode = httpStatusCode;
    }

    /// <summary>
    /// 初始化一个<see cref="BusinessException"/>类型的新实例
    /// </summary>
    /// <param name="message">错误消息</param>
    /// <param name="innerException">内部异常</param>
    /// <param name="errorCode">错误码</param>
    /// <param name="httpStatusCode">HTTP状态码，默认400</param>
    public BusinessException(string message, Exception innerException, string errorCode = ErrorCodes.BUSINESS_ERROR, int httpStatusCode = 400)
        : base(errorCode, message, innerException)
    {
        HttpStatusCode = httpStatusCode;
    }
}

/// <summary>
/// 资源未找到异常
/// </summary>
public class ResourceNotFoundException : BusinessException
{
    public ResourceNotFoundException(string resourceName, object? resourceId = null)
        : base(
            resourceId != null
                ? $"Resource '{resourceName}' with id '{resourceId}' was not found."
                : $"Resource '{resourceName}' was not found.",
            ErrorCodes.RESOURCE_NOT_FOUND,
            404)
    {
    }
}

/// <summary>
/// 未授权异常（401 - 未认证）
/// </summary>
public class UnauthorizedException : BusinessException
{
    public UnauthorizedException(string message = "Unauthorized access.")
        : base(message, ErrorCodes.UNAUTHORIZED, 401)
    {
    }
}

/// <summary>
/// 禁止访问异常（403 - 已认证但无权限）
/// </summary>
public class ForbiddenException : BusinessException
{
    /// <summary>
    /// 需要的权限
    /// </summary>
    public string? RequiredPermission { get; }

    public ForbiddenException(string message = "Access forbidden.")
        : base(message, ErrorCodes.FORBIDDEN, 403)
    {
    }

    public ForbiddenException(string message, string requiredPermission)
        : base(message, ErrorCodes.FORBIDDEN, 403)
    {
        RequiredPermission = requiredPermission;
    }

    /// <summary>
    /// 初始化一个<see cref="ForbiddenException"/>类型的新实例
    /// </summary>
    /// <param name="message">错误消息</param>
    /// <param name="errorCode">错误码</param>
    /// <param name="requiredPermission">需要的权限（可选）</param>
    public ForbiddenException(string message, string errorCode, string? requiredPermission = null)
        : base(message, errorCode, 403)
    {
        RequiredPermission = requiredPermission;
    }
}

/// <summary>
/// 验证失败异常
/// </summary>
public class ValidationException : BusinessException
{
    /// <summary>
    /// 验证错误详情
    /// </summary>
    public Dictionary<string, string[]>? ValidationErrors { get; }

    public ValidationException(string message, Dictionary<string, string[]>? validationErrors = null)
        : base(message, ErrorCodes.VALIDATION_ERROR, 400)
    {
        ValidationErrors = validationErrors;
    }

    /// <summary>
    /// 从ValidationError列表创建验证异常
    /// </summary>
    public ValidationException(IEnumerable<ValidationError> errors)
        : base("One or more validation errors occurred.", ErrorCodes.VALIDATION_ERROR, 400)
    {
        ValidationErrors = errors
            .GroupBy(e => e.PropertyName)
            .ToDictionary(g => g.Key, g => g.Select(e => e.ErrorMessage).ToArray());
    }
}


/// <summary>
/// 授权异常（用于权限检查失败）
/// </summary>
public class AuthorizationException : ForbiddenException
{
    public AuthorizationException(string message, string? requiredPermission = null)
        : base(message, ErrorCodes.FORBIDDEN, requiredPermission)
    {
    }
}

/// <summary>
/// 数据冲突异常
/// </summary>
public class ConflictException : BusinessException
{
    public ConflictException(string message)
        : base(message, ErrorCodes.DATA_CONFLICT, 409)
    {
    }
}

/// <summary>
/// 重复键异常（数据重复）
/// 用于表示唯一键约束冲突或重复数据
/// </summary>
public class DuplicateKeyException : ConflictException
{
    /// <summary>
    /// 实体类型名称
    /// </summary>
    public string? EntityTypeName { get; }

    /// <summary>
    /// 重复的键名称
    /// </summary>
    public string? KeyName { get; }

    /// <summary>
    /// 重复的键值
    /// </summary>
    public object? KeyValue { get; }

    /// <summary>
    /// 初始化一个<see cref="DuplicateKeyException"/>类型的新实例
    /// </summary>
    /// <param name="message">错误消息</param>
    public DuplicateKeyException(string message)
        : base(message)
    {
    }

    /// <summary>
    /// 初始化一个<see cref="DuplicateKeyException"/>类型的新实例
    /// </summary>
    /// <param name="entityTypeName">实体类型名称</param>
    /// <param name="keyName">重复的键名称</param>
    /// <param name="keyValue">重复的键值</param>
    public DuplicateKeyException(string entityTypeName, string keyName, object? keyValue)
        : base($"Duplicate key '{keyName}' with value '{keyValue}' already exists for entity '{entityTypeName}'.")
    {
        EntityTypeName = entityTypeName;
        KeyName = keyName;
        KeyValue = keyValue;
        this.WithData("EntityTypeName", entityTypeName)
            .WithData("KeyName", keyName);
        if (keyValue != null)
        {
            this.WithData("KeyValue", keyValue);
        }
    }

    /// <summary>
    /// 初始化一个<see cref="DuplicateKeyException"/>类型的新实例（泛型版本）
    /// </summary>
    /// <typeparam name="TEntity">实体类型</typeparam>
    /// <param name="keyName">重复的键名称</param>
    /// <param name="keyValue">重复的键值</param>
    public DuplicateKeyException<TEntity> ForEntity<TEntity>(string keyName, object? keyValue)
    {
        return new DuplicateKeyException<TEntity>(keyName, keyValue);
    }
}

/// <summary>
/// 重复键异常（泛型版本）
/// </summary>
/// <typeparam name="TEntity">实体类型</typeparam>
public class DuplicateKeyException<TEntity> : DuplicateKeyException
{
    /// <summary>
    /// 初始化一个<see cref="DuplicateKeyException{TEntity}"/>类型的新实例
    /// </summary>
    /// <param name="keyName">重复的键名称</param>
    /// <param name="keyValue">重复的键值</param>
    public DuplicateKeyException(string keyName, object? keyValue)
        : base(typeof(TEntity).Name, keyName, keyValue)
    {
    }
}

/// <summary>
/// 服务不可用异常
/// </summary>
public class ServiceUnavailableException : BusinessException
{
    public ServiceUnavailableException(string message = "Service temporarily unavailable.")
        : base(message, ErrorCodes.SERVICE_UNAVAILABLE, 503)
    {
    }
}

/// <summary>
/// 错误请求异常（400）
/// </summary>
public class BadRequestException : BusinessException
{
    public BadRequestException(string message)
        : base(message, ErrorCodes.BUSINESS_ERROR, 400)
    {
    }
}

/// <summary>
/// 请求限流异常（429）
/// </summary>
public class RateLimitException : BusinessException
{
    /// <summary>
    /// 重试间隔（秒）
    /// </summary>
    public int? RetryAfter { get; }

    /// <summary>
    /// 初始化一个<see cref="RateLimitException"/>类型的新实例
    /// </summary>
    /// <param name="message">异常消息</param>
    /// <param name="retryAfter">重试间隔（秒）</param>
    public RateLimitException(string message, int? retryAfter = null)
        : base(message, ErrorCodes.RATE_LIMIT_EXCEEDED, 429)
    {
        RetryAfter = retryAfter;
        if (retryAfter.HasValue)
        {
            this.WithData("RetryAfter", retryAfter.Value);
        }
    }
}

/// <summary>
/// 请求超时异常（504）
/// </summary>
public class RequestTimeoutException : BusinessException
{
    /// <summary>
    /// 超时时间（秒）
    /// </summary>
    public int? TimeoutSeconds { get; }

    /// <summary>
    /// 初始化一个<see cref="RequestTimeoutException"/>类型的新实例
    /// </summary>
    /// <param name="message">异常消息</param>
    /// <param name="timeoutSeconds">超时时间（秒）</param>
    public RequestTimeoutException(string message = "Request timeout.", int? timeoutSeconds = null)
        : base(message, ErrorCodes.SERVICE_TIMEOUT, 504)
    {
        TimeoutSeconds = timeoutSeconds;
        if (timeoutSeconds.HasValue)
        {
            this.WithData("TimeoutSeconds", timeoutSeconds.Value);
        }
    }
}

/// <summary>
/// 功能未实现异常（501）
/// </summary>
public class FeatureNotImplementedException : BusinessException
{
    /// <summary>
    /// 功能名称
    /// </summary>
    public string? FeatureName { get; }

    /// <summary>
    /// 初始化一个<see cref="FeatureNotImplementedException"/>类型的新实例
    /// </summary>
    /// <param name="message">异常消息</param>
    /// <param name="featureName">功能名称</param>
    public FeatureNotImplementedException(string message = "Feature not implemented.", string? featureName = null)
        : base(message, ErrorCodes.BUSINESS_ERROR, 501)
    {
        FeatureName = featureName;
        if (featureName != null)
        {
            this.WithData("FeatureName", featureName);
        }
    }
}
