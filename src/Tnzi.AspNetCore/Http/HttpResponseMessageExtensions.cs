
namespace Tnzi.AspNetCore.Http;

/// <summary>
/// HttpResponseMessage扩展方法
/// </summary>
public static class HttpResponseMessageExtensions
{
    /// <summary>
    /// 由旧的<see cref="HttpResponseMessage"/>和新数据创建新的<see cref="HttpResponseMessage"/>
    /// </summary>
    /// <param name="response">原始响应</param>
    /// <param name="data">新数据</param>
    /// <returns>新的响应</returns>
    public static HttpResponseMessage CreateNew(this HttpResponseMessage response, string data)
    {
        Check.NotNull(response);
        Check.NotNull(data);

        var content = new StringContent(data, Encoding.UTF8, "application/json");
        
        // 复制原始Content的Headers（如果存在）
        if (response.Content != null)
        {
            foreach (var header in response.Content.Headers)
            {
                content.Headers.TryAddWithoutValidation(header.Key, header.Value);
            }
        }
        
        response.Content = content;
        return response;
    }
}