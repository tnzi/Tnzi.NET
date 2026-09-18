// 名字冲突消歧：System.ComponentModel.DataAnnotations 也有一个 ValidationException
using ValidationException = Tnzi.Exceptions.ValidationException;

namespace Tnzi.Data.Filtering;

/// <summary>
/// 请求来源的过滤规则的值或操作符不能用在该字段上（值解析不了、操作符不认识、In 给的不是集合、
/// 字符串操作符落在非字符串列上、null 给了非空值类型）。HTTP 400；消息只含字段路径与操作符，
/// 刻意不含 CLR 类型名 —— 这条异常会原样到达请求方；原始原因放在 <see cref="Cause"/> 与
/// <see cref="TnziException.ContextData"/>（键 <c>cause</c>）里供日志与排障，不进响应。
/// </summary>
[StableApi(Since = "0.1.30")]
public class FilterValueException : ValidationException
{
    /// <summary>
    /// 出错规则的字段路径（调用方原样给的）
    /// </summary>
    public string Field { get; }

    /// <summary>
    /// 出错规则的操作符
    /// </summary>
    public FilterOperator Operator { get; }

    /// <summary>
    /// 原始失败（解析异常、类型不匹配等）
    /// </summary>
    public Exception Cause { get; }

    public FilterValueException(string field, FilterOperator @operator, Exception cause)
        : base(
            $"Filter value for field '{field}' is not valid for operator '{@operator}'.",
            new Dictionary<string, string[]> { [field] = [$"value is not valid for operator '{@operator}'"] })
    {
        Field = field;
        Operator = @operator;
        Cause = Check.NotNull(cause);
        WithData("cause", cause.Message);
    }
}
