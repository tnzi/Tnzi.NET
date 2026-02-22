
namespace Tnzi.AspNetCore.Http;

/// <summary>
/// Http服务端加密解密功能
/// </summary>
public class HostHttpCrypto : IHostHttpCrypto
{
    private readonly ILogger _logger;
    private readonly string? _privateKey;
    private TransmissionEncryptor? _encryptor;

    /// <summary>
    /// 初始化一个<see cref="HostHttpCrypto"/>类型的新实例
    /// </summary>
    /// <param name="provider">服务提供者</param>
    public HostHttpCrypto(IServiceProvider provider)
    {
        Check.NotNull(provider);

        _logger = provider.GetLogger(typeof(HostHttpCrypto));
        var aspNetCoreOptions = provider.GetService(typeof(IOptions<AspNetCoreOptions>)) as IOptions<AspNetCoreOptions>;
        var options = aspNetCoreOptions?.Value;
        
        if (options?.HttpEncrypt?.Enabled == true)
        {
            HttpEncryptOptions httpEncrypt = options.HttpEncrypt;
            _privateKey = httpEncrypt.HostPrivateKey;
            
            if (string.IsNullOrEmpty(_privateKey))
            {
                throw new TnziException("The HostPrivateKey of the HttpEncrypt node in the configuration file cannot be empty");
            }
        }
    }

    /// <summary>
    /// 将收到的客户端请求进行解密
    /// </summary>
    /// <param name="request">加密的请求</param>
    /// <returns>解密后的请求</returns>
    public async Task<HttpRequest> DecryptRequest(HttpRequest request)
    {
        Check.NotNull(request);

        if (_privateKey == null || request.Method == HttpMethods.Get || request.Body == null)
        {
            return request;
        }

        string? clientPublicKey = request.Headers.GetOrDefault(HttpHeaderNames.ClientPublicKey);
        if (clientPublicKey != null)
        {
            _encryptor = new TransmissionEncryptor(_privateKey, clientPublicKey);
        }
        
        if (_encryptor == null)
        {
            return request;
        }
        
        _logger.LogDebug("Use the incoming client public key and server private key to create a server communication encryptor");

        try
        {
            string data = await request.ReadAsStringAsync();
            if (string.IsNullOrEmpty(data))
            {
                return request;
            }

            data = _encryptor.DecryptAndVerifyData(data);
            if (data == null)
            {
                throw new TnziException("An exception occurred while the server was parsing the request data.");
            }
            
            _logger.LogDebug("Use the server's private key to decrypt the request data, and use the client's public key to verify the data");
            await request.WriteBodyAsync(data);
            return request;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, ex is CryptographicException 
                ? "An exception occurred when the server parsed the transmitted data." 
                : "An exception occurred when the server decrypted the requested data.");
            throw new TnziException("An exception occurred while the server was decrypting the request data.", ex);
        }
    }

    /// <summary>
    /// 加密发往客户端的响应
    /// </summary>
    /// <param name="response">未加密的响应</param>
    /// <returns>加密后的响应</returns>
    public async Task<HttpResponse> EncryptResponse(HttpResponse response)
    {
        Check.NotNull(response);

        if (_encryptor == null || !response.IsSuccessStatusCode())
        {
            return response;
        }

        string data = await response.ReadAsStringAsync();
        if (string.IsNullOrEmpty(data))
        {
            return response;
        }

        try
        {
            data = _encryptor.EncryptData(data);
            _logger.LogDebug("Encrypt response data using server-side public key");
            response = await response.WriteBodyAsync(data);
            return response;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "An exception occurred when the server encrypted the returned data.");
            throw;
        }
    }
}