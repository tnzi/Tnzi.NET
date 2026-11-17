namespace Tnzi.Identity.OpenIdConnect.Services;

/// <summary>
/// 让 OpenIddict 的 EF 存储用应用的<b>主</b> DbContext。
/// </summary>
/// <remarks>
/// OpenIddict 自带的 <c>UseDbContext&lt;T&gt;()</c> 要在编译期知道上下文类型，而框架的主上下文是消费方的类型、
/// 由配置决定。框架把主上下文同时登记为基类 <see cref="DbContext"/>，这里就解析它 —— 本模块四个实体的
/// 配置不绑定上下文（归默认组），也正是只进主上下文。
/// </remarks>
public sealed class OidcPrimaryDbContextAccessor : IOpenIddictEntityFrameworkCoreContext
{
    private readonly IServiceProvider _serviceProvider;

    /// <summary>创建访问器。</summary>
    public OidcPrimaryDbContextAccessor(IServiceProvider serviceProvider)
    {
        _serviceProvider = Check.NotNull(serviceProvider);
    }

    /// <inheritdoc />
    public ValueTask<DbContext> GetDbContextAsync(CancellationToken cancellationToken)
    {
        var context = _serviceProvider.GetService<DbContext>()
            ?? throw new InvalidOperationException(
                "The OpenID Connect provider needs the primary DbContext. Register the application's DbContext with AddTnziDbContext(..., isPrimary: true).");
        return ValueTask.FromResult(context);
    }
}
