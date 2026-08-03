namespace Tnzi.Documents.Signing;

/// <summary>
/// 电子签署子模块：可复用模板 + 放置字段 + 多收件人签署流程 + 密封成品。
/// </summary>
/// <remarks>
/// <para>
/// <b>它刻意不依赖任何业务模块。</b>一份文档通过 <c>HostEntityType</c> + <c>HostEntityId</c>
/// 多态绑定到它的宿主记录；业务模块从自己那侧插两个扩展点进来：
/// <see cref="Services.IMergeSourceProvider"/>（这类记录能提供哪些合并变量、怎么解析）与
/// <see cref="Services.IDocumentHostSink"/>（签完的 PDF 归档到哪）。依赖方向永远是
/// 业务模块 → 本模块，绝不反向。
/// </para>
/// <para>
/// 因此它的 <see cref="LoadOrder"/> 排在业务模块<b>之前</b>：它们 <c>[DependsOn]</c> 本模块，
/// 而不是反过来。
/// </para>
/// <para>
/// PDF 本身的活不在这里做 —— 转换、读页定位、盖章压平都来自可选包 <c>Tnzi.Documents</c>。
/// </para>
/// </remarks>
[DependsOn(typeof(DocumentsModule))]
public class SigningModule : TnziApplicationModule
{
    /// <inheritdoc />
    public override string? TableNamePrefix => "Signing";

    /// <summary>
    /// 排在业务模块之前加载：它们依赖本模块的扩展点契约，反向依赖不存在。
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
