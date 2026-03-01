
namespace Tnzi.Utilities;

/// <summary>
/// 参数检查工具类
/// </summary>
[DebuggerStepThrough]
[StableApi(Since = "0.1.0")]
public static class Check
{
    private const string FallbackParamName = "value";

    /// <summary>
    /// 检查参数是否为 null
    /// </summary>
    /// <typeparam name="T">参数类型</typeparam>
    /// <param name="value">参数值</param>
    /// <param name="paramName">参数名（可选，不提供时由编译器从调用处推导）</param>
    /// <returns>参数值</returns>
    public static T NotNull<T>([System.Diagnostics.CodeAnalysis.NotNull] T value,[CallerArgumentExpression(nameof(value))] string? paramName = null)
    {
        if (value == null)
            throw new ArgumentNullException(paramName ?? FallbackParamName);
        return value;
    }

    /// <summary>
    /// 检查字符串是否为 null 或空字符串
    /// </summary>
    /// <param name="value">字符串值</param>
    /// <param name="paramName">参数名（可选，不提供时由编译器从调用处推导）</param>
    /// <returns>字符串值</returns>
    public static string NotNullOrEmpty(string value, [CallerArgumentExpression(nameof(value))] string? paramName = null)
    {
        if (string.IsNullOrEmpty(value))
            throw new ArgumentException($"{paramName ?? FallbackParamName} can not be null or empty!", paramName ?? FallbackParamName);
        return value;
    }

    /// <summary>
    /// 检查字符串是否为 null 或空白字符串
    /// </summary>
    /// <param name="value">字符串值</param>
    /// <param name="paramName">参数名（可选，不提供时由编译器从调用处推导）</param>
    /// <returns>字符串值</returns>
    public static string NotNullOrWhiteSpace(string value, [CallerArgumentExpression(nameof(value))] string? paramName = null)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new ArgumentException($"{paramName ?? FallbackParamName} can not be null or white space!", paramName ?? FallbackParamName);
        return value;
    }

    /// <summary>
    /// 检查 Guid 是否为默认值（空 Guid）
    /// </summary>
    /// <param name="value">Guid 值</param>
    /// <param name="paramName">参数名（可选，不提供时由编译器从调用处推导）</param>
    /// <returns>Guid 值</returns>
    public static Guid NotDefault(Guid value, [CallerArgumentExpression(nameof(value))] string? paramName = null)
    {
        if (value == default)
            throw new ArgumentException($"{paramName ?? FallbackParamName} can not be default (empty) Guid!", paramName ?? FallbackParamName);
        return value;
    }

    /// <summary>
    /// 检查集合是否为 null 或空
    /// </summary>
    /// <typeparam name="T">集合元素类型</typeparam>
    /// <param name="value">集合值</param>
    /// <param name="paramName">参数名（可选，不提供时由编译器从调用处推导）</param>
    /// <returns>集合值</returns>
    public static IEnumerable<T> NotNullOrEmpty<T>(IEnumerable<T> value, [CallerArgumentExpression(nameof(value))] string? paramName = null)
    {
        if (value == null)
            throw new ArgumentNullException(paramName ?? FallbackParamName);
        if (!value.Any())
            throw new ArgumentException($"{paramName ?? FallbackParamName} can not be empty!", paramName ?? FallbackParamName);
        return value;
    }

    /// <summary>
    /// 检查数值是否在指定范围内
    /// </summary>
    /// <typeparam name="T">数值类型</typeparam>
    /// <param name="value">数值</param>
    /// <param name="min">最小值（包含）</param>
    /// <param name="max">最大值（包含）</param>
    /// <param name="paramName">参数名（可选，不提供时由编译器从调用处推导）</param>
    /// <returns>数值</returns>
    public static T InRange<T>(T value, T min, T max, [CallerArgumentExpression(nameof(value))] string? paramName = null) where T : IComparable<T>
    {
        if (value.CompareTo(min) < 0 || value.CompareTo(max) > 0)
            throw new ArgumentOutOfRangeException(paramName ?? FallbackParamName, value, $"{paramName ?? FallbackParamName} must be between {min} and {max}!");
        return value;
    }

    /// <summary>
    /// 检查数值是否大于指定值
    /// </summary>
    /// <typeparam name="T">数值类型</typeparam>
    /// <param name="value">数值</param>
    /// <param name="min">最小值（不包含）</param>
    /// <param name="paramName">参数名（可选，不提供时由编译器从调用处推导）</param>
    /// <returns>数值</returns>
    public static T GreaterThan<T>(T value, T min, [CallerArgumentExpression(nameof(value))] string? paramName = null) where T : IComparable<T>
    {
        if (value.CompareTo(min) <= 0)
            throw new ArgumentOutOfRangeException(paramName ?? FallbackParamName, value, $"{paramName ?? FallbackParamName} must be greater than {min}!");
        return value;
    }

    /// <summary>
    /// 检查数值是否大于或等于指定值
    /// </summary>
    /// <typeparam name="T">数值类型</typeparam>
    /// <param name="value">数值</param>
    /// <param name="min">最小值（包含）</param>
    /// <param name="paramName">参数名（可选，不提供时由编译器从调用处推导）</param>
    /// <returns>数值</returns>
    public static T GreaterThanOrEqual<T>(T value, T min, [CallerArgumentExpression(nameof(value))] string? paramName = null) where T : IComparable<T>
    {
        if (value.CompareTo(min) < 0)
            throw new ArgumentOutOfRangeException(paramName ?? FallbackParamName, value, $"{paramName ?? FallbackParamName} must be greater than or equal to {min}!");
        return value;
    }

    /// <summary>
    /// 检查数值是否小于指定值
    /// </summary>
    /// <typeparam name="T">数值类型</typeparam>
    /// <param name="value">数值</param>
    /// <param name="max">最大值（不包含）</param>
    /// <param name="paramName">参数名（可选，不提供时由编译器从调用处推导）</param>
    /// <returns>数值</returns>
    public static T LessThan<T>(T value, T max, [CallerArgumentExpression(nameof(value))] string? paramName = null) where T : IComparable<T>
    {
        if (value.CompareTo(max) >= 0)
            throw new ArgumentOutOfRangeException(paramName ?? FallbackParamName, value, $"{paramName ?? FallbackParamName} must be less than {max}!");
        return value;
    }

    /// <summary>
    /// 检查数值是否小于或等于指定值
    /// </summary>
    /// <typeparam name="T">数值类型</typeparam>
    /// <param name="value">数值</param>
    /// <param name="max">最大值（包含）</param>
    /// <param name="paramName">参数名（可选，不提供时由编译器从调用处推导）</param>
    /// <returns>数值</returns>
    public static T LessThanOrEqual<T>(T value, T max, [CallerArgumentExpression(nameof(value))] string? paramName = null) where T : IComparable<T>
    {
        if (value.CompareTo(max) > 0)
            throw new ArgumentOutOfRangeException(paramName ?? FallbackParamName, value, $"{paramName ?? FallbackParamName} must be less than or equal to {max}!");
        return value;
    }

    /// <summary>
    /// 通用条件检查，条件不满足时抛出 ArgumentException
    /// </summary>
    /// <param name="condition">需要满足的条件</param>
    /// <param name="message">条件不满足时的错误消息</param>
    public static void Condition(bool condition, string message)
    {
        if (!condition)
            throw new ArgumentException(message);
    }

    /// <summary>
    /// 检查字符串长度不超过最大值
    /// </summary>
    public static string MaxLength(string value, int maxLength, [CallerArgumentExpression(nameof(value))] string? paramName = null)
    {
        NotNull(value, paramName);
        if (value.Length > maxLength)
            throw new ArgumentException($"{paramName ?? FallbackParamName} must not exceed {maxLength} characters! Current length: {value.Length}.", paramName ?? FallbackParamName);
        return value;
    }

    /// <summary>
    /// 检查字符串长度不小于最小值
    /// </summary>
    public static string MinLength(string value, int minLength, [CallerArgumentExpression(nameof(value))] string? paramName = null)
    {
        NotNull(value, paramName);
        if (value.Length < minLength)
            throw new ArgumentException($"{paramName ?? FallbackParamName} must be at least {minLength} characters! Current length: {value.Length}.", paramName ?? FallbackParamName);
        return value;
    }

    /// <summary>
    /// 检查字符串长度在指定范围内
    /// </summary>
    public static string Length(string value, int minLength, int maxLength, [CallerArgumentExpression(nameof(value))] string? paramName = null)
    {
        NotNull(value, paramName);
        if (value.Length < minLength || value.Length > maxLength)
            throw new ArgumentException($"{paramName ?? FallbackParamName} must be between {minLength} and {maxLength} characters! Current length: {value.Length}.", paramName ?? FallbackParamName);
        return value;
    }

    /// <summary>
    /// 检查字符串是否匹配正则表达式
    /// </summary>
    public static string Matches(string value, [System.Diagnostics.CodeAnalysis.StringSyntax("Regex")] string pattern, [CallerArgumentExpression(nameof(value))] string? paramName = null)
    {
        NotNull(value, paramName);
        if (!Regex.IsMatch(value, pattern))
            throw new ArgumentException($"{paramName ?? FallbackParamName} does not match the required pattern.", paramName ?? FallbackParamName);
        return value;
    }

    /// <summary>
    /// 检查数值是否为正数（大于 0）
    /// </summary>
    public static T Positive<T>(T value, [CallerArgumentExpression(nameof(value))] string? paramName = null) where T : IComparable<T>
    {
        if (value.CompareTo(default!) <= 0)
            throw new ArgumentOutOfRangeException(paramName ?? FallbackParamName, value, $"{paramName ?? FallbackParamName} must be positive!");
        return value;
    }

    /// <summary>
    /// 检查 Guid 是否不为空
    /// </summary>
    public static Guid NotEmpty(Guid value, [CallerArgumentExpression(nameof(value))] string? paramName = null)
    {
        if (value == Guid.Empty)
            throw new ArgumentException($"{paramName ?? FallbackParamName} can not be empty Guid!", paramName ?? FallbackParamName);
        return value;
    }
}