namespace Tnzi.System.Options;

/// <summary>
/// 配置加密选项
/// 配置路径：System:Encryption
/// </summary>
public class SettingEncryptionOptions
{
    /// <summary>
    /// 获取或设置 是否启用加密功能
    /// </summary>
    public bool Enabled { get; set; }

    /// <summary>
    /// 获取或设置 AES-256 加密密钥（Base64 编码，32 字节）
    /// </summary>
    public string? EncryptionKey { get; set; }
}
