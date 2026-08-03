namespace Tnzi.Utilities;

/// <summary>
/// 常用正则表达式字符串（**提取**用，不是校验用）
/// </summary>
/// <remarks>
/// ★这里的模式一律**不带锚点**（无 <c>^</c>/<c>$</c>），用途是从一段自由文本里**找出**目标片段，
/// 配合 <c>StringExtensions.MatchAll(pattern)</c> / <c>StringExtensions.ReplaceRegex(...)</c> 使用。
///
/// ⚠️ **不要拿它们做输入校验**：<c>Regex.IsMatch("随便什么 a@b.com 后面还有一堆", Email)</c> 为 true
/// —— 只要文本里**含有**一个邮箱就命中。校验请走带锚点的专用入口：
/// <list type="bullet">
/// <item>邮箱 → <c>StringExtensions.IsValidEmail()</c> 或 <c>[Email]</c> 特性</item>
/// <item>手机号 → <c>StringExtensions.IsValidPhoneNumber()</c> 或 <c>[Phone]</c> 特性</item>
/// <item>用户名 / 口令 → <c>[Username]</c> / <c>[Password]</c> 特性</item>
/// </list>
/// </remarks>
public static class RegexPatterns
{
    /// <summary>
    /// IP的匹配字符串（提取用，无锚点）
    /// </summary>
    public const string Ip = @"((?:(?:25[0-5]|2[0-4]\d|((1\d{2})|([1-9]?\d)))\.){3}(?:25[0-5]|2[0-4]\d|((1\d{2})|([1-9]?\d))))";

    /// <summary>
    /// 前后断言的字符串截取匹配格式串。用
    /// <c>string.Format(SubstringFormat, before, after)</c> 组装出「取 before 与 after 之间内容」的模式。
    /// </summary>
    public const string SubstringFormat = "(?<=({0})).+(?=({1}))";

    /// <summary>
    /// 邮箱的匹配字符串（提取用，无锚点；校验请用 <c>IsValidEmail()</c>）
    /// </summary>
    public const string Email = @"[\w-]+(\.[\w-]+)*@[\w-]+(\.[\w-]+)+";

    /// <summary>
    /// Unicode（中文）字符的匹配字符串
    /// </summary>
    public const string Unicode = @"[\u4E00-\u9FA5\uE815-\uFA29]+";

    /// <summary>
    /// URL的匹配字符串（提取用，无锚点）
    /// </summary>
    public const string Url = @"(http|https|ftp|rtsp|mms):(\/\/|\\\\)[A-Za-z0-9%\-_@]+\.[A-Za-z0-9%\-_@]+[A-Za-z0-9\.\/=\?%\-&_~`@:\+!;]*";

  
}

