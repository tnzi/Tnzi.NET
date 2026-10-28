namespace Tnzi.System.Services;

/// <summary>
/// 一条加密设置「读到了但解不开」：没有配置加密器，或密钥 / 密文对不上。
/// </summary>
/// <remarks>
/// 与「这个键没配」是两件事。<see cref="TnziException.Code"/> 是
/// <see cref="ErrorCodes.CONFIGURATION_MISSING"/>（加密被关掉而加密行还在）或 <see cref="ErrorCodes.CONFIGURATION_INVALID"/>（解密失败）。
/// </remarks>
internal sealed class SettingUnreadableException : TnziException
{
    public SettingUnreadableException(string key, string code, string message, Exception? innerException = null)
        : base(code, message, innerException)
    {
        Key = key;
    }

    /// <summary>解不开的那个键。</summary>
    public string Key { get; }
}

/// <summary>
/// 加密设置值的解密口径：解不开就抛 <see cref="SettingUnreadableException"/>，绝不折叠成 <see langword="null"/>。
/// </summary>
/// <remarks>
/// ★ 每一条读加密设置的路径（按键读取、按作用域读取、<c>GetDecryptedAsync</c>、分层解析的三个 provider）都经这里。
/// 此前只有按键读取把解不开当失败，其余路径在加密器缺席时返回 <see langword="null"/>：调用方拿到与「没配」一模一样的答案，
/// 分层解析还会悄悄落到低一级作用域的另一个值上。而「加密被关掉而加密行还在」恰恰是最需要报出来的部署状态。
/// </remarks>
internal static class EncryptedSettingValue
{
    public static string Decrypt(ISettingEncryptor? encryptor, string key, string cipherText)
    {
        if (encryptor is null)
        {
            throw new SettingUnreadableException(
                key,
                ErrorCodes.CONFIGURATION_MISSING,
                $"Setting '{key}' is encrypted but setting encryption is not configured");
        }

        try
        {
            return encryptor.Decrypt(cipherText);
        }
        catch (Exception ex)
        {
            throw new SettingUnreadableException(
                key,
                ErrorCodes.CONFIGURATION_INVALID,
                $"Setting '{key}' is encrypted but could not be decrypted (wrong key or corrupted ciphertext)",
                ex);
        }
    }
}
