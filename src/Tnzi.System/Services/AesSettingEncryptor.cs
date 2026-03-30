namespace Tnzi.System.Services;

/// <summary>
/// AES-256-GCM 配置值加密器
/// 输出格式：Base64( nonce[12] + ciphertext[N] + tag[16] )
/// </summary>
public class AesSettingEncryptor : ISettingEncryptor, IDisposable
{
    private const int NonceSize = 12; // AES-GCM 推荐 96-bit nonce
    private const int TagSize = 16;   // AES-GCM 128-bit tag

    private readonly byte[] _key;

    public AesSettingEncryptor(IOptions<SettingEncryptionOptions> options)
    {
        Check.NotNull(options);

        var encryptionOptions = options.Value;
        if (string.IsNullOrWhiteSpace(encryptionOptions.EncryptionKey))
            throw new ConfigurationException("System:Encryption:EncryptionKey", "Encryption key is not configured");

        _key = Convert.FromBase64String(encryptionOptions.EncryptionKey);
        if (_key.Length != 32)
            throw new ConfigurationException("System:Encryption:EncryptionKey", "Encryption key must be 32 bytes (256-bit)");
    }

    /// <inheritdoc />
    public string Encrypt(string plainText)
    {
        Check.NotNull(plainText);

        var plainBytes = Encoding.UTF8.GetBytes(plainText);
        try
        {
            var nonce = new byte[NonceSize];
            RandomNumberGenerator.Fill(nonce);

            var cipherText = new byte[plainBytes.Length];
            var tag = new byte[TagSize];

            using var aes = new AesGcm(_key, TagSize);
            aes.Encrypt(nonce, plainBytes, cipherText, tag);

            // 输出格式：nonce + ciphertext + tag
            var result = new byte[NonceSize + cipherText.Length + TagSize];
            nonce.CopyTo(result, 0);
            cipherText.CopyTo(result, NonceSize);
            tag.CopyTo(result, NonceSize + cipherText.Length);

            return Convert.ToBase64String(result);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(plainBytes);
        }
    }

    /// <inheritdoc />
    public string Decrypt(string cipherText)
    {
        Check.NotNull(cipherText);

        byte[] data;
        try
        {
            data = Convert.FromBase64String(cipherText);
        }
        catch (FormatException)
        {
            throw new BusinessException("Invalid encrypted value format", ErrorCodes.VALIDATION_ERROR);
        }

        if (data.Length < NonceSize + TagSize)
            throw new BusinessException("Invalid encrypted value: data too short", ErrorCodes.VALIDATION_ERROR);

        var nonce = data.AsSpan(0, NonceSize);
        var encrypted = data.AsSpan(NonceSize, data.Length - NonceSize - TagSize);
        var tag = data.AsSpan(data.Length - TagSize, TagSize);

        var plainBytes = new byte[encrypted.Length];

        try
        {
            using var aes = new AesGcm(_key, TagSize);
            aes.Decrypt(nonce, encrypted, tag, plainBytes);

            return Encoding.UTF8.GetString(plainBytes);
        }
        catch (CryptographicException)
        {
            throw new BusinessException("Failed to decrypt setting value: invalid key or corrupted data", ErrorCodes.VALIDATION_ERROR);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(plainBytes);
        }
    }

    private bool _disposed;

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed) return;
        CryptographicOperations.ZeroMemory(_key);
        _disposed = true;
    }
}
