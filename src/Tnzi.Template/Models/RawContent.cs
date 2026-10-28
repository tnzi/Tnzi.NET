namespace Tnzi.Template.Models;

/// <summary>
/// 原始内容包装器（避免 HTML 编码）
/// </summary>
public class RawContent
{
    private readonly string _value;

    public RawContent(string value)
    {
        _value = value;
    }

    public override string ToString()
    {
        return _value;
    }

    /// <summary>
    /// 当字符串用（拼接、赋给 <c>string</c>、传给收 <c>string</c> 的助手）。转出来的是普通字符串，
    /// 直接写出时按普通值处理：HTML 输出下会被编码。
    /// </summary>
    public static implicit operator string(RawContent? content) => content?._value ?? string.Empty;
}
