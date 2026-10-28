namespace Tnzi.EFCore.Encryption;

/// <summary>
/// 密钥标识与密钥材料的格式规则,配置密钥环与密钥文件共用一份,免得两条来源对「合法密钥」的理解漂移。
/// </summary>
internal static partial class FieldEncryptionKeyValidation
{
    /// <summary>密钥材料要求的字节长度(256 位)。</summary>
    public const int RequiredKeySizeBytes = 32;

    [GeneratedRegex("^[A-Za-z0-9_-]+$")]
    private static partial Regex KeyIdPattern();

    /// <summary>
    /// 逐条校验密钥,问题经 <paramref name="report"/> 报出(消息不含密钥材料)。
    /// </summary>
    public static void ValidateKeys(IEnumerable<KeyValuePair<string, string>> keys, Action<string> report)
    {
        foreach (var (keyId, material) in keys)
        {
            if (string.IsNullOrWhiteSpace(keyId) || !KeyIdPattern().IsMatch(keyId))
            {
                report($"key id '{keyId}' is invalid. Only letters, digits, hyphen and underscore are allowed.");
                continue;
            }

            if (keyId.Length > FieldEncryptionOptions.MaxKeyIdLength)
            {
                report($"key id '{keyId}' is longer than {FieldEncryptionOptions.MaxKeyIdLength} characters. The key id is stored in every ciphertext and encrypted column lengths reserve at most {FieldEncryptionOptions.MaxKeyIdLength} characters for it.");
                continue;
            }

            if (string.IsNullOrWhiteSpace(material))
            {
                report($"key '{keyId}' has empty key material.");
                continue;
            }

            byte[] bytes;
            try
            {
                bytes = Convert.FromBase64String(material);
            }
            catch (FormatException)
            {
                report($"key '{keyId}' is not valid Base64.");
                continue;
            }

            if (bytes.Length != RequiredKeySizeBytes)
            {
                report($"key '{keyId}' must decode to {RequiredKeySizeBytes} bytes ({RequiredKeySizeBytes * 8} bits) but was {bytes.Length}.");
            }
        }
    }
}
