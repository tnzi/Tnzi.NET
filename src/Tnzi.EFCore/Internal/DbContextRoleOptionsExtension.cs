namespace Tnzi.EFCore.Internal;

/// <summary>
/// 设计期工厂写在 options 上的主次标记：这个上下文是不是主上下文。
/// </summary>
/// <remarks>
/// 运行期从应用容器里的 <see cref="RegisteredDbContext"/> 登记判定主次，设计期（<c>dotnet ef</c>）没有应用容器，
/// 由 <see cref="DesignTimeDbContextFactoryBase{TDbContext}"/> 按 <c>Database</c> 配置节判定后写在这里，
/// 两侧建出同一个模型。读取方是 <see cref="PrimaryDbContextResolver"/>。
/// </remarks>
internal sealed class DbContextRoleOptionsExtension : IDbContextOptionsExtension
{
    private DbContextOptionsExtensionInfo? _info;

    public DbContextRoleOptionsExtension(bool isPrimary)
    {
        IsPrimary = isPrimary;
    }

    /// <summary>是不是主上下文。</summary>
    public bool IsPrimary { get; }

    public DbContextOptionsExtensionInfo Info => _info ??= new ExtensionInfo(this);

    public void ApplyServices(IServiceCollection services)
    {
        // 只是 options 上的一个值，不向内部容器登记任何服务。
    }

    public void Validate(IDbContextOptions options)
    {
    }

    private sealed class ExtensionInfo(DbContextRoleOptionsExtension extension) : DbContextOptionsExtensionInfo(extension)
    {
        private new DbContextRoleOptionsExtension Extension => (DbContextRoleOptionsExtension)base.Extension;

        public override bool IsDatabaseProvider => false;

        public override string LogFragment => Extension.IsPrimary ? "PrimaryContext " : "SecondaryContext ";

        public override int GetServiceProviderHashCode() => Extension.IsPrimary.GetHashCode();

        public override bool ShouldUseSameServiceProvider(DbContextOptionsExtensionInfo other)
            => other is ExtensionInfo otherInfo && otherInfo.Extension.IsPrimary == Extension.IsPrimary;

        public override void PopulateDebugInfo(IDictionary<string, string> debugInfo)
        {
            debugInfo["Tnzi:PrimaryContext"] = Extension.IsPrimary.ToString();
        }
    }
}
