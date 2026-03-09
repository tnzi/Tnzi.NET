namespace Tnzi.EFCore.Internal;

/// <summary>
/// EF 模型缓存键工厂（包含多租户开关）
/// </summary>
public sealed class MultiTenancyModelCacheKeyFactory : IModelCacheKeyFactory
{
    public object Create(DbContext context, bool designTime)
    {
        var enabled = context is IMultiTenancySwitchProvider provider && provider.IsMultiTenancyEnabled;
        return (context.GetType(), designTime, enabled);
    }
}
