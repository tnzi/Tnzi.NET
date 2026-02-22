
namespace Tnzi.AspNetCore.Http;

/// <summary>
/// 服务端通信加密解密中间件, 对请求进行解密, 对响应进行加密, 如使用, 请将此中间件放在第一个
/// </summary>
public class HostHttpCryptoMiddleware
{
    private readonly RequestDelegate _next;
    private readonly IHostHttpCrypto _hostHttpCrypto;

    /// <summary>
    /// 初始化一个<see cref="HostHttpCryptoMiddleware"/>类型的新实例
    /// </summary>
    /// <param name="next">下一个中间件</param>
    /// <param name="hostHttpCrypto">服务端HTTP加密服务</param>
    public HostHttpCryptoMiddleware(RequestDelegate next, IHostHttpCrypto hostHttpCrypto)
    {
        _next = Check.NotNull(next);
        _hostHttpCrypto = Check.NotNull(hostHttpCrypto);
    }

    /// <summary>
    /// 执行中间件拦截逻辑
    /// </summary>
    /// <param name="context">Http上下文</param>
    /// <returns></returns>
    public async Task InvokeAsync(HttpContext context)
    {
        HttpRequest request = context.Request;
        await _hostHttpCrypto.DecryptRequest(request);
        await _next(context);
        HttpResponse response = context.Response;
        await _hostHttpCrypto.EncryptResponse(response);
    }
}