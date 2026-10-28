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
    /// UTF-8 下一个字符最多占的字节数。按码点计列长的库（PostgreSQL / MySQL）里一个字符可以是 4 字节的增补字符。
    /// </summary>
    private const int MaxUtf8BytesPerChar = 4;

    /// <summary>
    /// AES-GCM 的随机数与认证标签（12 + 16 字节），与明文一起进 Base64。
    /// </summary>
    private const int AesGcmOverheadBytes = 28;

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

        builder.HasConversion(new EncryptedStringValueConverter(encryptor, resolved));

        RelaxMaxLength(builder, encryptor.CiphertextOverheadChars);
        return builder;
    }

    /// <summary>
    /// 这个属性是否经 <see cref="IsEncrypted(PropertyBuilder{string}, string?)"/> 声明为加密列。
    /// </summary>
    /// <remarks>
    /// ★ 给「把模型值复制到别处」的代码用。EF 的 <c>CurrentValue</c> / <c>OriginalValue</c> 是<b>模型值</b>：
    /// 加密只发生在数据库提供程序的边界上，变更跟踪器里拿到的是明文。实体级审计就曾这样把加密列的明文
    /// 逐条写进审计表：加密删除销毁了密钥，那份副本还在。任何读变更跟踪器、再把值落到别的表 / 日志 / 消息里的地方，
    /// 都要先问这一句。
    /// <para>
    /// 判定看的是值转换器的类型，不是模型注解：注解会写进迁移快照，给每个已上线的消费方凭空多出一次快照变更。
    /// </para>
    /// </remarks>
    public static bool IsEncryptedColumn(this IReadOnlyProperty property)
    {
        Check.NotNull(property);
        return property.GetValueConverter() is EncryptedStringValueConverter;
    }

    /// <summary>
    /// 加密列的值转换器。单独成类只为让 <see cref="IsEncryptedColumn"/> 认得出它。
    /// </summary>
    private sealed class EncryptedStringValueConverter(IFieldEncryptor encryptor, string purpose)
        : ValueConverter<string, string>(
            plain => encryptor.Encrypt(plain, purpose),
            stored => encryptor.IsEncrypted(stored) ? encryptor.Decrypt(stored, purpose) : stored);

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
    /// <para>
    /// 只放宽、不收紧；未设长度上限的属性不做处理。
    /// 这是刻意的越权：忘记调整长度是必然会发生的疏忽，而它的表现是运行期写入失败。
    /// </para>
    /// <para>
    /// 按最坏情况算：原列长是字符数，加密的是它的 UTF-8 字节（每字符最多 4 字节），
    /// 密文 = <c>{keyId}:</c> + <c>v2:</c> + Base64(随机数 + 密文 + 标签)，密钥标识按校验器允许的最大长度计。
    /// ★ 此前按每字符 1 字节估算：<c>HasMaxLength(50)</c> 放宽到 152，而 30 个汉字的密文已超过 160 个字符，
    /// 中文、日文这类文本写到原长的三分之一左右就在数据库层报超长。
    /// </para>
    /// </remarks>
    private static void RelaxMaxLength(PropertyBuilder builder, int overheadChars)
    {
        var current = builder.Metadata.GetMaxLength();
        if (current is not > 0)
        {
            return;
        }

        var payloadBytes = (long)current.Value * MaxUtf8BytesPerChar + AesGcmOverheadBytes;
        var base64Chars = (payloadBytes + 2) / 3 * 4;
        var expanded = overheadChars + base64Chars;

        // 大到 int 装不下的上限等于不设上限
        builder.Metadata.SetMaxLength(expanded <= int.MaxValue ? (int)expanded : null);
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

        // ★★ 设计期（dotnet ef）且没有应用容器（框架的设计期工厂就是这样建上下文的）：迁移只需要模型，
        // 给一个调用即抛的占位，列长放宽与转换器形状与运行期一致。有应用容器却取不到加密器时，
        // 运行期同样会失败，照旧在这里拒绝。
        if (appServices == null && EF.IsDesignTime)
        {
            return DesignTimeFieldEncryptor.Instance;
        }

        var property = builder.Metadata;
        throw new InvalidOperationException(
            $"Property '{property.DeclaringType.Name}.{property.Name}' is configured with IsEncrypted(), "
            + "but no IFieldEncryptor is available. Enable it by setting EFCore:FieldEncryption:Enabled to true "
            + "and configuring a key ring, or register a custom IFieldEncryptor implementation. "
            + "Field encryption is never silently downgraded to plaintext storage.");
    }
}
