
namespace Tnzi.AspNetCore.Http;

/// <summary>
/// HttpClient客户端加密通信处理器
/// </summary>
public class ClientHttpCryptoHandler : DelegatingHandler
{
    private readonly IClientHttpCrypto _clientHttpCrypto;

    /// <summary>
    /// 初始化一个<see cref="ClientHttpCryptoHandler"/>类型的新实例
    /// </summary>
    /// <param name="clientHttpCrypto">客户端HTTP加密服务</param>
    public ClientHttpCryptoHandler(IClientHttpCrypto clientHttpCrypto)
    {
        _clientHttpCrypto = Check.NotNull(clientHttpCrypto);
    }

    /// <summary>
    /// 发送HTTP请求到内部处理器以发送到服务器
    /// </summary>
    /// <param name="request">要发送到服务器的HTTP请求消息</param>
    /// <param name="cancellationToken">用于取消操作的取消令牌</param>
    /// <returns>表示异步操作的任务对象</returns>
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        request = await _clientHttpCrypto.EncryptRequest(request);
        HttpResponseMessage response = await base.SendAsync(request, cancellationToken);
        response = await _clientHttpCrypto.DecryptResponse(response);
        return response;
    }
}