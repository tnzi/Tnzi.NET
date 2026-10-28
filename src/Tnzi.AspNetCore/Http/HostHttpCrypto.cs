
namespace Tnzi.AspNetCore.Http;

/// <summary>
/// Http服务端加密解密功能
/// 注意：此类为 Scoped 生命周期，每个请求一个实例，避免并发竞态条件
/// </summary>
public class HostHttpCrypto : IHostHttpCrypto
{
    private readonly ILogger _logger;
    private readonly string? _privateKey;
    private readonly int _maxRequestBodyBytes = HttpEncryptOptions.DefaultMaxRequestBodyBytes;

    /// <summary>
    /// 当前请求的加密器实例（Scoped 生命周期，线程安全）
    /// </summary>
    private TransmissionEncryptor? _encryptor;

    /// <summary>
    /// 初始化一个<see cref="HostHttpCrypto"/>类型的新实例
    /// </summary>
    public HostHttpCrypto(IOptions<AspNetCoreOptions> aspNetCoreOptions, ILogger<HostHttpCrypto> logger)
    {
        _logger = Check.NotNull(logger);
        var options = Check.NotNull(aspNetCoreOptions).Value;

        if (options.HttpEncrypt?.Enabled == true)
        {
            HttpEncryptOptions httpEncrypt = options.HttpEncrypt;
            _privateKey = httpEncrypt.HostPrivateKey;

            if (string.IsNullOrEmpty(_privateKey))
            {
                throw new TnziException("The HostPrivateKey of the HttpEncrypt node in the configuration file cannot be empty");
            }

            if (httpEncrypt.MaxRequestBodyBytes <= 0)
            {
                throw new TnziException("The MaxRequestBodyBytes of the HttpEncrypt node in the configuration file must be greater than zero");
            }

            _maxRequestBodyBytes = httpEncrypt.MaxRequestBodyBytes;
        }
    }

    /// <inheritdoc />
    public bool IsResponseEncryptionNegotiated => _encryptor != null;

    /// <summary>
    /// 将收到的客户端请求进行解密
    /// </summary>
    /// <param name="request">加密的请求</param>
    /// <returns>解密后的请求</returns>
    /// <remarks>
    /// ★ <strong>协商先于解密。</strong>客户端公钥不论请求是什么方法都要读进来：
    /// GET 没有请求体可解密，但它的<b>响应</b>照样要加密。此前这里对 GET 整条早退，
    /// 于是 GET 成了唯一一条明文出口，而客户端处理器对每个成功响应都会尝试解密 ——
    /// 那个 GET 只会以 500 收场。
    /// </remarks>
    public async Task<HttpRequest> DecryptRequest(HttpRequest request)
    {
        Check.NotNull(request);

        if (_privateKey == null)
        {
            return request;
        }

        string? clientPublicKey = request.Headers.GetOrDefault(HttpHeaderNames.ClientPublicKey);
        if (!string.IsNullOrEmpty(clientPublicKey))
        {
            _encryptor = new TransmissionEncryptor(_privateKey, clientPublicKey);
            _logger.LogDebug("Use the incoming client public key and server private key to create a server communication encryptor");
        }

        if (_encryptor == null || request.Method == HttpMethods.Get || request.Body == null)
        {
            return request;
        }

        // 这段读发生在认证与限流之前，任何带 ClientPublicKey 头的未认证调用方都能让它跑：
        // 声明了 Content-Length 的在读之前拒绝；没声明的（chunked）只读到上限 + 1 字节就停，
        // 绝不「先读到底再查大小」：那等于让调用方决定服务端分配多大的字符串。
        if (request.ContentLength > _maxRequestBodyBytes)
        {
            throw RequestBodyTooLarge(request.ContentLength.Value);
        }

        var data = await request.TryReadAsStringAsync(_maxRequestBodyBytes, request.HttpContext.RequestAborted)
                   ?? throw RequestBodyTooLarge(null);
        if (string.IsNullOrEmpty(data))
        {
            return request;
        }

        if (!_encryptor.IsEncryptedPayload(data))
        {
            // 协商了但正文不是本协议的密文：原样交给管线（读取后已回到流首），不解密也不验签
            _logger.LogDebug("The request body is not in the encrypted transport format; it was passed through without decryption or signature verification");
            return request;
        }

        try
        {
            data = _encryptor.DecryptAndVerifyData(data);
        }
        catch (Exception ex) when (ex is CryptographicException or ArgumentException or FormatException)
        {
            // 解不开、验不过、格式不对都是客户端送来的载荷有问题：答 400，记 Warning，不当服务器故障记 500
            _logger.LogWarning(ex, "The encrypted request body could not be decrypted or verified");
            throw new BusinessException("The encrypted request body could not be decrypted or verified.", ErrorCodes.DATA_INVALID, StatusCodes.Status400BadRequest);
        }

        _logger.LogDebug("Use the server's private key to decrypt the request data, and use the client's public key to verify the data");
        await request.WriteBodyAsync(data);
        return request;
    }

    private BusinessException RequestBodyTooLarge(long? declaredBytes)
    {
        _logger.LogWarning("Encrypted request body rejected: {DeclaredBytes} bytes declared, limit {Limit} bytes", declaredBytes, _maxRequestBodyBytes);
        return new BusinessException(
            $"The encrypted request body exceeds the {_maxRequestBodyBytes}-byte limit.",
            ErrorCodes.DATA_INVALID,
            StatusCodes.Status413PayloadTooLarge);
    }

    /// <summary>
    /// 加密发往客户端的响应
    /// </summary>
    /// <param name="response">未加密的响应（<c>Body</c> 必须是可读可定位的缓冲流，见 <see cref="IHostHttpCrypto.EncryptResponse"/>）</param>
    /// <returns>加密后的响应</returns>
    /// <remarks>
    /// 失败响应刻意<b>不</b>加密：客户端要在「解密失败」与「业务失败」之间分得清，
    /// 而请求被拒时最可能出问题的恰恰是密钥本身。这条与客户端
    /// <c>ClientHttpCrypto.DecryptResponse</c> 的早退条件是同一条。
    /// </remarks>
    public async Task<HttpResponse> EncryptResponse(HttpResponse response)
    {
        Check.NotNull(response);

        if (_encryptor == null || !response.IsSuccessStatusCode() || !response.Body.CanRead)
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