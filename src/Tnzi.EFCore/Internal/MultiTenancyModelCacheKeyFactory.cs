namespace Tnzi.EFCore.Internal;

/// <summary>
/// EF 模型缓存键工厂（包含多租户开关与主次上下文判定）
/// </summary>
/// <remarks>
/// 主次决定未指定上下文的实体进不进模型（见 <see cref="PrimaryDbContextResolver"/>），所以也进键：
/// 同一个上下文类型在不同的宿主里可能一次是主、一次是次，不能共用一份缓存的模型。
/// </remarks>
public sealed class MultiTenancyModelCacheKeyFactory : IModelCacheKeyFactory
{
    public object Create(DbContext context, bool designTime)
    {
        var enabled = context is IMultiTenancySwitchProvider provider && provider.IsMultiTenancyEnabled;
        return (context.GetType(), designTime, enabled, PrimaryDbContextResolver.IsPrimary(context));
    }
}
