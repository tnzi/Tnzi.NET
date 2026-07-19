namespace Tnzi.Finance.Options;

/// <summary>
/// 财务加密配置验证器
/// </summary>
/// <remarks>
/// 仅在配置了密钥时校验其格式（合法 Base64 且解码后为 32 字节）；
/// 留空是合法状态（表示未启用加密，写加密字段时服务层返回 400 引导）。
/// </remarks>
public class FinanceEncryptionOptionsValidator : OptionsValidatorBase<FinanceEncryptionOptions>
{
    private const int KeySizeBytes = 32;

    protected override void ValidateOptions(FinanceEncryptionOptions options, List<string> errors)
    {
        if (string.IsNullOrWhiteSpace(options.EncryptionKey))
            return;

        byte[] key;
        try
        {
            key = Convert.FromBase64String(options.EncryptionKey);
        }
        catch (FormatException)
        {
            errors.Add("Finance:Encryption:EncryptionKey must be a valid Base64 string.");
            return;
        }

        if (key.Length != KeySizeBytes)
            errors.Add($"Finance:Encryption:EncryptionKey must decode to {KeySizeBytes} bytes (256-bit).");
    }
}
