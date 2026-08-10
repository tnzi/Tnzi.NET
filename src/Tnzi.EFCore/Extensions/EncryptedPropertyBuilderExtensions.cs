using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace Tnzi.EFCore.Extensions;

/// <summary>
/// 把实体属性声明为「落库前加密、读取后解密」。
/// </summary>
/// <remarks>
/// <para>
/// 用法（在 <see cref="EntityTypeConfigurationBase{TEntity, TKey}"/> 的 Configure 里）：
/// <code>
/// builder.Property(e => e.IdCardNumber)
///        .HasMaxLength(32)
///        .IsEncrypted();
/// </code>
/// </para>
/// <para>
/// <strong>这是一项可选能力。</strong>不调用本扩展的应用完全不受影响，
/// 框架不会为它们注册加密器、也不会做任何额外扫描。
/// </para>
/// <para>
/// <strong>被加密的列查不了。</strong>密文每次加密都带新的随机数，同一明文两次加密结果不同，
/// 因此该列<strong>无法</strong>用于 <c>Where</c> 等值比较、排序、<c>LIKE</c> 或建唯一索引。
/// 需要按该值查找时，另建一列存确定性哈希（如 <c>HashHelper</c> 产出）并对哈希列建索引。
/// </para>
/// </remarks>
public static class EncryptedPropertyBuilderExtensions
{
    /// <summary>
    /// AES-GCM 的随机数与认证标签开销（12 + 16 字节），再加版本与密钥标识前缀的余量。
    /// </summary>
    private const int CipherOverheadBytes = 64;

    /// <summary>
    /// 将 <see cref="string"/> 属性声明为加密列。
    /// </summary>
    /// <param name="builder">属性构建器。</param>
    /// <param name="purpose">
    /// 用途标识，参与密文认证，防止密文被从别的列复制过来。
    /// 缺省按 <c>实体类型全名.属性名</c> 推导，<strong>一经上线不可更改</strong>：
    /// 改了它，用旧标识加密的存量数据将无法解密。
    /// </param>
    /// <exception cref="InvalidOperationException">
    /// 未启用字段级加密，或未注册 <see cref="IFieldEncryptor"/>。
    /// <strong>刻意抛出而不是静默降级为明文</strong>：声明了要加密却明文落库，
    /// 是这类功能最危险的失败方式。
    /// </exception>
    /// <remarks>
    /// 可空属性用同一个方法：可空引用类型的注解不参与重载解析，
    /// <c>PropertyBuilder&lt;string?&gt;</c> 与 <c>PropertyBuilder&lt;string&gt;</c> 是同一个类型。
    /// <strong><c>null</c> 不会进入转换器</strong>，EF Core 对 <c>null</c> 直接透传，
    /// 因此空值仍然以 <c>NULL</c> 落库（而不是"某个固定密文"，那会泄露"这一行是空的"以外的信息）。
    /// </remarks>
    public static PropertyBuilder<string> IsEncrypted(this PropertyBuilder<string> builder, string? purpose = null)
    {
        Check.NotNull(builder);
        var encryptor = ResolveEncryptorOrThrow(builder);
        var resolved = purpose ?? DerivePurpose(builder);

        builder.HasConversion(new ValueConverter<string, string>(
            plain => encryptor.Encrypt(plain, resolved),
            stored => encryptor.IsEncrypted(stored) ? encryptor.Decrypt(stored, resolved) : stored));

        RelaxMaxLength(builder);
        return builder;
    }

    /// <summary>
    /// 从属性元数据推导用途标识：<c>实体类型全名.属性名</c>。
    /// </summary>
    private static string DerivePurpose(PropertyBuilder builder)
    {
        var property = builder.Metadata;
        var declaring = property.DeclaringType.ClrType.FullName ?? property.DeclaringType.Name;
        return $"{declaring}.{property.Name}";
    }

    /// <summary>
    /// 密文比明文长，按原长度放宽列长上限，避免上线后才在写入时撞见截断。
    /// </summary>
    /// <remarks>
    /// 只放宽、不收紧；未设长度上限的属性不做处理。
    /// 这是刻意的越权：忘记调整长度是必然会发生的疏忽，而它的表现是运行期写入失败。
    /// </remarks>
    private static void RelaxMaxLength(PropertyBuilder builder)
    {
        var current = builder.Metadata.GetMaxLength();
        if (current is not > 0)
        {
            return;
        }

        // Base64 膨胀 4/3，另加随机数、标签与前缀开销。
        var expanded = (int)Math.Ceiling((current.Value + CipherOverheadBytes) * 4d / 3d);
        builder.Metadata.SetMaxLength(expanded);
    }

    /// <summary>
    /// 解析应用层注册的 <see cref="IFieldEncryptor"/>。
    /// </summary>
    /// <remarks>
    /// 走 EF Core 官方途径拿应用服务提供程序（<c>CoreOptionsExtension.ApplicationServiceProvider</c>），
    /// 而不是把加密器塞进静态字段，以免测试之间互相污染。
    /// </remarks>
    private static IFieldEncryptor ResolveEncryptorOrThrow(PropertyBuilder builder)
    {
        var dbContext = EntityConfigurationContext.GetCurrentDbContextValue();
        var appServices = dbContext?
            .GetService<IDbContextOptions>()
            .FindExtension<CoreOptionsExtension>()?
            .ApplicationServiceProvider;

        var encryptor = appServices?.GetService<IFieldEncryptor>();
        if (encryptor != null)
        {
            return encryptor;
        }

        var property = builder.Metadata;
        throw new InvalidOperationException(
            $"Property '{property.DeclaringType.Name}.{property.Name}' is configured with IsEncrypted(), "
            + "but no IFieldEncryptor is available. Enable it by setting EFCore:FieldEncryption:Enabled to true "
            + "and configuring a key ring, or register a custom IFieldEncryptor implementation. "
            + "Field encryption is never silently downgraded to plaintext storage.");
    }
}
