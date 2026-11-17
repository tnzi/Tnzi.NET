namespace Tnzi.AI.Services;

/// <summary>
/// 线程来源（<see cref="AgentThread.Origin"/>）的形状。
/// </summary>
/// <remarks>
/// <para>
/// 来源是应用自己定义的一个短值，说明线程是怎么开始的（邮件规则、定时任务、某张卡片……），建线程时写入，之后不变。
/// 框架不解释它，只保证它是一个可以安全放进列、放进 URL、拿来当查表键的标识：字母开头，字母、数字、
/// <c>_</c> <c>.</c> <c>:</c> <c>-</c>，至多 <see cref="MaxLength"/> 个字符，大小写敏感。
/// </para>
/// <para>
/// 形状由每条建线程的路径各自校验（服务端代码直接建、聊天第一条消息新建）；聊天新建的线程最终带什么值是另一回事，
/// 由 <see cref="IThreadOriginPolicy"/> 决定：放行或拒绝页面声明的值，或者给出只有服务端知道的值。
/// </para>
/// </remarks>
public static class ThreadOrigin
{
    /// <summary>来源的最大长度（列宽）。</summary>
    public const int MaxLength = 64;

    private static readonly Regex Pattern = new(
        "^[A-Za-z][A-Za-z0-9_.:-]{0," + (MaxLength - 1) + "}$", RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));

    /// <summary>是否是一个形状合规的来源值。</summary>
    public static bool IsWellFormed(string? origin) => origin is not null && Pattern.IsMatch(origin);

    /// <summary>
    /// 校验一个要写进线程的来源：null 或空串表示「没有声明」（返回 null），其余必须形状合规，否则 400 <c>AI_INVALID_THREAD_ORIGIN</c>。
    /// </summary>
    public static Result<string?> Normalize(string? origin)
    {
        if (string.IsNullOrEmpty(origin))
        {
            return Result<string?>.Success(null);
        }

        return IsWellFormed(origin)
            ? Result<string?>.Success(origin)
            : Result<string?>.Failure(
                $"A thread origin starts with a letter and contains only letters, digits, '_', '.', ':' or '-' (at most {MaxLength} characters).",
                400, ErrorCodes.InvalidThreadOrigin);
    }
}
