
namespace Tnzi.AspNetCore.Http;

/// <summary>
/// HTTP服务端加密通信接口
/// </summary>
public interface IHostHttpCrypto
{
    /// <summary>
    /// 将收到的客户端请求进行解密
    /// </summary>
    /// <param name="request">加密的请求</param>
    /// <returns>解密后的请求</returns>
    Task<HttpRequest> DecryptRequest(HttpRequest request);

    /// <summary>
    /// 加密发往客户端的响应
    /// </summary>
    /// <param name="response">未加密的响应</param>
    /// <returns>加密后的响应</returns>
    Task<HttpResponse> EncryptResponse(HttpResponse response);
}