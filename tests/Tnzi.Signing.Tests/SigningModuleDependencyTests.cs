using Microsoft.Extensions.DependencyInjection;
using Tnzi.Documents;
using Tnzi.Modules;
using Tnzi.Storage;

namespace Tnzi.Signing.Tests;

/// <summary>
/// 本模块声明的依赖必须覆盖它<b>必填注入</b>的每一个跨模块服务。
/// </summary>
/// <remarks>
/// <para>
/// 存在的理由：<c>EnvelopeService</c> / <c>SigningSealer</c> / <c>SigningCertificateBuilder</c>
/// 三处都在构造函数里<b>必填</b>接收 <c>IFileStorageService</c>（<c>Tnzi.Storage</c> 注册），
/// 而它一度只声明了 <c>[DependsOn(typeof(DocumentsModule))]</c>。缺这条声明的后果不是启动报错，
/// 而是<b>启动照常成功</b>，直到某个请求第一次解析 <c>IEnvelopeService</c> 才崩在 DI 容器里 ——
/// 这正是 <c>[DependsOn]</c> 要消灭的失败形态（它让 <c>ModuleLoader</c> 递归把依赖带上，
/// 真缺失时在启动期 fail-fast 并打印完整依赖链）。
/// </para>
/// <para>
/// 这道测试同时是那条模块注释的守卫：注释说「不依赖任何<b>领域</b>业务模块，存储底座除外」。
/// 若日后有人为了让「不依赖任何业务模块」字面成真而删掉 <c>StorageModule</c>，这里会立刻红，
/// 而不是等某个消费应用在生产里发现。
/// </para>
/// </remarks>
public class SigningModuleDependencyTests
{
    private static IReadOnlyList<IModuleDescriptor> LoadFromSigningAlone()
        => new ModuleLoader().LoadModules(new ServiceCollection(), typeof(SigningModule));

    [Fact]
    public void Loading_signing_alone_brings_storage_along()
    {
        var modules = LoadFromSigningAlone();

        modules.Select(m => m.Type).ShouldContain(typeof(StorageModule),
            "Signing 必填注入 IFileStorageService，StorageModule 必须被 [DependsOn] 带进模块图");
    }

    [Fact]
    public void Loading_signing_alone_brings_documents_along()
    {
        var modules = LoadFromSigningAlone();

        modules.Select(m => m.Type).ShouldContain(typeof(DocumentsModule),
            "Signing 消费 IPdfStamper / IPdfInspector，DocumentsModule 必须在模块图里");
    }

    [Fact]
    public void Storage_is_ordered_before_signing()
    {
        var modules = LoadFromSigningAlone().Select(m => m.Type).ToList();
        var storageIndex = modules.IndexOf(typeof(StorageModule));
        var signingIndex = modules.IndexOf(typeof(SigningModule));

        // 先确认两者都在图里再比顺序：IndexOf 对缺席者返回 -1，而 -1 小于任何有效下标，
        // 光比大小的话「Storage 根本没被加载」会伪装成「顺序正确」而静默通过。
        storageIndex.ShouldBeGreaterThanOrEqualTo(0, "StorageModule 必须在模块图里");
        signingIndex.ShouldBeGreaterThanOrEqualTo(0, "SigningModule 必须在模块图里");

        // Storage(30) 注册 IFileStorageService，Signing(45) 消费它：
        // 拓扑排序必须把提供方排在消费方之前，否则 Signing 的 ConfigureServicesAsync
        // 拿不到已注册的存储服务。
        storageIndex.ShouldBeLessThan(signingIndex);
    }
}
