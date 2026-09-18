// 名字冲突消歧：System.ComponentModel.DataAnnotations 也有一个 ValidationException
using ValidationException = Tnzi.Exceptions.ValidationException;

namespace Tnzi.Data.Filtering;

/// <summary>
/// 请求来源的过滤字段不可用（不存在、不是标量、或被 <see cref="FilterFieldPolicy"/> 拒绝）。
/// HTTP 400；消息只含字段路径，刻意不含实体类型名，也不区分「不存在」与「不允许」。
/// </summary>
[StableApi(Since = "0.1.30")]
public class FilterFieldException : ValidationException
{
    /// <summary>
    /// 被拒绝的字段路径（调用方原样给的）
    /// </summary>
    public string Field { get; }

    public FilterFieldException(string field)
        : base($"Filter field '{field}' is not filterable.", new Dictionary<string, string[]> { [field] = ["is not filterable"] })
    {
        Field = field;
    }
}
