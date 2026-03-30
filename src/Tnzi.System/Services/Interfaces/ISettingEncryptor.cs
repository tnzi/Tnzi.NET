namespace Tnzi.System.Services;

/// <summary>
/// 配置值加密/解密接口
/// 用于加密存储敏感配置（密码、API Key、连接字符串等）
/// </summary>
public interface ISettingEncryptor
{
    /// <summary>
    /// Encrypt plain text to cipher text
    /// </summary>
    /// <param name="plainText">The plain text to encrypt</param>
    /// <returns>Base64-encoded cipher text (includes nonce and tag)</returns>
    string Encrypt(string plainText);

    /// <summary>
    /// Decrypt cipher text to plain text
    /// </summary>
    /// <param name="cipherText">Base64-encoded cipher text (includes nonce and tag)</param>
    /// <returns>The decrypted plain text</returns>
    string Decrypt(string cipherText);
}
