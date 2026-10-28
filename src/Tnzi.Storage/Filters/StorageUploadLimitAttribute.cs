namespace Tnzi.Storage.Filters;

/// <summary>
/// 上传端点的传输层上限：按 <see cref="StorageOptions.MaxFileSize"/> 的<b>当前值</b>逐请求设定请求体与 multipart 正文的上限。
/// </summary>
/// <remarks>
/// <para>
/// ★ <c>MaxFileSize</c> 是设置中心里可以热改的一项，业务闸门（<c>UploadGuard</c>）经 <c>IOptionsMonitor</c> 读当前值；
/// 而 <see cref="StorageModule"/> 放开的服务器级上限是启动时按配置算一次的常量。更糟的是设置中心的值在启动那一刻还不在
/// <c>IConfiguration</c> 里（它的配置源在应用初始化之后才从库里填充），所以那个常量只认 appsettings / 环境变量：
/// 管理员把 100MB 调到 500MB、界面提示保存成功，300MB 的文件照样被 Kestrel / IIS 以不透明的 413 拦下，
/// <c>UploadGuard</c> 根本没机会说话，重启也一样。
/// </para>
/// <para>
/// 这里在模型绑定之前（授权过滤器阶段，请求体还没读）把本请求的 <see cref="IHttpMaxRequestBodySizeFeature.MaxRequestBodySize"/>
/// 与表单的 <see cref="FormOptions.MultipartBodyLengthLimit"/> 设成 <c>MaxFileSize + </c><see cref="Allowance"/>，
/// 调大调小对传输层与业务闸门同时生效。特性只读（请求体已开始读取）或宿主不提供它时什么都不做，退回服务器级上限。
/// IIS 的 <c>requestFiltering.maxAllowedContentLength</c> 与反向代理的上限在这之外，仍是部署层的事。
/// </para>
/// <para>
/// 挂在每一个收文件的端点上（直传、批量上传、建版本、分片上传）；消费方覆写这些端点时保留这个特性，否则它们退回服务器级上限。
/// </para>
/// </remarks>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = false, Inherited = true)]
public sealed class StorageUploadLimitAttribute : Attribute, IAuthorizationFilter
{
    /// <summary>multipart 边界与表单字段的余量（与 <see cref="StorageModule"/> 放开服务器级上限时的口径一致）。</summary>
    public const long Allowance = 1L * 1024 * 1024;

    /// <inheritdoc />
    public void OnAuthorization(AuthorizationFilterContext context)
    {
        Check.NotNull(context);

        var http = context.HttpContext;
        var storageOptions = http.RequestServices.GetService<IOptionsMonitor<StorageOptions>>();
        if (storageOptions == null)
            return;

        var limit = storageOptions.CurrentValue.MaxFileSize + Allowance;

        var bodySize = http.Features.Get<IHttpMaxRequestBodySizeFeature>();
        if (bodySize is { IsReadOnly: false })
        {
            bodySize.MaxRequestBodySize = limit;
        }

        // 表单还没读：换一份只改了 multipart 正文上限的 FormOptions，其余各项沿用宿主的配置。
        var form = http.Features.Get<IFormFeature>();
        if (form?.Form == null)
        {
            var configured = http.RequestServices.GetService<IOptions<FormOptions>>()?.Value ?? new FormOptions();
            http.Features.Set<IFormFeature>(new FormFeature(http.Request, WithMultipartBodyLimit(configured, limit)));
        }
    }

    private static FormOptions WithMultipartBodyLimit(FormOptions source, long limit) => new()
    {
        BufferBody = source.BufferBody,
        MemoryBufferThreshold = source.MemoryBufferThreshold,
        BufferBodyLengthLimit = source.BufferBodyLengthLimit,
        ValueCountLimit = source.ValueCountLimit,
        KeyLengthLimit = source.KeyLengthLimit,
        ValueLengthLimit = source.ValueLengthLimit,
        MultipartBoundaryLengthLimit = source.MultipartBoundaryLengthLimit,
        MultipartHeadersCountLimit = source.MultipartHeadersCountLimit,
        MultipartHeadersLengthLimit = source.MultipartHeadersLengthLimit,
        MultipartBodyLengthLimit = limit
    };
}
