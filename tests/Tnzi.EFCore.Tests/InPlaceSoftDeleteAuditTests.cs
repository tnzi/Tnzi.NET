namespace Tnzi.EFCore.Tests;

/// <summary>
/// 直接把 <see cref="ISoftDelete.IsDeleted"/> 置 true 的软删除（不经仓储 <c>DeleteAsync</c>）
/// 同样要留下删除人与删除时间。
/// </summary>
/// <remarks>
/// <para>
/// 仓储删除走的是 EF 的 <c>Deleted</c> 状态，审计管线在那条分支里把它转成软删并写
/// <c>DeleterId</c> / <c>DeletionTime</c>。但代码也可以<b>直接赋值</b> ——
/// <c>ChildCollectionSync.ReplaceChildren</c> 就是这么做的（它是纯内存操作，拿不到当前用户，
/// 本来也不该拿）。那条路径此前只走 <c>Modified</c> 分支，于是产出一批
/// 「没有人、在没有时间删掉的」软删行：<c>IsDeleted</c> 为真，而事后追责唯一想看的那两列是 null。
/// </para>
/// <para>
/// 这个缺陷是某消费应用在被建议改用 <c>ReplaceChildren</c> 时发现并拒绝执行的 ——
/// 它宁可保留自己那份手写删除，也不肯写出没有删除人的软删行。
/// </para>
/// </remarks>
public class InPlaceSoftDeleteAuditTests : EFCoreTestBase
{
    private async Task<TestUser> SeedAsync()
    {
        var user = new TestUser { UserName = "alice", Email = "alice@example.com" };
        DbContext.Set<TestUser>().Add(user);
        await DbContext.SaveChangesAsync();
        return user;
    }

    [Fact]
    public async Task Setting_IsDeleted_directly_should_record_who_and_when()
    {
        var user = await SeedAsync();
        Assert.False(user.IsDeleted);

        // 不经仓储：就地把标志翻过去，实体停在 Modified。
        user.IsDeleted = true;
        await DbContext.SaveChangesAsync();

        Assert.Equal(CurrentUser.Id, user.DeleterId);
        Assert.NotNull(user.DeletionTime);
    }

    [Fact]
    public async Task Editing_an_already_deleted_row_should_not_overwrite_the_original_deletion()
    {
        var user = await SeedAsync();

        user.IsDeleted = true;
        await DbContext.SaveChangesAsync();
        var firstDeleter = user.DeleterId;
        var firstDeletionTime = user.DeletionTime;

        // 稍后再改这条已软删的行的别的字段。
        await Task.Delay(10);
        user.Email = "alice+archived@example.com";
        await DbContext.SaveChangesAsync();

        // ★ 判据是「跃迁」而不是「现在为 true」：后者会在每次修改一条已软删的行时
        //   反复覆盖删除人，把最初那次删除的痕迹抹掉。
        Assert.Equal(firstDeleter, user.DeleterId);
        Assert.Equal(firstDeletionTime, user.DeletionTime);
    }

    [Fact]
    public async Task An_ordinary_edit_should_not_look_like_a_deletion()
    {
        var user = await SeedAsync();

        user.Email = "alice.b@example.com";
        await DbContext.SaveChangesAsync();

        Assert.False(user.IsDeleted);
        Assert.Null(user.DeleterId);
        Assert.Null(user.DeletionTime);
    }

    [Fact]
    public async Task Reviving_then_deleting_again_records_the_second_deletion()
    {
        // ChildCollectionSync 的「复活」路径：软删行再次出现在目标集合里就被翻回 false。
        var user = await SeedAsync();

        user.IsDeleted = true;
        await DbContext.SaveChangesAsync();
        var firstDeletionTime = user.DeletionTime;

        user.IsDeleted = false;
        await DbContext.SaveChangesAsync();

        await Task.Delay(10);
        user.IsDeleted = true;
        await DbContext.SaveChangesAsync();

        // 复活之后的这次删除是一次新的删除，该记新的时间。
        Assert.NotEqual(firstDeletionTime, user.DeletionTime);
        Assert.Equal(CurrentUser.Id, user.DeleterId);
    }
}
