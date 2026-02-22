
namespace Tnzi.EFCore.Dapper.Internal;

/// <summary>
/// SQL 标识符验证辅助类
/// 用于防止 SQL 注入攻击
/// </summary>
public static partial class SqlIdentifierHelper
{
    // 有效的 SQL 标识符正则表达式：字母或下划线开头，后跟字母、数字或下划线
    // 支持 Unicode 字母
    [GeneratedRegex(@"^[\p{L}_][\p{L}\p{N}_]*$", RegexOptions.Compiled)]
    private static partial Regex ValidIdentifierRegex();

    /// <summary>
    /// 验证 SQL 标识符是否有效（防止 SQL 注入）
    /// </summary>
    /// <param name="identifier">要验证的标识符</param>
    /// <returns>如果有效返回 true</returns>
    public static bool IsValidIdentifier(string? identifier)
    {
        if (string.IsNullOrWhiteSpace(identifier))
            return false;

        // 长度限制（大多数数据库的标识符最大长度是 128）
        if (identifier.Length > 128)
            return false;

        // 检查是否匹配有效标识符模式
        return ValidIdentifierRegex().IsMatch(identifier);
    }

    /// <summary>
    /// 验证 Schema 限定标识符是否有效（支持 schema.table 格式）
    /// </summary>
    /// <param name="qualifiedIdentifier">限定标识符（如 "public.users" 或 "users"）</param>
    /// <returns>如果有效返回 true</returns>
    public static bool IsValidQualifiedIdentifier(string? qualifiedIdentifier)
    {
        if (string.IsNullOrWhiteSpace(qualifiedIdentifier))
            return false;

        // 整体长度限制（schema.table 合计不超过 260）
        if (qualifiedIdentifier.Length > 260)
            return false;

        var dotIndex = qualifiedIdentifier.IndexOf('.');
        if (dotIndex < 0)
        {
            // 无 Schema，按普通标识符验证
            return IsValidIdentifier(qualifiedIdentifier);
        }

        // 分别验证 Schema 和 Table 部分
        var schema = qualifiedIdentifier[..dotIndex];
        var table = qualifiedIdentifier[(dotIndex + 1)..];
        return IsValidIdentifier(schema) && IsValidIdentifier(table);
    }

    /// <summary>
    /// 验证标识符，如果无效则抛出异常
    /// </summary>
    /// <param name="identifier">要验证的标识符</param>
    /// <param name="paramName">参数名（用于异常消息）</param>
    /// <exception cref="ArgumentException">如果标识符无效</exception>
    public static void ThrowIfInvalidIdentifier(string? identifier, string paramName = "identifier")
    {
        if (!IsValidIdentifier(identifier))
        {
            throw new ArgumentException(
                $"Invalid SQL identifier: '{identifier}'. Identifiers must start with a letter or underscore, " +
                "contain only letters, numbers, or underscores, and be no longer than 128 characters.",
                paramName);
        }
    }

    /// <summary>
    /// 验证 Schema 限定标识符，如果无效则抛出异常
    /// </summary>
    /// <param name="qualifiedIdentifier">限定标识符（如 "public.users" 或 "users"）</param>
    /// <param name="paramName">参数名（用于异常消息）</param>
    /// <exception cref="ArgumentException">如果标识符无效</exception>
    public static void ThrowIfInvalidQualifiedIdentifier(string? qualifiedIdentifier, string paramName = "identifier")
    {
        if (!IsValidQualifiedIdentifier(qualifiedIdentifier))
        {
            throw new ArgumentException(
                $"Invalid SQL qualified identifier: '{qualifiedIdentifier}'. " +
                "Qualified identifiers must be in 'name' or 'schema.name' format, " +
                "where each part starts with a letter or underscore and contains only letters, numbers, or underscores.",
                paramName);
        }
    }

    /// <summary>
    /// 使用 Provider 转义 Schema 限定标识符
    /// 对 "schema.table" 格式，分别转义 schema 和 table 部分
    /// </summary>
    /// <param name="qualifiedIdentifier">限定标识符</param>
    /// <param name="provider">数据库提供者（用于转义）</param>
    /// <returns>转义后的标识符（如 "public"."users" 或 [users]）</returns>
    public static string EscapeQualifiedIdentifier(string qualifiedIdentifier, Providers.IDatabaseProvider provider)
    {
        Check.NotNull(provider);
        Check.NotNullOrWhiteSpace(qualifiedIdentifier);

        var dotIndex = qualifiedIdentifier.IndexOf('.');
        if (dotIndex < 0)
        {
            return provider.EscapeIdentifier(qualifiedIdentifier);
        }

        var schema = qualifiedIdentifier[..dotIndex];
        var table = qualifiedIdentifier[(dotIndex + 1)..];
        return $"{provider.EscapeIdentifier(schema)}.{provider.EscapeIdentifier(table)}";
    }

    /// <summary>
    /// 清理标识符（移除潜在危险字符）
    /// </summary>
    /// <param name="identifier">原始标识符</param>
    /// <returns>清理后的标识符</returns>
    public static string SanitizeIdentifier(string identifier)
    {
        if (string.IsNullOrEmpty(identifier))
            return identifier;

        // 仅保留字母、数字和下划线
        return SanitizeRegex().Replace(identifier, "");
    }

    [GeneratedRegex(@"[^\p{L}\p{N}_]", RegexOptions.Compiled)]
    private static partial Regex SanitizeRegex();
}
