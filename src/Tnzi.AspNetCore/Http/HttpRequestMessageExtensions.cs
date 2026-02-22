
namespace Tnzi.AspNetCore.Http;

/// <summary>
/// HttpRequestMessage扩展方法
/// </summary>
public static class HttpRequestMessageExtensions
{
    /// <summary>
    /// 由旧的<see cref="HttpRequestMessage"/>和新数据创建新的<see cref="HttpRequestMessage"/>
    /// </summary>
    /// <param name="request">原始请求</param>
    /// <param name="data">新数据</param>
    /// <returns>新的请求</returns>
    public static HttpRequestMessage CreateNew(this HttpRequestMessage request, string data)
    {
        Check.NotNull(request);
        Check.NotNull(data);

        var content = new StringContent(data, Encoding.UTF8, "application/json");
        
        // 复制原始Content的Headers（如果存在）
        if (request.Content != null)
        {
            foreach (var header in request.Content.Headers)
            {
                content.Headers.TryAddWithoutValidation(header.Key, header.Value);
            }
        }
        
        request.Content = content;
        return request;
    }
}