
namespace Tnzi.AspNetCore.Http;

/// <summary>
/// HTTP 扩展方法（用于ASP.NET Core的HttpRequest和HttpResponse）
/// </summary>
public static class HttpExtensions
{
    /// <summary>
    /// 读取<see cref="HttpRequest"/>的Body为字符串
    /// </summary>
    /// <param name="request">HTTP请求</param>
    /// <returns>Body内容</returns>
    public static async Task<string> ReadAsStringAsync(this HttpRequest request)
    {
        Check.NotNull(request);
        if (request.Body == null)
            return string.Empty;

        // 保存原始流位置
        request.EnableBuffering();
        request.Body.Position = 0;

        using var reader = new StreamReader(request.Body, Encoding.UTF8, leaveOpen: true);
        string content = await reader.ReadToEndAsync();
        
        // 重置流位置，以便后续读取
        request.Body.Position = 0;
        
        return content;
    }

    /// <summary>
    /// 有界读取 <see cref="HttpRequest"/> 的 Body：最多读 <paramref name="maxBytes"/> + 1 字节，超过上限返回 <c>null</c>，
    /// 否则返回 UTF-8 解码的内容。读完把流位置放回 0 供下游再读；不可定位的流先 <see cref="HttpRequestRewindExtensions.EnableBuffering(HttpRequest)"/>。
    /// </summary>
    /// <remarks>
    /// 中间件在认证之前读请求体（签名校验、请求日志）时必须用它而不是 <see cref="ReadAsStringAsync(HttpRequest)"/>：
    /// 没有 Content-Length（chunked）的请求「先读到底再查大小」等于让任何调用方决定服务端分配多大的字符串。
    /// </remarks>
    /// <param name="request">HTTP 请求</param>
    /// <param name="maxBytes">允许的最大字节数（含）</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>Body 内容；超过 <paramref name="maxBytes"/> 时为 <c>null</c></returns>
    public static async Task<string?> TryReadAsStringAsync(this HttpRequest request, int maxBytes, CancellationToken cancellationToken = default)
    {
        Check.NotNull(request);
        Check.GreaterThanOrEqual(maxBytes, 0);
        if (request.Body == null)
            return string.Empty;

        if (!request.Body.CanSeek)
        {
            request.EnableBuffering();
        }

        request.Body.Position = 0;

        // 多读一个字节只为判断「超了」；maxBytes 顶到 int.MaxValue 时不再加一，避免溢出。
        var limit = (int)Math.Min((long)maxBytes + 1, int.MaxValue);
        var buffer = ArrayPool<byte>.Shared.Rent(limit);
        try
        {
            var total = 0;
            while (total < limit)
            {
                var read = await request.Body.ReadAsync(buffer.AsMemory(total, limit - total), cancellationToken);
                if (read == 0)
                {
                    break;
                }

                total += read;
            }

            request.Body.Position = 0;

            return total > maxBytes ? null : Encoding.UTF8.GetString(buffer, 0, total);
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    /// <summary>
    /// 读取<see cref="HttpResponse"/>的Body为字符串
    /// </summary>
    /// <param name="response">HTTP响应</param>
    /// <returns>Body内容</returns>
    public static async Task<string> ReadAsStringAsync(this HttpResponse response)
    {
        Check.NotNull(response);
        if (response.Body == null)
            return string.Empty;

        // 保存原始流位置
        long originalPosition = response.Body.Position;
        response.Body.Position = 0;

        using var reader = new StreamReader(response.Body, Encoding.UTF8, leaveOpen: true);
        string content = await reader.ReadToEndAsync();
        
        // 重置流位置
        response.Body.Position = originalPosition;
        
        return content;
    }

    /// <summary>
    /// 设置<see cref="HttpRequest"/>的Body为指定字符串
    /// </summary>
    /// <param name="request">HTTP请求</param>
    /// <param name="data">要写入的数据</param>
    /// <returns>HTTP请求</returns>
    public static Task<HttpRequest> WriteBodyAsync(this HttpRequest request, string data)
    {
        Check.NotNull(request);
        if (request.Method == HttpMethods.Get)
        {
            return Task.FromResult(request);
        }

        if (string.IsNullOrEmpty(data))
        {
            request.Body = new MemoryStream();
            request.ContentLength = 0;
            return Task.FromResult(request);
        }

        byte[] bytes = data.ToBytes();
        request.ContentLength = bytes.Length;
        request.Body = new MemoryStream(bytes);
        return Task.FromResult(request);
    }

    /// <summary>
    /// 设置<see cref="HttpResponse"/>的Body为指定字符串
    /// </summary>
    /// <param name="response">HTTP响应</param>
    /// <param name="data">要写入的数据</param>
    /// <returns>HTTP响应</returns>
    public static Task<HttpResponse> WriteBodyAsync(this HttpResponse response, string data)
    {
        Check.NotNull(response);

        if (string.IsNullOrEmpty(data))
        {
            response.ContentLength = 0;
            return Task.FromResult(response);
        }

        byte[] bytes = data.ToBytes();
        response.ContentLength = bytes.Length;
        response.Body = new MemoryStream(bytes);
        return Task.FromResult(response);
    }

    /// <summary>
    /// 获取一个值, 该值指示 HTTP 响应是否成功
    /// </summary>
    /// <param name="response">HTTP响应</param>
    /// <returns>是否成功</returns>
    public static bool IsSuccessStatusCode(this HttpResponse response)
    {
        Check.NotNull(response);

        return response.StatusCode >= 200 && response.StatusCode <= 299;
    }

    /// <summary>
    /// 从请求头中获取指定键的值，如果不存在则返回默认值
    /// </summary>
    /// <param name="headers">请求头集合</param>
    /// <param name="key">键名</param>
    /// <returns>值或null</returns>
    public static string? GetOrDefault(this IHeaderDictionary headers, string key)
    {
        if (headers == null || string.IsNullOrEmpty(key))
            return null;

        if (headers.TryGetValue(key, out var value))
        {
            return value.ToString();
        }

        return null;
    }
}