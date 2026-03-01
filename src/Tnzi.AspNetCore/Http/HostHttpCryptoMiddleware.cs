
namespace Tnzi.AspNetCore.Http;

/// <summary>
/// 服务端通信加密解密中间件, 对请求进行解密, 对响应进行加密, 如使用, 请将此中间件放在第一个
/// 实现 IMiddleware 接口以支持 Scoped 生命周期（每个请求一个实例），避免并发竞态条件
/// </summary>
public class HostHttpCryptoMiddleware : IMiddleware
{
    private readonly IHostHttpCrypto _hostHttpCrypto;

    /// <summary>
    /// 初始化一个<see cref="HostHttpCryptoMiddleware"/>类型的新实例
    /// </summary>
    /// <param name="hostHttpCrypto">服务端HTTP加密服务</param>
    public HostHttpCryptoMiddleware(IHostHttpCrypto hostHttpCrypto)
    {
        _hostHttpCrypto = Check.NotNull(hostHttpCrypto);
    }

    /// <summary>
    /// 执行中间件拦截逻辑
    /// </summary>
    /// <param name="context">Http上下文</param>
    /// <param name="next">下一个中间件</param>
    public async Task InvokeAsync(HttpContext context, RequestDelegate next)
    {
        HttpRequest request = context.Request;
        await _hostHttpCrypto.DecryptRequest(request);
        await next(context);
        HttpResponse response = context.Response;
        await _hostHttpCrypto.EncryptResponse(response);
    }
}