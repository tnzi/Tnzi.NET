
namespace Tnzi.Http;

/// <summary>
/// 结合RSA, AES的通信传输加密解密操作类, 使用AES对数据进行对称加密, 使用RSA加密AES的密钥, 并对数据进行签名校验, 保证数据传输安全与完整性
/// </summary>
public class TransmissionEncryptor
{
    private const string DefaultSeparator = "#@|tnzi|@#";

    private readonly string _separator = DefaultSeparator;
    private readonly string _facePublicKey;
    private readonly string _ownPrivateKey;

    /// <summary>
    /// 初始化一个<see cref="TransmissionEncryptor"/>类型的新实例
    /// </summary>
    /// <param name="ownPrivateKey">己方私钥</param>
    /// <param name="facePublicKey">对方公钥</param>
    public TransmissionEncryptor(string ownPrivateKey, string facePublicKey)
    {
        _ownPrivateKey = Check.NotNullOrEmpty(ownPrivateKey);
        _facePublicKey = Check.NotNullOrEmpty(facePublicKey);
    }

    /// <summary>
    /// 解密接收到的加密数据并验证完整性, 如果验证通过返回明文
    /// </summary>
    /// <param name="data">接收到的加密数据</param>
    /// <returns>解密并验证成功后, 返回明文</returns>
    /// <exception cref="ArgumentException">数据为空或格式不正确</exception>
    /// <exception cref="CryptographicException">解密或验证失败</exception>
    public string DecryptAndVerifyData(string data)
    {
        Check.NotNullOrEmpty(data);

        string separator = GetSeparator(_separator);
        if (!data.Contains(separator, StringComparison.Ordinal))
        {
            // 如果没有分隔符，说明数据未加密，直接返回
            return data;
        }

        string[] separators = { separator };
        //0为AES密钥密文, 1为 正文+摘要 的密文
        string[] datas = data.Split(separators, StringSplitOptions.None);

        // 验证数据格式
        Check.Condition(datas.Length >= 2, "Invalid encrypted data format: missing required parts.");

        try
        {
            //用接收端私钥RSA解密获取AES密钥
            byte[] keyBytes = RsaHelper.Decrypt(Convert.FromBase64String(datas[0]), _ownPrivateKey);
            string key = keyBytes.ToEncodingString();

            //AES解密获取 正文+摘要 的明文
            data = new AesHelper(key, true).Decrypt(datas[1]);

            //0为正文明文, 1为摘要
            datas = data.Split(separators, StringSplitOptions.None);
            if (datas.Length < 2)
            {
                throw new CryptographicException("Invalid decrypted data format: missing signature.");
            }

            data = datas[0];
            if (RsaHelper.VerifyData(data, datas[1], _facePublicKey))
            {
                return data;
            }

            throw new CryptographicException("Data signature verification failed. The data may have been tampered with.");
        }
        catch (FormatException ex)
        {
            throw new CryptographicException("Failed to decode base64 data.", ex);
        }
        catch (CryptographicException)
        {
            throw;
        }
        catch (Exception ex)
        {
            throw new CryptographicException("An error occurred during decryption.", ex);
        }
    }

    /// <summary>
    /// 加密要发送的数据, 包含签名, AES加密, RSA加密AES密钥等步骤
    /// </summary>
    /// <param name="data">要加密的正文明文数据</param>
    /// <returns>已加密待发送的密文</returns>
    /// <exception cref="ArgumentNullException">数据为空</exception>
    /// <exception cref="CryptographicException">加密失败</exception>
    public string EncryptData(string data)
    {
        Check.NotNull(data);

        try
        {
            string separator = GetSeparator(_separator);
            //获取正文摘要
            string signData = RsaHelper.SignData(data, _ownPrivateKey);
            data = new[] { data, signData }.ExpandAndToString(separator);

            //使用AES加密 正文+摘要
            AesHelper aes = new AesHelper(true);
            data = aes.Encrypt(data);

            //RSA加密AES密钥
            byte[] keyBytes = aes.Key.ToBytes();
            string enAesKey = Convert.ToBase64String(RsaHelper.Encrypt(keyBytes, _facePublicKey));
            return new[] { enAesKey, data }.ExpandAndToString(separator);
        }
        catch (CryptographicException)
        {
            throw;
        }
        catch (Exception ex)
        {
            throw new CryptographicException("An error occurred during encryption.", ex);
        }
    }

    private static string GetSeparator(string separator)
    {
        return Convert.ToBase64String(Encoding.UTF8.GetBytes(separator));
    }
}