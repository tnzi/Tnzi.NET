namespace Tnzi.System.Options;

/// <summary>
/// SettingEncryptionOptions 配置验证器
/// </summary>
public class SettingEncryptionOptionsValidator : OptionsValidatorBase<SettingEncryptionOptions>
{
    protected override void ValidateOptions(SettingEncryptionOptions options, List<string> errors)
    {
        if (!options.Enabled)
            return;

        if (string.IsNullOrWhiteSpace(options.EncryptionKey))
        {
            errors.Add("EncryptionKey is required when encryption is enabled.");
            return;
        }

        // 验证 Base64 格式及密钥长度（AES-256 需要 32 字节）
        try
        {
            var keyBytes = Convert.FromBase64String(options.EncryptionKey);
            if (keyBytes.Length != 32)
            {
                errors.Add("EncryptionKey must be a Base64-encoded 32-byte (256-bit) key.");
            }
        }
        catch (FormatException)
        {
            errors.Add("EncryptionKey must be a valid Base64-encoded string.");
        }
    }
}
