
namespace Tnzi.AspNetCore.Http;

/// <summary>
/// 请求标识的字形约束。
/// </summary>
/// <remarks>
/// ★★★ <strong>请求标识可以由调用方给定，而它会被<b>原样写回响应头</b>。</strong>
/// 客户端带 <c>X-Request-Id</c> 是为了把自己的调用链和服务端日志对上，这个约定本身没问题；
/// 问题是此前既不限长度也不限字符 —— 带 <c>%0d%0a</c> 的值会让 Kestrel 在写头时抛异常
/// （任何人都能让任意一个请求变成 500），而一段任意长的值会跟着每条日志与每个响应走。
/// 校验不过就当作没给：服务端自己生成一个，追踪照常，只是链路对不上。
/// </remarks>
internal static class RequestIdentifier
{
    /// <summary>
    /// 允许的字形：可见 ASCII 里的字母数字与 <c>. _ - :</c>，最长 128 字符。
    /// </summary>
    /// <remarks>
    /// 白名单而不是黑名单：常见的标识格式（GUID 的 N 形、W3C traceparent 的片段、
    /// 各家 APM 的 <c>&lt;service&gt;:&lt;span&gt;</c>）都落在这个集合里，
    /// 而「哪些字符是危险的」永远会少写一个。
    /// </remarks>
    private static readonly Regex WellFormed = new(
        @"^[A-Za-z0-9._:-]{1,128}$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant,
        TimeSpan.FromSeconds(1));

    /// <summary>这个标识是否可以安全地进日志与响应头。</summary>
    internal static bool IsWellFormed(string? value)
        => !string.IsNullOrEmpty(value) && WellFormed.IsMatch(value);

    /// <summary>取校验通过的标识，否则返回 <c>null</c>。</summary>
    internal static string? Accept(string? value)
        => IsWellFormed(value) ? value : null;
}
