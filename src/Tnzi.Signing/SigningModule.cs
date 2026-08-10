namespace Tnzi.Signing;

/// <summary>
/// 电子签署模块：可复用模板 + 放置字段 + 多收件人签署流程 + 密封成品。
/// </summary>
/// <remarks>
/// <para>
/// <b>它刻意不依赖任何领域业务模块</b>（合同、订单、HR 记录等）。一份文档通过
/// <c>HostEntityType</c> + <c>HostEntityId</c> 多态绑定到它的宿主记录；领域模块从自己那侧
/// 插两个扩展点进来：<see cref="Services.IMergeSourceProvider"/>（这类记录能提供哪些合并
/// 变量、怎么解析）与 <see cref="Services.IDocumentHostSink"/>（签完的 PDF 归档到哪）。
/// 依赖方向永远是领域模块 → 本模块，绝不反向。
/// </para>
/// <para>
/// <b>存储底座 <c>Tnzi.Storage</c> 是有意的例外</b>：原件、中间态、密封成品、完成证书都要有
/// 确定的落地位置，本模块直接经 <c>IFileStorageService</c> 存取，故 <c>[DependsOn]</c> 了
/// <see cref="StorageModule"/>。它是全框架的存储底座而非某个业务领域，与上一条不矛盾 ——
/// 少了这条声明，一个加载本模块却没加载 Storage 的应用会照常启动，直到首次解析
/// <see cref="Services.IEnvelopeService"/> 才崩在 DI 容器里。
/// </para>
/// <para>
/// <c>EFCoreModule</c> 同样是直接声明而不是靠 <c>StorageModule</c> 传递：本模块自带
/// <c>Signing_</c> 前缀的表、直接消费 <c>IRepository&lt;,&gt;</c>，把这条依赖挂在别人的
/// 依赖链上，只要 Storage 哪天不再依赖 EFCore 就会断。
/// </para>
/// <para>
/// 因此它的 <see cref="LoadOrder"/> 排在<b>消费它的</b>领域业务模块之前：它们
/// <c>[DependsOn]</c> 本模块，而不是反过来。（它自身排在 <see cref="StorageModule"/> 之后。）
/// </para>
/// <para>
/// PDF 本身的活不在这里做 —— 转换、读页定位、盖章压平都来自可选包 <c>Tnzi.Documents</c>。
/// </para>
/// </remarks>
[DependsOn(typeof(DocumentsModule), typeof(EFCoreModule), typeof(StorageModule))]
public class SigningModule : TnziApplicationModule
{
    /// <inheritdoc />
    public override string? TableNamePrefix => "Signing";

    /// <summary>
    /// 排在<b>消费本模块的</b>领域业务模块之前加载：它们依赖本模块的扩展点契约，反向依赖不存在。
    /// 本模块自身排在它依赖的 <see cref="StorageModule"/>（30）之后。
    /// </summary>
    public override int LoadOrder => 45;

    /// <inheritdoc />
    public override Task ConfigureServicesAsync(ServiceConfigurationContext context)
    {
        Check.NotNull(context);

        // 权限目录自声明：不注册的话这 9 个码永远不会被播种，而管理端点上的
        // [ApiAuthorize] 引用的正是它们 —— 结果是每个端点恒 403，且没有任何一处
        // 会告诉你为什么。
        context.Services.AddTransient<IPermissionDefinitionProvider, SigningPermissions>();

        // 把业务模块注册进来的 provider / sink 按宿主类型归拢。
        // 本模块从不点名它们中的任何一个。
        context.Services.TryAddScoped<IMergeSourceRegistry, MergeSourceRegistry>();

        // 密封器（盖章 + 压平 + 哈希 + 落文件）。internal 语义，但走 DI 便于替换与测试。
        context.Services.TryAddScoped<SigningSealer>();
        context.Services.TryAddScoped<SigningCertificateBuilder>();
        context.Services.TryAddScoped<ComposedDocumentRenderer>();

        context.Services.TryAddScoped<IEnvelopeTemplateService, EnvelopeTemplateService>();
        context.Services.TryAddScoped<IEnvelopeService, EnvelopeService>();

        return Task.CompletedTask;
    }
}
