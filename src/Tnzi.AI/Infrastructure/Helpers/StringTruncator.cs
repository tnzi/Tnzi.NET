namespace Tnzi.AI.Infrastructure.Helpers;

/// <summary>
/// 运行记录里的摘要截断：正文最多 <c>maxLength</c> 个 UTF-16 码元，被截时加 <c>...</c>；<c>null</c> 返回空串。
/// </summary>
/// <remarks>
/// 截断点落在代理对中间时整个字符一起舍掉（见核心的 <c>StringExtensions.Truncate</c>）：摘要落进运行表，
/// 孤立的高代理项会让严格 UTF-8 的驱动（Npgsql）拒绝这次写入。
/// </remarks>
public static class StringTruncator
{
    public static string Truncate(string? value, int maxLength)
        => string.IsNullOrEmpty(value) ? string.Empty : value.Truncate(maxLength);
}
