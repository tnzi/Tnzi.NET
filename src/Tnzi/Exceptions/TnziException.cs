
namespace Tnzi.Exceptions;

/// <summary>
/// Tnzi框架异常基类
/// 所有业务异常应继承自 BusinessException 而非此类
/// 此类仅用于框架内部错误
/// </summary>
public class TnziException : Exception
{
    /// <summary>
    /// 错误代码
    /// </summary>
    public string Code { get; }

    /// <summary>
    /// 异常数据（上下文信息）
    /// </summary>
    public Dictionary<string, object>? ContextData { get; set; }

    /// <summary>
    /// 初始化一个<see cref="TnziException"/>类型的新实例
    /// </summary>
    public TnziException() : this(ErrorCodes.UNKNOWN_ERROR, "An unknown error occurred.")
    {
    }

    /// <summary>
    /// 初始化一个<see cref="TnziException"/>类型的新实例
    /// </summary>
    /// <param name="message">异常消息</param>
    public TnziException(string message) : this(ErrorCodes.UNKNOWN_ERROR, message)
    {
    }

    /// <summary>
    /// 初始化一个<see cref="TnziException"/>类型的新实例
    /// </summary>
    /// <param name="code">错误代码</param>
    /// <param name="message">异常消息</param>
    /// <param name="innerException">内部异常</param>
    public TnziException(string code, string message, Exception? innerException = null)
        : base(message, innerException)
    {
        Code = code;
    }

    /// <summary>
    /// 初始化一个<see cref="TnziException"/>类型的新实例
    /// </summary>
    /// <param name="message">异常消息</param>
    /// <param name="innerException">内部异常</param>
    public TnziException(string message, Exception innerException)
        : this(ErrorCodes.UNKNOWN_ERROR, message, innerException)
    {
    }

    /// <summary>
    /// 添加上下文数据
    /// </summary>
    /// <param name="key">键</param>
    /// <param name="value">值</param>
    /// <returns>当前异常实例（支持链式调用）</returns>
    public TnziException WithData(string key, object value)
    {
        ContextData ??= new Dictionary<string, object>();
        ContextData[key] = value;
        return this;
    }
}

/// <summary>
/// 验证错误数据结构
/// 用于表示单个验证错误
/// </summary>
public class ValidationError
{
    /// <summary>
    /// 属性名称
    /// </summary>
    public string PropertyName { get; }

    /// <summary>
    /// 错误消息
    /// </summary>
    public string ErrorMessage { get; }

    /// <summary>
    /// 尝试的值
    /// </summary>
    public object? AttemptedValue { get; }

    /// <summary>
    /// 初始化验证错误
    /// </summary>
    /// <param name="propertyName">属性名称</param>
    /// <param name="errorMessage">错误消息</param>
    /// <param name="attemptedValue">尝试的值</param>
    public ValidationError(string propertyName, string errorMessage, object? attemptedValue = null)
    {
        PropertyName = propertyName;
        ErrorMessage = errorMessage;
        AttemptedValue = attemptedValue;
    }
}