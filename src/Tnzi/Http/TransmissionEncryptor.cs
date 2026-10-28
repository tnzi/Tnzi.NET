
namespace Tnzi.Http;

/// <summary>
/// 结合 RSA、AES 的通信传输加密解密操作类：AES 对正文做对称加密，RSA 加密 AES 密钥，并用发送方私钥对正文签名。
/// </summary>
/// <remarks>
/// <para>它是应用层的附加混淆，<b>不</b>保证身份与完整性：</para>
/// <list type="bullet">
/// <item>验签用的对方公钥由调用方提供、没有任何背书（服务端用的是请求头里客户端自带的公钥）。
/// 验签通过只说明正文与这把公钥配套，不说明是谁发的，也挡不住中间人换掉公钥后重签。</item>
/// <item>不是本协议格式的输入（不含分隔符）<b>原样返回、不验签</b>，见 <see cref="DecryptAndVerifyData"/>。
/// 需要区分时先问 <see cref="IsEncryptedPayload"/>。</item>
/// </list>
/// <para>需要传输安全请用 TLS。</para>
/// </remarks>
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
    /// 输入是不是本协议格式的密文（含分隔符）。不是的话 <see cref="DecryptAndVerifyData"/> 会原样返回它、不做任何校验。
    /// </summary>
    /// <param name="data">接收到的数据</param>
    public bool IsEncryptedPayload(string data)
    {
        return !string.IsNullOrEmpty(data) && data.Contains(GetSeparator(_separator), StringComparison.Ordinal);
    }

    /// <summary>
    /// 解密接收到的密文并用对方公钥验签，通过后返回明文。
    /// <b>不是本协议格式的输入（见 <see cref="IsEncryptedPayload"/>）原样返回、不验签。</b>
    /// </summary>
    /// <param name="data">接收到的数据</param>
    /// <returns>解密并验签通过后的明文；输入不是本协议格式时就是输入本身</returns>
    /// <exception cref="ArgumentException">数据为空或格式不正确</exception>
    /// <exception cref="CryptographicException">解密或验证失败</exception>
    public string DecryptAndVerifyData(string data)
    {
        Check.NotNullOrEmpty(data);

        if (!IsEncryptedPayload(data))
        {
            // 没有分隔符：不是本协议的密文，原样返回（调用方要区分时先问 IsEncryptedPayload）
            return data;
        }

        string separator = GetSeparator(_separator);

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