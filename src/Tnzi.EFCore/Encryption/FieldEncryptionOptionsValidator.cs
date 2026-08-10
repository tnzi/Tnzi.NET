namespace Tnzi.EFCore.Encryption;

/// <summary>
/// <see cref="FieldEncryptionOptions"/> 的启动期验证器。
/// </summary>
/// <remarks>
/// 配置错误在这里 fail-fast，而不是等到第一条数据要落库时才炸。
/// 加密配置写错的代价是数据写不进去或读不出来，越早暴露越好。
/// </remarks>
public partial class FieldEncryptionOptionsValidator : OptionsValidatorBase<FieldEncryptionOptions>
{
    /// <summary>密钥材料要求的字节长度（256 位）。</summary>
    private const int RequiredKeySizeBytes = 32;

    [GeneratedRegex("^[A-Za-z0-9_-]+$")]
    private static partial Regex KeyIdPattern();

    /// <inheritdoc />
    protected override void ValidateOptions(FieldEncryptionOptions options, List<string> errors)
    {
        // 未启用时不校验密钥环：允许配置里留着空壳节点。
        if (!options.Enabled)
        {
            return;
        }

        if (options.Keys.Count == 0)
        {
            AddError(errors, nameof(options.Keys), "must contain at least one key when field encryption is enabled.");
            return;
        }

        if (string.IsNullOrWhiteSpace(options.ActiveKeyId))
        {
            AddError(errors, nameof(options.ActiveKeyId), "is required when field encryption is enabled.");
        }
        else if (!options.Keys.ContainsKey(options.ActiveKeyId))
        {
            AddError(errors, nameof(options.ActiveKeyId), $"'{options.ActiveKeyId}' is not present in the configured key ring.");
        }

        foreach (var (keyId, material) in options.Keys)
        {
            if (string.IsNullOrWhiteSpace(keyId) || !KeyIdPattern().IsMatch(keyId))
            {
                AddError(errors, nameof(options.Keys), $"key id '{keyId}' is invalid. Only letters, digits, hyphen and underscore are allowed.");
                continue;
            }

            if (string.IsNullOrWhiteSpace(material))
            {
                AddError(errors, nameof(options.Keys), $"key '{keyId}' has empty key material.");
                continue;
            }

            byte[] bytes;
            try
            {
                bytes = Convert.FromBase64String(material);
            }
            catch (FormatException)
            {
                AddError(errors, nameof(options.Keys), $"key '{keyId}' is not valid Base64.");
                continue;
            }

            if (bytes.Length != RequiredKeySizeBytes)
            {
                AddError(
                    errors,
                    nameof(options.Keys),
                    $"key '{keyId}' must decode to {RequiredKeySizeBytes} bytes ({RequiredKeySizeBytes * 8} bits) but was {bytes.Length}.",
                    RequiredKeySizeBytes);
            }
        }
    }
}
