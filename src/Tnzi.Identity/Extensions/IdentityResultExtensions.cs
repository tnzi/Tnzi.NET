namespace Tnzi.Identity.Extensions;

/// <summary>
/// IdentityResult扩展方法
/// </summary>
public static class IdentityResultExtensions
{
    /// <summary>
    /// 格式化 IdentityResult 的错误消息
    /// </summary>
    /// <param name="result">IdentityResult 实例</param>
    /// <param name="separator">错误消息分隔符，默认为 ", "</param>
    /// <param name="includeCode">是否包含错误代码，默认为 false</param>
    /// <returns>格式化后的错误消息字符串</returns>
    public static string FormatErrors(this IdentityResult result, string separator = ", ", bool includeCode = false)
    {
        Check.NotNull(result);

        if (result.Succeeded || result.Errors == null || !result.Errors.Any())
            return string.Empty;

        if (includeCode)
        {
            return string.Join(separator, result.Errors.Select(e => $"{e.Code}: {e.Description}"));
        }

        return string.Join(separator, result.Errors.Select(e => e.Description));
    }
}
