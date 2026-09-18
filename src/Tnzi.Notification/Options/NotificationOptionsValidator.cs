namespace Tnzi.Notification.Options;

/// <summary>
/// Notification配置验证器
/// </summary>
/// <remarks>
/// 每条渠道的默认节（<c>MailSender</c>）与具名节（<c>MailSenders:{key}</c>）走<b>同一套</b>规则，
/// 只是错误信息里的路径前缀不同 —— 两套规则会漂开，而漂开的症状是「具名的那家配错了却启动得起来」。
/// </remarks>
public class NotificationOptionsValidator : OptionsValidatorBase<NotificationOptions>
{
    /// <summary>
    /// 邮箱地址的形状检查：本地部分 + <c>@</c> + 带点的域名，三段都不含空白。
    /// </summary>
    /// <remarks>
    /// 刻意宽松 —— 这里要挡的是配置写错（漏了域名、写了两个 <c>@</c>、粘进了空格），
    /// 不是判定一个地址收不收得到信，那只有真发一封才知道。
    /// </remarks>
    private const string EmailShape = @"^[^@\s]+@[^@\s]+\.[^@\s]+$";

    protected override void ValidateOptions(NotificationOptions options, List<string> errors)
    {
        // 验证各个配置部分
        ValidateMailSenderOptions(options.MailSender, "MailSender", errors);
        ValidateSmsSenderOptions(options.SmsSender, "SmsSender", errors);
        ValidatePushSenderOptions(options.PushSender, "PushSender", errors);
        ValidateFaxSenderOptions(options.FaxSender, "FaxSender", errors);

        ValidateNamedProfiles(options.MailSenders, "MailSenders", errors, ValidateMailSenderOptions);
        ValidateNamedProfiles(options.SmsSenders, "SmsSenders", errors, ValidateSmsSenderOptions);
        ValidateNamedProfiles(options.PushSenders, "PushSenders", errors, ValidatePushSenderOptions);
        ValidateNamedProfiles(options.FaxSenders, "FaxSenders", errors, ValidateFaxSenderOptions);

        ValidateQueueOptions(options.Queue, errors);
        ValidateAttachmentOptions(options.Attachments, errors);
        ValidateCommonOptions(options, errors);
    }

    /// <summary>
    /// 验证附件来源配置：根目录必须是绝对路径（相对路径取决于进程工作目录，等于没约束），上限必须为正。
    /// </summary>
    private static void ValidateAttachmentOptions(AttachmentOptions attachments, List<string> errors)
    {
        if (attachments == null)
        {
            errors.Add("Attachments must not be null.");
            return;
        }

        foreach (var root in attachments.AllowedLocalRoots ?? [])
        {
            if (string.IsNullOrWhiteSpace(root))
            {
                errors.Add("Attachments.AllowedLocalRoots must not contain blank entries.");
                continue;
            }

            if (!Path.IsPathFullyQualified(root))
                errors.Add($"Attachments.AllowedLocalRoots entry '{root}' must be an absolute path.");
        }

        if (attachments.MaxAttachmentBytes <= 0)
            errors.Add("Attachments.MaxAttachmentBytes must be greater than 0.");
    }

    /// <summary>
    /// 具名节：键本身要合法且不能是保留的 <c>default</c>，值按该渠道的规则逐节检查。
    /// </summary>
    /// <remarks>
    /// ★ 键是配置节的名字，写错了不会有任何症状：没有代码引用它，只有消息带着这个键来投递时
    /// 才会失败，而那时报的是「服务商未注册」。所以形状在这里就拦。
    /// </remarks>
    private static void ValidateNamedProfiles<TProfile>(
        Dictionary<string, TProfile> profiles, string section, List<string> errors,
        Action<TProfile?, string, List<string>> validateProfile)
    {
        foreach (var (key, profile) in profiles)
        {
            var path = $"{section}:{key}";

            if (NotificationProviderKeys.IsDefault(key))
            {
                errors.Add($"{path} uses the reserved provider key '{NotificationProviderKeys.Default}'; the default sender is the singular section ({section.TrimEnd('s')}).");
                continue;
            }

            var keyError = NotificationProviderKeys.Describe(key.Trim());
            if (keyError != null)
            {
                errors.Add($"{path}: {keyError}");
                continue;
            }

            if (profile == null)
            {
                errors.Add($"{path} is declared but empty.");
                continue;
            }

            validateProfile(profile, path, errors);
        }
    }

    /// <summary>
    /// 验证邮件发送配置
    /// </summary>
    private static void ValidateMailSenderOptions(MailSenderOptions? mailSender, string prefix, List<string> errors)
    {
        if (mailSender == null)
            return;

        if (string.IsNullOrWhiteSpace(mailSender.SmtpServer))
            errors.Add($"{prefix}.SmtpServer is required.");

        if (mailSender.SmtpPort <= 0 || mailSender.SmtpPort > 65535)
            errors.Add($"{prefix}.SmtpPort must be between 1 and 65535.");

        if (string.IsNullOrWhiteSpace(mailSender.FromEmail))
            errors.Add($"{prefix}.FromEmail is required.");

        if (!string.IsNullOrWhiteSpace(mailSender.FromEmail) &&
            !Regex.IsMatch(mailSender.FromEmail, EmailShape, RegexOptions.IgnoreCase))
            errors.Add($"{prefix}.FromEmail must be a valid email address.");

        // 如果启用了 SSL，验证用户名和密码
        if (mailSender.EnableSsl)
        {
            if (string.IsNullOrWhiteSpace(mailSender.Username))
                errors.Add($"{prefix}.Username is required when EnableSsl is true.");

            if (string.IsNullOrWhiteSpace(mailSender.Password))
                errors.Add($"{prefix}.Password is required when EnableSsl is true.");
        }
    }

    /// <summary>
    /// 验证短信发送配置
    /// </summary>
    private static void ValidateSmsSenderOptions(SmsSenderOptions? smsSender, string prefix, List<string> errors)
    {
        if (smsSender == null)
            return;

        var provider = smsSender.Provider?.ToLower() ?? string.Empty;

        if (provider == "twilio")
        {
            if (string.IsNullOrWhiteSpace(smsSender.TwilioAccountSid))
                errors.Add($"{prefix}.TwilioAccountSid is required when Provider is 'twilio'.");

            if (string.IsNullOrWhiteSpace(smsSender.TwilioAuthToken))
                errors.Add($"{prefix}.TwilioAuthToken is required when Provider is 'twilio'.");

            if (string.IsNullOrWhiteSpace(smsSender.TwilioFromPhoneNumber))
                errors.Add($"{prefix}.TwilioFromPhoneNumber is required when Provider is 'twilio'.");
        }
        else if (provider == "plivo")
        {
            if (string.IsNullOrWhiteSpace(smsSender.PlivoAuthId))
                errors.Add($"{prefix}.PlivoAuthId is required when Provider is 'plivo'.");

            if (string.IsNullOrWhiteSpace(smsSender.PlivoAuthToken))
                errors.Add($"{prefix}.PlivoAuthToken is required when Provider is 'plivo'.");

            if (string.IsNullOrWhiteSpace(smsSender.PlivoFromPhoneNumber))
                errors.Add($"{prefix}.PlivoFromPhoneNumber is required when Provider is 'plivo'.");
        }
        else if (!string.IsNullOrWhiteSpace(provider))
        {
            errors.Add($"{prefix}.Provider '{smsSender.Provider}' is not supported. Supported providers: twilio, plivo.");
        }
    }

    /// <summary>
    /// 验证推送通知配置
    /// </summary>
    private static void ValidatePushSenderOptions(PushSenderOptions? pushSender, string prefix, List<string> errors)
    {
        if (pushSender == null)
            return;

        var provider = pushSender.Provider?.ToLower() ?? string.Empty;

        if (provider == "fcm" || provider == "firebase")
        {
            if (string.IsNullOrWhiteSpace(pushSender.FirebaseProjectId))
                errors.Add($"{prefix}.FirebaseProjectId is required when Provider is 'fcm' or 'firebase'.");

            if (string.IsNullOrWhiteSpace(pushSender.FirebaseServiceAccountJson) &&
                string.IsNullOrWhiteSpace(pushSender.FirebaseServiceAccountJsonPath))
            {
                errors.Add($"{prefix}.FirebaseServiceAccountJson or FirebaseServiceAccountJsonPath is required when Provider is 'fcm' or 'firebase'.");
            }
            else if (!string.IsNullOrWhiteSpace(pushSender.FirebaseServiceAccountJsonPath))
            {
                // 注意：不验证文件路径是否存在，因为文件可能在运行时才创建
                // 如果文件不存在，会在实际使用时失败并记录错误
            }
            else if (!string.IsNullOrWhiteSpace(pushSender.FirebaseServiceAccountJson))
            {
                // 验证 JSON 内容是否有效
                try
                {
                    JsonDocument.Parse(pushSender.FirebaseServiceAccountJson);
                }
                catch (JsonException)
                {
                    errors.Add($"{prefix}.FirebaseServiceAccountJson is not valid JSON.");
                }
            }
        }
        else if (provider == "apns")
        {
            errors.Add($"{prefix}.Provider 'apns' is not yet implemented.");
        }
        else if (!string.IsNullOrWhiteSpace(provider))
        {
            errors.Add($"{prefix}.Provider '{pushSender.Provider}' is not supported. Supported providers: fcm, firebase.");
        }
    }

    /// <summary>
    /// 验证传真发送配置
    /// </summary>
    /// <remarks>
    /// 网关域名写成邮箱地址（<c>fax@example.com</c>）是最容易犯的一个错，而它的后果是
    /// 拼出 <c>9055551234@fax@example.com</c> —— 一个投递失败的地址。这里当场拦掉。
    /// </remarks>
    private static void ValidateFaxSenderOptions(FaxSenderOptions? faxSender, string prefix, List<string> errors)
    {
        if (faxSender == null || !faxSender.Enabled)
            return;

        var domain = faxSender.GatewayDomain?.Trim().TrimStart('@') ?? string.Empty;

        if (string.IsNullOrWhiteSpace(domain))
        {
            errors.Add($"{prefix}.GatewayDomain is required when {prefix}.Enabled is true.");
        }
        else if (domain.Contains('@') || domain.Any(char.IsWhiteSpace) || !domain.Contains('.'))
        {
            errors.Add($"{prefix}.GatewayDomain '{faxSender.GatewayDomain}' must be a bare domain such as 'fax.example.com', not an email address.");
        }

        if (!string.IsNullOrWhiteSpace(faxSender.DevOverrideEmail) &&
            !Regex.IsMatch(faxSender.DevOverrideEmail, EmailShape, RegexOptions.IgnoreCase))
        {
            errors.Add($"{prefix}.DevOverrideEmail must be a valid email address.");
        }

        // 承载邮件发送器的键只查形状：它可能指向代码里注册的 keyed IEmailSender，
        // 配置层面看不见那个注册，存不存在要到解析 IFaxSender 时才知道。
        var emailKey = NotificationProviderKeys.Normalize(faxSender.EmailProviderKey);
        if (emailKey != null && NotificationProviderKeys.Describe(emailKey) is { } keyError)
            errors.Add($"{prefix}.EmailProviderKey: {keyError}");

        ValidateFaxConfirmationOptions(faxSender.Confirmation, prefix, errors);
    }

    /// <summary>
    /// 验证传真回执收件箱配置
    /// </summary>
    /// <remarks>
    /// ★ <b>整节缺省是合法的</b>（这个部署不收回执），所以 <c>Host</c> 为空直接放行。
    /// 但填了 <c>Host</c> 就必须填全账号密码：那是笔误，而它的症状是"配了却不生效"——
    /// 没有报错、没有日志、回执就是不进来。宁可在启动时炸。
    /// </remarks>
    private static void ValidateFaxConfirmationOptions(FaxConfirmationOptions? confirmation, string prefix, List<string> errors)
    {
        if (confirmation == null || !confirmation.Enabled || string.IsNullOrWhiteSpace(confirmation.Host))
            return;

        if (string.IsNullOrWhiteSpace(confirmation.UserName))
            errors.Add($"{prefix}.Confirmation.UserName is required when a Host is configured.");

        if (string.IsNullOrWhiteSpace(confirmation.Password))
            errors.Add($"{prefix}.Confirmation.Password is required when a Host is configured.");

        if (confirmation.Port is < 1 or > 65535)
            errors.Add($"{prefix}.Confirmation.Port '{confirmation.Port}' must be between 1 and 65535.");

        if (string.IsNullOrWhiteSpace(confirmation.Folder))
            errors.Add($"{prefix}.Confirmation.Folder cannot be blank; the usual value is 'INBOX'.");

        // 网关的回执本来就要几分钟才回，秒级轮询只是白白敲人家的 IMAP（还可能被限流）。
        if (confirmation.PollIntervalSeconds < 30)
            errors.Add($"{prefix}.Confirmation.PollIntervalSeconds '{confirmation.PollIntervalSeconds}' is too small; 30 is the minimum.");

        if (confirmation.MaxMessagesPerPoll < 1)
            errors.Add($"{prefix}.Confirmation.MaxMessagesPerPoll '{confirmation.MaxMessagesPerPoll}' must be at least 1.");

        if (confirmation.LookbackHours < 1)
            errors.Add($"{prefix}.Confirmation.LookbackHours '{confirmation.LookbackHours}' must be at least 1.");
    }

    /// <summary>
    /// 验证队列配置
    /// </summary>
    private static void ValidateQueueOptions(QueueOptions queue, List<string> errors)
    {
        if (!queue.Enabled)
            return;

        if (queue.QueueCapacity <= 0)
            errors.Add("Queue.QueueCapacity must be greater than 0.");
    }

    /// <summary>
    /// 验证通用配置选项
    /// </summary>
    private static void ValidateCommonOptions(NotificationOptions options, List<string> errors)
    {
        // 验证并发配置
        if (options.MaxConcurrency <= 0)
            errors.Add("MaxConcurrency must be greater than 0.");

        // 验证超时配置
        if (options.SendTimeoutSeconds <= 0)
            errors.Add("SendTimeoutSeconds must be greater than 0.");

        // 验证 SMS 最大长度配置
        if (options.SmsMaxContentLength <= 0)
            errors.Add("SmsMaxContentLength must be greater than 0.");

        // 验证重试配置
        if (options.Retry.RetryDelaySeconds < 0)
            errors.Add("Retry.RetryDelaySeconds must be greater than or equal to 0.");
    }
}
