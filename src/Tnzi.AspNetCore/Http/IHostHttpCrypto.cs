
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
    /// <remarks>
    /// 它同时是<b>协商</b>那一步：客户端公钥经请求头带来，本方法把它读进来建出本次请求的加密器，
    /// <see cref="EncryptResponse"/> 之后才有东西可用。GET 没有请求体可解密，
    /// 但协商照做 —— 否则 GET 的响应会是唯一一条明文出口。
    /// </remarks>
    Task<HttpRequest> DecryptRequest(HttpRequest request);

    /// <summary>
    /// 本次请求是否协商出了响应加密（客户端带来了公钥）。
    /// </summary>
    /// <remarks>
    /// 中间件据此决定要不要为响应装缓冲流。默认 <c>true</c> 是刻意的：
    /// 第三方实现没实现这个属性时，宁可多缓冲一次，也不要让它的响应<b>安静地</b>不加密。
    /// </remarks>
    bool IsResponseEncryptionNegotiated => true;

    /// <summary>
    /// 加密发往客户端的响应。
    /// </summary>
    /// <param name="response">未加密的响应。</param>
    /// <returns>加密后的响应。</returns>
    /// <remarks>
    /// ★★★ <strong>契约：调用方必须先把 <c>Response.Body</c> 换成可读可定位的缓冲流，
    /// 并在本方法返回后把 <c>Response.Body</c> 的内容写到真正的连接流上。</strong>
    /// 本方法<b>替换</b> <c>Response.Body</c>（连同 <c>ContentLength</c>），它自己送不出任何字节。
    /// <c>HostHttpCryptoMiddleware</c> 负责这两头。
    /// <para>
    /// 这条契约不是设计洁癖，是这个特性此前<b>从未生效</b>的原因：管线跑完之后
    /// <c>Response.Body</c> 已是 Kestrel 的只写流，回读抛 <c>NotSupportedException</c>，
    /// 而赋一个新 <c>MemoryStream</c> 更不会把字节送出去 —— 响应逐字明文发出，
    /// 日志里只留下外层异常中间件的一条 "Response has already started"。
    /// </para>
    /// </remarks>
    Task<HttpResponse> EncryptResponse(HttpResponse response);
}