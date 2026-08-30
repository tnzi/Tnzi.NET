namespace Tnzi.Storage.Cloud;

/// <summary>
/// 对象存储子模块：把 Amazon S3 / Cloudflare R2 / Azure Blob 三个 provider 从 <c>Tnzi.Storage</c> 里分出来。
/// </summary>
/// <remarks>
/// <para>
/// <b>业务范围</b>：把文件存到云上的对象存储。文件落本地磁盘（<c>Storage:Provider=Local</c>，
/// 默认值）或只在测试里用内存 provider 的应用不加载它，于是 <c>AWSSDK.S3</c> 与
/// <c>Azure.Storage.Blobs</c> 连同它们的传递包（<c>Azure.Core</c>、<c>Microsoft.Identity.Client</c>
/// 及其身份链）都不进入依赖闭包。
/// </para>
/// <para>
/// <b>为什么三个 provider 一个包。</b> S3 与 R2 共用 <c>AWSSDK.S3</c>（R2 是 S3 兼容 API），
/// 只用 S3 的部署确实还背着 <c>Azure.Storage.Blobs</c>。但那是同一条论证再往下切一层，
/// 而三个 provider 类拆成两个包的代价（两份模块、两份文档、两份装配说明）高于它省下的东西。
/// 真要再切，判据是「某一家的 SDK 大到值得单独一个包」，而不是「它们来自不同的厂商」。
/// </para>
/// <para>
/// <b>无实体无表</b>，故用 <see cref="TnziCustomModule"/> 且不声明表前缀；无控制器、无权限码，
/// 加载与否不改变任何 HTTP 面。配置节也不新增：三家的选项仍是父模块 <c>StorageOptions</c> 上的
/// <c>Storage:S3</c> / <c>Storage:R2</c> / <c>Storage:Azure</c>，路径一字不变，本模块经项目引用读它们。
/// 选项的校验同样留在父模块的 <c>StorageOptionsValidator</c> 里：配置写错要在启动时说话，
/// 而这件事不该取决于有没有加载本模块。
/// </para>
/// <para>
/// <b>缺席时的行为</b>：<c>Storage:Provider</c> 配成 <c>S3</c> / <c>R2</c> / <c>Azure</c> 而没有加载本模块时，
/// <see cref="StorageProviderFactory"/> 解析不到该名字，<b>抛异常</b>并列出已注册的 provider 与要加载的模块。
/// 它绝不回退到本地磁盘：那样文件会被默默写到没人预期的地方，而配置、日志和接口返回全都是正常的，
/// 等到发现时本地盘上已经堆了一批本该在对象存储里的文件。
/// </para>
/// </remarks>
[DependsOn(typeof(StorageModule))]
public class StorageCloudModule : TnziCustomModule
{
    /// <summary>Storage(30) 之后；实际次序由 <c>[DependsOn]</c> 拓扑排序保证，此值仅为同级 tiebreak。</summary>
    public override int LoadOrder => 31;

    /// <summary>
    /// 把三个 provider 注册进 <see cref="StorageProviderFactory"/>。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 这里不是往 DI 容器里加服务，而是往工厂的静态注册表里加名字，所以放在 <b>PreConfigure</b> 阶段：
    /// 那正是 <see cref="StorageProviderFactory.Register"/> 给所有 provider 作者（包括消费方自己写的）
    /// 指定的扩展点，本模块没有理由走另一条路。父模块的 <c>IFileStorage</c> 单例是延迟解析的工厂委托，
    /// 真正读注册表要到第一次用到存储时，这一步远在其前。
    /// </para>
    /// <para>
    /// 名字与拆分前逐字相同（<c>s3</c> / <c>r2</c> / <c>azure</c>，大小写不敏感），
    /// 所以既有配置不需要改一个字符。消费方要换掉其中某一家的实现，仍是在自己模块里
    /// 用同名再 <c>Register</c> 一次覆盖 —— 本模块不改这条约定。
    /// </para>
    /// </remarks>
    public override Task PreConfigureServicesAsync(ServiceConfigurationContext context)
    {
        StorageProviderFactory.Register("s3", ctx =>
        {
            if (ctx.Options.S3 == null)
                throw new InvalidOperationException("S3 options are required when Provider is S3.");
            ILogger<S3Storage>? logger = ctx.LoggerFactory?.CreateLogger<S3Storage>();
            return new S3Storage(ctx.Options.S3, ctx.Configuration, logger, ctx.OptionsMonitor);
        });

        StorageProviderFactory.Register("r2", ctx =>
        {
            if (ctx.Options.R2 == null)
                throw new InvalidOperationException("R2 options are required when Provider is R2.");
            ILogger<R2Storage>? logger = ctx.LoggerFactory?.CreateLogger<R2Storage>();
            return new R2Storage(ctx.Options.R2, ctx.Configuration, logger, ctx.OptionsMonitor);
        });

        StorageProviderFactory.Register("azure", ctx =>
        {
            if (ctx.Options.Azure == null)
                throw new InvalidOperationException("Azure options are required when Provider is Azure.");
            ILogger<AzureBlobStorage>? logger = ctx.LoggerFactory?.CreateLogger<AzureBlobStorage>();
            return new AzureBlobStorage(ctx.Options.Azure, ctx.Configuration, logger, ctx.OptionsMonitor);
        });

        return Task.CompletedTask;
    }
}
