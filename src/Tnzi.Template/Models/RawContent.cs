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
}
