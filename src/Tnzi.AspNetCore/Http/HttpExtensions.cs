
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