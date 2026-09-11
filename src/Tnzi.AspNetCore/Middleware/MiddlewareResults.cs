
namespace Tnzi.AspNetCore.Middleware;

/// <summary>
/// 中间件里写失败响应的唯一出口。
/// </summary>
/// <remarks>
/// ★★★ <strong>存在的理由是「手拼的信封会长得不一样，而没有任何东西会告诉你」。</strong>
/// 控制器与异常中间件都经 <see cref="ApiResult"/> 出信封，字段是
/// <c>succeeded / code / message / errorCode / errorDetails</c>；
/// 而中间件里手写一个匿名对象时，业务错误码很自然会被叫成 <c>error</c> ——
/// 序列化成功、状态码正确、前端拿到 200 以外的码也照常显示 message，
/// 只是那个专门用来分支处理的字段<b>读不到</b>。租户中间件此前就是这样。
/// 走这里就不可能拼错，因为形状由 <see cref="ApiResult"/> 自己决定。
/// </remarks>
internal static class MiddlewareResults
{
    /// <summary>
    /// 按标准信封写一条失败响应。
    /// </summary>
    /// <param name="context">HTTP 上下文。</param>
    /// <param name="statusCode">HTTP 状态码，同时作为信封里的 <c>code</c>。</param>
    /// <param name="message">面向调用方的英文消息。</param>
    /// <param name="errorCode">业务错误码，供前端分支处理。</param>
    internal static async Task WriteErrorAsync(
        HttpContext context, int statusCode, string message, string? errorCode = null)
    {
        Check.NotNull(context);

        context.Response.StatusCode = statusCode;
        context.Response.ContentType = "application/json";

        var envelope = ApiResult.Error(message, statusCode, errorCode);
        await context.Response.WriteAsync(JsonSerializer.Serialize(envelope, TnziJsonDefaults.Options));
    }
}
