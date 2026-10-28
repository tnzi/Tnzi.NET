namespace Tnzi.Hosting.Events.Handlers;

/// <summary>
/// 邮件里指向后端端点（<c>auth/confirm-email</c>、<c>auth/reset-password</c>）的链接的根地址。
/// </summary>
/// <remarks>
/// <para>
/// <c>System:ApiBaseUrl</c> 是 API 的 origin（部署在子路径下时连同子路径），<b>不含</b> API 前缀；
/// 控制器经 <c>RoutePrefixConvention</c> 挂在 <c>AspNetCore:ApiPathPrefix</c>（默认 <c>/api</c>）之下，
/// 所以前缀由这里补上。此前两个处理器直接拼 <c>{ApiBaseUrl}/auth/...</c>：默认前缀下确认与重置链接全是 404，
/// 邮件照常发出、日志写着「sent」，开了 <c>RequireConfirmedEmail</c> 的部署里新用户永远确认不了、登不进去。
/// </para>
/// <para>
/// 已经以前缀结尾的基础地址（为绕开上面那个缺陷手工写成 <c>https://host/api</c> 的部署）不再追加，
/// 否则升级后链接变成 <c>/api/api/...</c>。
/// </para>
/// </remarks>
internal static class ApiRootUrl
{
    /// <summary>在 API 的基础地址（origin，可带部署子路径）后补上 API 路径前缀。</summary>
    public static string Compose(string baseUrl, AspNetCoreOptions aspNetCoreOptions)
    {
        var root = baseUrl.TrimEnd('/');
        var prefix = (aspNetCoreOptions.ApiPathPrefix ?? string.Empty).Trim().Trim('/');
        if (root.Length == 0 || prefix.Length == 0)
        {
            return root;
        }

        return root.EndsWith("/" + prefix, StringComparison.OrdinalIgnoreCase) ? root : $"{root}/{prefix}";
    }

    /// <summary>
    /// 只知道前端地址时（假设同源部署）推出 API 的基础地址：前端的 origin + <c>AspNetCore:PathBase</c>。
    /// 前端地址不是绝对地址时返回空串。
    /// </summary>
    public static string FromFrontendOrigin(string frontendUrl, AspNetCoreOptions aspNetCoreOptions)
    {
        if (!Uri.TryCreate(frontendUrl, UriKind.Absolute, out var uri))
        {
            return string.Empty;
        }

        var pathBase = (aspNetCoreOptions.PathBase ?? string.Empty).Trim().TrimEnd('/');
        return $"{uri.Scheme}://{uri.Authority}{pathBase}";
    }
}
