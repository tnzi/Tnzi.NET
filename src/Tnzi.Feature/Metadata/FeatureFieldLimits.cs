namespace Tnzi.Feature.Metadata;

/// <summary>
/// 功能定义与功能值的文本列宽：<b>实体配置与写入校验共用同一组常量</b>。
/// </summary>
/// <remarks>
/// 越界值在 SQL Server / PostgreSQL 上是一次 <c>DbUpdateException</c>，也就是给管理员一个 500；正确的答复是 400。
/// 测试夹具跑 SQLite，而 SQLite 不强制 <c>VARCHAR</c> 长度，测试挡不住这件事，所以常量由实体配置直接引用，
/// 「代码校验到的长度」与「列声明的长度」不可能各写一个数字后漂开。判定落在要落库的那个值上。
/// </remarks>
internal static class FeatureFieldLimits
{
    /// <summary>功能名（<c>FeatureDefinition.Name</c>）与父功能名（<c>ParentName</c>）。</summary>
    internal const int Name = 100;

    /// <summary>显示名（<c>FeatureDefinition.DisplayName</c>）。</summary>
    internal const int DisplayName = 200;

    /// <summary>描述（<c>FeatureDefinition.Description</c>）。</summary>
    internal const int Description = 500;

    /// <summary>功能值（<c>FeatureValue.Value</c>）与定义的默认值（<c>FeatureDefinition.DefaultValue</c>）。</summary>
    internal const int Value = 200;

    /// <summary>分组（<c>FeatureDefinition.Group</c>）。</summary>
    internal const int Group = 50;

    /// <summary>作用域名与作用域键（<c>FeatureValue.ProviderName</c> / <c>ProviderKey</c>）。</summary>
    internal const int ProviderKey = 64;

    /// <summary>
    /// 越界时给出一条面向调用方的说明，合法（含 null）时返回 <see langword="null"/>。
    /// </summary>
    /// <param name="value">即将落库的那个值。</param>
    /// <param name="max">列宽。</param>
    /// <param name="field">字段的英文名，放进消息里。</param>
    internal static string? Exceeded(string? value, int max, string field)
        => value != null && value.Length > max
            ? $"{field} must be at most {max} characters (got {value.Length})."
            : null;
}
