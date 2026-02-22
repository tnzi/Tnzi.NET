
namespace Tnzi.Extensions;

/// <summary>
/// 字符串扩展方法
/// </summary>
public static class StringExtensions
{
    #region 空值检查

    public static bool IsNotNullOrEmpty(this string? str)
    {
        return !string.IsNullOrEmpty(str);
    }

    public static bool IsNotNullOrWhiteSpace(this string? str)
    {
        return !string.IsNullOrWhiteSpace(str);
    }

    public static bool IsNullOrEmpty(this string? str)
    {
        return string.IsNullOrEmpty(str);
    }

    public static bool IsNullOrWhiteSpace(this string? str)
    {
        return string.IsNullOrWhiteSpace(str);
    }

    #endregion

    #region 哈希算法

    /// <summary>
    /// 计算字符串的MD5哈希值
    /// </summary>
    public static string ToMd5(this string str)
    {
        if (string.IsNullOrEmpty(str))
            return string.Empty;

        using var md5 = MD5.Create();
        var inputBytes = Encoding.UTF8.GetBytes(str);
        var hashBytes = md5.ComputeHash(inputBytes);
        return Convert.ToHexString(hashBytes).ToLowerInvariant();
    }

    /// <summary>
    /// 计算字符串的SHA256哈希值
    /// </summary>
    public static string ToSha256(this string str)
    {
        if (string.IsNullOrEmpty(str))
            return string.Empty;

        using var sha256 = SHA256.Create();
        var inputBytes = Encoding.UTF8.GetBytes(str);
        var hashBytes = sha256.ComputeHash(inputBytes);
        return Convert.ToHexString(hashBytes).ToLowerInvariant();
    }

    /// <summary>
    /// 计算字符串的SHA512哈希值
    /// </summary>
    public static string ToSha512(this string str)
    {
        if (string.IsNullOrEmpty(str))
            return string.Empty;

        using var sha512 = SHA512.Create();
        var inputBytes = Encoding.UTF8.GetBytes(str);
        var hashBytes = sha512.ComputeHash(inputBytes);
        return Convert.ToHexString(hashBytes).ToLowerInvariant();
    }

    #endregion

    #region 字符串处理

    /// <summary>
    /// 移除字符串开头的指定字符
    /// </summary>
    public static string TrimStart(this string str, char trimChar)
    {
        if (string.IsNullOrEmpty(str))
            return str;

        int startIndex = 0;
        while (startIndex < str.Length && str[startIndex] == trimChar)
        {
            startIndex++;
        }

        return startIndex > 0 ? str[startIndex..] : str;
    }

    /// <summary>
    /// 移除字符串结尾的指定字符
    /// </summary>
    public static string TrimEnd(this string str, char trimChar)
    {
        if (string.IsNullOrEmpty(str))
            return str;

        int endIndex = str.Length - 1;
        while (endIndex >= 0 && str[endIndex] == trimChar)
        {
            endIndex--;
        }

        return endIndex < str.Length - 1 ? str[..(endIndex + 1)] : str;
    }

    /// <summary>
    /// 转换为驼峰命名（首字母小写）
    /// </summary>
    public static string ToCamelCase(this string str)
    {
        if (string.IsNullOrEmpty(str))
            return str;

        if (str.Length == 1)
            return str.ToLowerInvariant();

        return char.ToLowerInvariant(str[0]) + str[1..];
    }

    /// <summary>
    /// 转换为帕斯卡命名（首字母大写）
    /// </summary>
    public static string ToPascalCase(this string str)
    {
        if (string.IsNullOrEmpty(str))
            return str;

        if (str.Length == 1)
            return str.ToUpperInvariant();

        return char.ToUpperInvariant(str[0]) + str[1..];
    }

    /// <summary>
    /// 转换为蛇形命名（snake_case）
    /// </summary>
    public static string ToSnakeCase(this string str)
    {
        if (string.IsNullOrEmpty(str))
            return str;

        var result = new StringBuilder();
        for (int i = 0; i < str.Length; i++)
        {
            if (char.IsUpper(str[i]) && i > 0)
            {
                result.Append('_');
            }
            result.Append(char.ToLowerInvariant(str[i]));
        }

        return result.ToString();
    }

    /// <summary>
    /// 简单的复数化（仅支持英文基础规则）
    /// </summary>
    public static string ToPlural(this string input)
    {
        if (string.IsNullOrEmpty(input))
            return input;

        // 如果已经是复数（简单判断：以s结尾，但排除ss和us）
        if (input.EndsWith("s", StringComparison.OrdinalIgnoreCase) && 
            !input.EndsWith("ss", StringComparison.OrdinalIgnoreCase) &&
            !input.EndsWith("us", StringComparison.OrdinalIgnoreCase))
        {
            return input;
        }

        if (input.EndsWith("y", StringComparison.OrdinalIgnoreCase) &&
            !input.EndsWith("ay", StringComparison.OrdinalIgnoreCase) &&
            !input.EndsWith("ey", StringComparison.OrdinalIgnoreCase) &&
            !input.EndsWith("iy", StringComparison.OrdinalIgnoreCase) &&
            !input.EndsWith("oy", StringComparison.OrdinalIgnoreCase) &&
            !input.EndsWith("uy", StringComparison.OrdinalIgnoreCase))
        {
            return input[..^1] + "ies";
        }

        if (input.EndsWith("s", StringComparison.OrdinalIgnoreCase) ||
            input.EndsWith("x", StringComparison.OrdinalIgnoreCase) ||
            input.EndsWith("z", StringComparison.OrdinalIgnoreCase) ||
            input.EndsWith("ch", StringComparison.OrdinalIgnoreCase) ||
            input.EndsWith("sh", StringComparison.OrdinalIgnoreCase))
        {
            return input + "es";
        }

        return input + "s";
    }

    /// <summary>
    /// 截断字符串到指定长度
    /// </summary>
    public static string Truncate(this string str, int maxLength, string suffix = "...")
    {
        if (string.IsNullOrEmpty(str) || str.Length <= maxLength)
            return str;

        return str[..maxLength] + suffix;
    }

    /// <summary>
    /// 移除HTML标签
    /// </summary>
    public static string RemoveHtmlTags(this string str)
    {
        if (string.IsNullOrEmpty(str))
            return str;

        return Regex.Replace(str, "<.*?>", string.Empty);
    }

    #endregion

    #region 验证

    /// <summary>
    /// 验证是否为有效的邮箱地址
    /// </summary>
    public static bool IsEmail(this string str)
    {
        if (string.IsNullOrEmpty(str))
            return false;

        const string pattern = @"^[a-zA-Z0-9._%+-]+@[a-zA-Z0-9.-]+\.[a-zA-Z]{2,}$";
        return Regex.IsMatch(str, pattern);
    }


    /// <summary>
    /// 是否手机号码
    /// </summary>
    /// <param name="value"></param>
    /// <param name="isRestrict">是否按严格格式验证</param>
    public static bool IsPhoneNumber(this string value, bool isRestrict = true)
    {
        if (value.IsNullOrEmpty()) return false;

        string pattern = isRestrict
        ? @"^(?:(?:\+1\s*(?:[.\-\s]*)?)?(?:\(?([2-9][0-9]{2})\)?|\s*([2-9][0-9]{2})\s*)(?:[.\-\s]*)?)?([2-9][0-9]{2})(?:[.\-\s]*)?([0-9]{4})(?:\s*(?:#|x|ext|extension)\s*(\d+))?$"
        : @"^[1]\d{10}$";
        return Regex.IsMatch(value, pattern);
    }

    /// <summary>
    /// 是否Url字符串
    /// </summary>
    public static bool IsUrl(this string value)
    {
        return Uri.TryCreate(value, UriKind.Absolute, out var uri) &&(uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps);
    }

    /// <summary>
    /// 验证是否匹配正则表达式
    /// </summary>
    public static bool IsMatch(this string str, string pattern)
    {
        if (string.IsNullOrEmpty(str) || string.IsNullOrEmpty(pattern))
            return false;

        return Regex.IsMatch(str, pattern);
    }

    #endregion

    #region 正则表达式

    /// <summary>
    /// 匹配第一个结果
    /// </summary>
    public static string? Match(this string str, string pattern)
    {
        if (string.IsNullOrEmpty(str) || string.IsNullOrEmpty(pattern))
            return null;

        var match = Regex.Match(str, pattern);
        return match.Success ? match.Value : null;
    }

    /// <summary>
    /// 匹配所有结果
    /// </summary>
    public static IEnumerable<string> Matches(this string str, string pattern)
    {
        if (string.IsNullOrEmpty(str) || string.IsNullOrEmpty(pattern))
            return Array.Empty<string>();

        return Regex.Matches(str, pattern).Cast<Match>().Select(m => m.Value);
    }

    /// <summary>
    /// 替换匹配的字符串
    /// </summary>
    public static string ReplaceRegex(this string str, string pattern, string replacement)
    {
        if (string.IsNullOrEmpty(str) || string.IsNullOrEmpty(pattern))
            return str;

        return Regex.Replace(str, pattern, replacement);
    }

    /// <summary>
    /// 将字符串转换为字节数组
    /// </summary>
    public static byte[] ToBytes(this string value, Encoding? encoding = null)
    {
        if (string.IsNullOrEmpty(value))
            return Array.Empty<byte>();

        encoding ??= Encoding.UTF8;
        return encoding.GetBytes(value);
    }

    /// <summary>
    /// 将字节数组按指定编码转换为字符串
    /// </summary>
    /// <param name="bytes">字节数组</param>
    /// <param name="encoding">编码方式，默认为 UTF-8</param>
    /// <returns>转换后的字符串</returns>
    public static string ToEncodingString(this byte[] bytes, Encoding? encoding = null)
    {
        if (bytes == null || bytes.Length == 0)
            return string.Empty;

        encoding ??= Encoding.UTF8;
        return encoding.GetString(bytes);
    }

    #endregion
}