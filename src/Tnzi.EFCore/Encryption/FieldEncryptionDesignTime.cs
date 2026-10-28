namespace Tnzi.EFCore.Encryption;

/// <summary>
/// 设计期(<c>dotnet ef</c>)建模所需的字段加密开关。
/// </summary>
/// <remarks>
/// 设计期没有应用容器,<c>IsEncrypted()</c> 取不到真正的加密器,但列宽放宽的幅度取决于加密形态
/// (信封模式的密文更长)。框架的 <see cref="DesignTimeDbContextFactoryBase{TDbContext}"/> 会从 appsettings 读
/// <c>EFCore:FieldEncryption:Mode</c> 写入这里;手写设计期工厂的消费方,在启用信封模式时须在建上下文之前自己赋值,
/// 否则迁移里的列宽会按直接模式计算、与运行期模型不一致。
/// </remarks>
public static class FieldEncryptionDesignTime
{
    /// <summary>设计期假定的加密形态,默认 <see cref="FieldEncryptionMode.Direct"/>。</summary>
    public static FieldEncryptionMode Mode { get; set; } = FieldEncryptionMode.Direct;
}
