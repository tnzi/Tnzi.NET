namespace Tnzi.Notification.Services;

/// <summary>
/// 邮件信封（收件人集合）的归一化、摘要与开发环境重定向
/// </summary>
/// <remarks>
/// 全是纯函数，一律返回新的 <see cref="EmailMessage"/>，绝不改动传入的对象——
/// 调用方可能在重试时复用同一份消息，就地改写收件人会让第二次发送寄错地方。
/// </remarks>
internal static class EmailEnvelope
{
    /// <summary>
    /// 摘要里最多列出的地址个数，其余折叠为 "+N more"（开发重定向会把摘要写进主题，不能无上限）
    /// </summary>
    private const int MaxDescribedAddresses = 5;

    /// <summary>
    /// 去掉空白地址、修剪首尾空格并按地址去重（大小写不敏感）。
    /// 同一地址出现在多个字段时只保留可见性最高的那一处：To &gt; Cc &gt; Bcc。
    /// </summary>
    internal static EmailMessage Normalize(EmailMessage message)
    {
        Check.NotNull(message);

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        return new EmailMessage
        {
            To = Distinct(message.To, seen),
            Cc = Distinct(message.Cc, seen),
            Bcc = Distinct(message.Bcc, seen),
            Subject = message.Subject,
            Body = message.Body,
            IsHtml = message.IsHtml,
            Attachments = message.Attachments
        };
    }

    /// <summary>
    /// 是否一个收件人都没有（To/Cc/Bcc 全空）
    /// </summary>
    internal static bool HasNoRecipient(EmailMessage message)
    {
        Check.NotNull(message);
        return message.To.Count == 0 && message.Cc.Count == 0 && message.Bcc.Count == 0;
    }

    /// <summary>
    /// 把整封信收敛到开发重定向地址：<paramref name="address"/> 成为唯一收件人，抄送与密送一并清空
    /// </summary>
    /// <remarks>
    /// 重定向生效时，除该地址以外任何地址都不允许收到这封信——所以这里是重新构造一个信封，
    /// 而不是在原信封上覆盖 To：覆盖式写法一旦漏掉某个字段，那份名单就会带着真实地址发出去。
    /// 原收件人名单写进主题，开发者据此看得出这封信本来要发给谁。
    /// </remarks>
    internal static EmailMessage RedirectTo(EmailMessage message, string address)
    {
        Check.NotNull(message);
        Check.NotNullOrWhiteSpace(address);

        return new EmailMessage
        {
            To = [new EmailAddress(address.Trim(), "Dev Override")],
            Subject = $"[DEV → {Describe(message)}] {message.Subject}",
            Body = message.Body,
            IsHtml = message.IsHtml,
            Attachments = message.Attachments
        };
    }

    /// <summary>
    /// 把收件人名单摘成一行，用于日志与开发重定向后的主题前缀
    /// </summary>
    internal static string Describe(EmailMessage message)
    {
        Check.NotNull(message);

        // 允许描述尚未归一化的消息（NullEmailSender 就直接描述调用方传入的原始对象），故此处自行滤掉空项
        var all = message.To.Concat(message.Cc).Concat(message.Bcc)
            .Where(a => a != null && !string.IsNullOrWhiteSpace(a.Address))
            .ToList();
        if (all.Count == 0)
        {
            return "(no recipient)";
        }

        var shown = string.Join(", ", all.Take(MaxDescribedAddresses)
            .Select(a => string.IsNullOrWhiteSpace(a.Name) ? a.Address : $"{a.Name} <{a.Address}>"));

        return all.Count > MaxDescribedAddresses
            ? $"{shown} +{all.Count - MaxDescribedAddresses} more"
            : shown;
    }

    private static List<EmailAddress> Distinct(List<EmailAddress>? addresses, HashSet<string> seen)
    {
        if (addresses is not { Count: > 0 })
        {
            return [];
        }

        var result = new List<EmailAddress>(addresses.Count);
        foreach (var address in addresses)
        {
            if (address == null || string.IsNullOrWhiteSpace(address.Address))
            {
                continue;
            }

            var trimmed = address.Address.Trim();
            if (!seen.Add(trimmed))
            {
                continue;
            }

            result.Add(trimmed == address.Address ? address : address with { Address = trimmed });
        }

        return result;
    }
}

