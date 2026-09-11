
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
    /// <remarks>
    /// ★★★ <strong>响应必须在 <c>next</c> <em>之前</em>换成缓冲流。</strong>
    /// 此前的写法是跑完管线再去加密，那时 <c>Response.Body</c> 已经是 Kestrel 的只写流：
    /// 回读抛 <c>NotSupportedException</c>，赋一个新的 <c>MemoryStream</c> 也送不出任何字节，
    /// 而外层异常中间件看到 <c>HasStarted</c> 只会记一条 "Response has already started"。
    /// 结果是<b>配置里写着加密开着，响应逐字明文发出</b> —— 这个特性从未生效过。
    /// </remarks>
    public async Task InvokeAsync(HttpContext context, RequestDelegate next)
    {
        Check.NotNull(context);
        Check.NotNull(next);

        // 解密顺带完成协商：客户端公钥就在请求头里。
        await _hostHttpCrypto.DecryptRequest(context.Request);

        if (!_hostHttpCrypto.IsResponseEncryptionNegotiated)
        {
            // 没协商就没有加密可言，也就不必为每个请求多缓冲一份响应。
            await next(context);
            return;
        }

        var connectionStream = context.Response.Body;
        using var buffer = new MemoryStream();
        context.Response.Body = buffer;

        try
        {
            await next(context);
            await _hostHttpCrypto.EncryptResponse(context.Response);

            // ★ 顺序要紧：先取走产物再交还真实流。EncryptResponse **替换** Response.Body，
            //   所以产物不一定是进来时那个缓冲区；先交还就再也拿不到它了。
            var produced = context.Response.Body;
            context.Response.Body = connectionStream;
            await CopyToConnectionAsync(produced, connectionStream);
        }
        catch
        {
            // ★★ 缓冲里那半截响应<strong>刻意丢掉</strong>。下游写到一半抛出时，
            //    那几个字节是一个不完整的载荷；把它送出去，只会和外层异常中间件
            //    随后写的错误信封拼在一起，客户端两样都解析不了。
            //    交还真实流，让那个信封成为客户端看到的唯一内容 ——
            //    缓冲的副作用是 HasStarted 仍为 false，所以那个信封确实写得出去。
            context.Response.Body = connectionStream;
            throw;
        }
    }

    /// <summary>
    /// 把缓冲下来的（可能已加密的）响应体写到真正的连接流上。
    /// </summary>
    private static async Task CopyToConnectionAsync(Stream produced, Stream connectionStream)
    {
        if (ReferenceEquals(produced, connectionStream) || !produced.CanRead)
        {
            return;
        }

        if (produced.CanSeek)
        {
            produced.Position = 0;
        }

        await produced.CopyToAsync(connectionStream);
    }
}
