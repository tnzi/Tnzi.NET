using Microsoft.EntityFrameworkCore;
using Tnzi.EFCore;
using Tnzi.Notification.Entities.Configs;
using Tnzi.Notification.Push.Entities.Configs;
using Tnzi.Security;

namespace Tnzi.Notification.Push.Tests;

/// <summary>
/// <c>Notification_PushDevice</c> 的表结构不变量，直接读 EF 模型元数据断言。
/// </summary>
public class PushDeviceSchemaTests
{
    private static Microsoft.EntityFrameworkCore.Metadata.IMutableEntityType ModelOf(IEntityRegister configuration)
    {
        var modelBuilder = new ModelBuilder();
        configuration.RegisterTo(modelBuilder);
        return modelBuilder.Model.FindEntityType(configuration.EntityType)!;
    }

    /// <summary>
    /// ★ 令牌列必须不宽于 <c>Notification_Recipient.Address</c>。
    /// </summary>
    /// <remarks>
    /// 投递时令牌被抄进 <c>Recipient.Address</c>。本列若更宽，一个存得进注册表却塞不进
    /// 那一列的令牌会在落库时抛异常 —— 而那一次 <c>SaveChanges</c> 承载的是<b>整批</b>
    /// 收件人的状态，于是已经发出去的那些退回 <c>Pending</c>，续发时再发一遍。
    /// <para>
    /// 这条不变量原本只是 <c>PushDeviceConfiguration</c> 上的一段注释。注释拦不住有人
    /// 为了「支持更长的令牌」把 500 改成 1000 —— 那一改编译通过、单测全绿、
    /// 只有生产上遇到超长令牌时才会以重复投递的形式暴露。
    /// </para>
    /// </remarks>
    [Fact]
    public void Token_column_is_never_wider_than_the_recipient_address_it_gets_copied_into()
    {
        var tokenWidth = ModelOf(new PushDeviceConfiguration())
            .FindProperty(nameof(Entities.PushDevice.Token))!.GetMaxLength();
        var addressWidth = ModelOf(new RecipientConfiguration())
            .FindProperty(nameof(Notification.Entities.Recipient.Address))!.GetMaxLength();

        tokenWidth.ShouldNotBeNull();
        addressWidth.ShouldNotBeNull();
        tokenWidth.Value.ShouldBeLessThanOrEqualTo(addressWidth.Value);
    }

    /// <summary>
    /// ★ 唯一约束落在<b>令牌</b>上，不是 (用户, 令牌)。
    /// </summary>
    /// <remarks>
    /// 一台设备换人登录时，那一行必须<b>改挂</b>到新用户。按 (UserId, Token) 唯一的话，
    /// 同一个令牌会同时挂在两个用户名下 —— 两行都合法、都能投递成功，
    /// 于是<b>前一个用户继续收到本该发给新用户的推送</b>，而这件事没有任何症状：
    /// 日志干净、投递报告全是 Sent。
    /// </remarks>
    [Fact]
    public void The_unique_key_is_the_token_alone_so_a_device_changing_hands_moves_instead_of_forking()
    {
        var unique = ModelOf(new PushDeviceConfiguration()).GetIndexes().Where(i => i.IsUnique).ToList();

        // 按令牌列挑出那一条，而不是断言「唯一索引只有一条」：设备密钥哈希后来也带了一条
        // 唯一索引，而那与本条不变量无关 —— 计数式断言会把「多了一条无关索引」
        // 误报成「令牌的唯一性被改坏了」。
        var tokenIndex = unique.Single(i =>
            i.Properties.Any(p => p.Name == nameof(Entities.PushDevice.Token)));
        var columns = tokenIndex.Properties.Select(p => p.Name).ToList();
        columns.ShouldContain(nameof(Entities.PushDevice.Token));
        columns.ShouldNotContain(nameof(Entities.PushDevice.UserId));
    }

    /// <summary>
    /// ★ <see cref="Entities.PushDevice.UserId"/> 必须可空，否则匿名设备一行都写不进来。
    /// </summary>
    /// <remarks>
    /// 改回不可空是编译得过的（服务层给的是 <c>Guid?</c>，赋值处会报错，但把那几处一并
    /// 改成 <c>Guid.Empty</c> 就又编译得过了），而症状是匿名注册端点全部 500，
    /// 或者更糟：全表挂在一个全零的用户名下。
    /// </remarks>
    [Fact]
    public void The_user_id_is_nullable_so_a_device_without_an_account_can_own_a_row()
    {
        ModelOf(new PushDeviceConfiguration())
            .FindProperty(nameof(Entities.PushDevice.UserId))!
            .IsNullable.ShouldBeTrue();
    }

    /// <summary>
    /// ★★ 设备密钥哈希的唯一索引<b>必须</b>带「非空」过滤器。
    /// </summary>
    /// <remarks>
    /// 登录设备的这一列全是 NULL，而各家数据库对唯一索引里的 NULL 判定不同：
    /// PostgreSQL / SQLite 认为 NULL 互不相等（多少行都行），SQL Server 认为 NULL 彼此相等
    /// （只许一行）。没有这个过滤器时，<b>第二台纯登录设备在 SQL Server 上直接插不进去</b>，
    /// 而在开发者本机的 SQLite / PostgreSQL 上跑得好好的 —— 「在另一个库上是好的」
    /// 正是这类缺陷最擅长的伪装，所以它必须被断言，不能只写在注释里。
    /// </remarks>
    [Fact]
    public void The_device_key_unique_index_excludes_null_rows_or_sql_server_rejects_the_second_login_device()
    {
        var index = ModelOf(new PushDeviceConfiguration())
            .GetIndexes()
            .Single(i => i.Properties.Any(p => p.Name == nameof(Entities.PushDevice.DeviceKeyHash)));

        index.IsUnique.ShouldBeTrue();
        index.GetFilter().ShouldNotBeNullOrWhiteSpace();
        index.GetFilter()!.ShouldContain("IS NOT NULL");
    }

    /// <summary>
    /// 设备密钥哈希的列宽就是 SHA-256 十六进制摘要的长度，取自 <c>OneTimeToken.HashLength</c>。
    /// </summary>
    /// <remarks>
    /// 那个常量的文档专门写着「建表时列宽按它取，不要凭记忆写 128 或 256」。
    /// 写宽了不会报错，只是让人以为这一列存的可能不止是哈希。
    /// </remarks>
    [Fact]
    public void The_device_key_hash_column_is_exactly_one_sha256_hex_digest_wide()
    {
        ModelOf(new PushDeviceConfiguration())
            .FindProperty(nameof(Entities.PushDevice.DeviceKeyHash))!
            .GetMaxLength().ShouldBe(OneTimeToken.HashLength);
    }

    /// <summary>本表刻意不带软删除：退役一个死令牌必须真的删掉那一行。</summary>
    /// <remarks>
    /// 软删除会让注册表只增不减，而每一行都是一个「哪台设备装了这个 App」的可关联事实；
    /// 而且带过滤器的唯一索引会让墓碑行与新注册共存，退役后再注册同一令牌将不再是 upsert。
    /// </remarks>
    [Fact]
    public void The_table_has_no_soft_delete_column()
    {
        var properties = ModelOf(new PushDeviceConfiguration()).GetProperties().Select(p => p.Name).ToList();
        properties.ShouldNotContain("IsDeleted");
    }
}
