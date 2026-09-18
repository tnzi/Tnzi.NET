namespace Tnzi.Localization.Tests;

/// <summary>
/// 探针资源类：对应 <c>Resources/ResxProbeResource.resx</c>（中性资源，只有一个键 <c>Probe.Hit</c>），
/// 用来证明 Resx 模式下命中的键不会被记成缺失。放在根命名空间，让 ResourceManager 的基名推导
/// （<c>{RootNamespace}.{ResourcesPath}.{TypeName}</c>）落在 <c>Tnzi.Localization.Tests.Resources.ResxProbeResource</c>。
/// </summary>
public class ResxProbeResource
{
}
