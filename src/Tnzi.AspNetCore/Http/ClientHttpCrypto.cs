
namespace Tnzi.AspNetCore.Http;

/// <summary>
/// Http客户端通信加密解密器
/// </summary>
public class ClientHttpCrypto : IClientHttpCrypto
{
    private readonly ILogger _logger;
    private readonly TransmissionEncryptor? _encryptor;
    private readonly string? _clientPublicKey;

    /// <summary>
    /// 初始化一个<see cref="ClientHttpCrypto"/>类型的新实例
    /// </summary>
    /// <param name="provider">服务提供者</param>
    public ClientHttpCrypto(IServiceProvider provider)
    {
        Check.NotNull(provider);

        _logger = provider.GetLogger(typeof(ClientHttpCrypto));
        var aspNetCoreOptions = provider.GetService(typeof(IOptions<AspNetCoreOptions>)) as IOptions<AspNetCoreOptions>;
        var options = aspNetCoreOptions?.Value;
        
        if (options?.HttpEncrypt?.Enabled == true)
        {
            HttpEncryptOptions httpEncrypt = options.HttpEncrypt;
            string? hostPublicKey = httpEncrypt.HostPublicKey;
            
            if (string.IsNullOrEmpty(hostPublicKey))
            {
                throw new TnziException("The HostPublicKey of the HttpEncrypt node in the configuration file cannot be empty.");
            }

            RsaHelper rsa = new RsaHelper();
            _encryptor = new TransmissionEncryptor(rsa.PrivateKey, hostPublicKey);
            _clientPublicKey = rsa.PublicKey;
            _logger.LogDebug("Create a client communication encryptor using the new client RSA private key and server public key");
        }
    }

    /// <summary>
    /// 将要发往服务器的请求进行加密
    /// </summary>
    /// <param name="request">未加密的请求</param>
    /// <returns>加密后的请求</returns>
    public virtual async Task<HttpRequestMessage> EncryptRequest(HttpRequestMessage request)
    {
        Check.NotNull(request);

        if (_encryptor == null || string.IsNullOrEmpty(_clientPublicKey) || request.Method == HttpMethod.Get || request.Content == null)
        {
            return request;
        }

        string data = await request.Content.ReadAsStringAsync();
        data = _encryptor.EncryptData(data);
        request = request.CreateNew(data);
        request.Headers.Add(HttpHeaderNames.ClientPublicKey, _clientPublicKey);
        _logger.LogDebug("Use client public key to encrypt client request data");
        return request;
    }

    /// <summary>
    /// 解密从服务器收到的响应
    /// </summary>
    /// <param name="response">加密的响应</param>
    /// <returns>解密后的响应</returns>
    public virtual async Task<HttpResponseMessage> DecryptResponse(HttpResponseMessage response)
    {
        Check.NotNull(response);

        if (_encryptor == null || !response.IsSuccessStatusCode)
        {
            return response;
        }

        string data = await response.Content.ReadAsStringAsync();
        if (string.IsNullOrEmpty(data))
        {
            return response;
        }

        try
        {
            data = _encryptor.DecryptAndVerifyData(data);
            if (data == null)
            {
                throw new TnziException("Failed to verify response data signature.");
            }
            response = response.CreateNew(data);
            _logger.LogDebug("Use the client's private key to decrypt the response data, and use the server's public key to verify the data");
            return response;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "An exception occurred when the client decrypted the response data.");
            response = new HttpResponseMessage(HttpStatusCode.InternalServerError);
            return response;
        }
    }
}