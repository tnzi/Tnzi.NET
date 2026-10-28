using System.Linq.Expressions;
using Tnzi.Identity.Entities;
using Tnzi.Security.Authorization;

namespace Tnzi.Chat.Tests.Services;

/// <summary>
/// 单聊对象是调用方直接给的 id，所以新建单聊前要确认它是一个能进业务会话的真实账号。
/// </summary>
/// <remarks>
/// 群成员写入早已静默剔除超管与不存在的租户外账号，单聊却只拒绝空 id 与自己：业务用户直接
/// <c>POST conversations/direct {userId: 超管 id}</c> 就能与运维超管建立单聊并投递消息（超管持全部权限码），
/// 传一个随机 GUID 则建出一个对方成员根本不存在的会话。两种情形答同一个 404，不泄露这个 id 是不是超管。
/// </remarks>
public class DirectChatTargetTests : Integration.IntegrationTestBase
{
    private static readonly Guid Colleague = Guid.NewGuid();
    private static readonly Guid SuperAdmin = Guid.NewGuid();
    private static readonly Guid Ghost = Guid.NewGuid();

    private static readonly List<User> Users =
    [
        new() { Id = CurrentUserId, UserName = "me" },
        new() { Id = Colleague, UserName = "colleague" },
        new() { Id = SuperAdmin, UserName = "root" },
    ];

    private IConversationService Conversations => ServiceProvider.GetRequiredService<IConversationService>();

    protected override void ConfigureServices(IServiceCollection services)
    {
        base.ConfigureServices(services);

        var userRepo = new Mock<IRepository<User, Guid>>();
        userRepo.Setup(r => r.ExistsAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Guid id, CancellationToken _) => Users.Any(u => u.Id == id));
        userRepo.Setup(r => r.ToListAsync(It.IsAny<Expression<Func<User, bool>>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Expression<Func<User, bool>> p, CancellationToken _) => Users.Where(p.Compile()).ToList());
        services.AddScoped(_ => userRepo.Object);

        var funcAuth = new Mock<IFunctionAuthorizationService>();
        funcAuth.Setup(f => f.GetSuperAdminUserIdsAsync())
            .ReturnsAsync((IReadOnlySet<Guid>)new HashSet<Guid> { SuperAdmin });
        services.AddScoped(_ => funcAuth.Object);
    }

    [Fact]
    public async Task DirectChat_WithANonexistentUser_IsNotFound_AndCreatesNothing()
    {
        var result = await Conversations.GetOrCreateDirectAsync(Ghost);

        result.Succeeded.ShouldBeFalse();
        result.Code.ShouldBe(404);
        (await DbContext.Set<Conversation>().AnyAsync()).ShouldBeFalse();
        (await DbContext.Set<ConversationMember>().AnyAsync()).ShouldBeFalse();
    }

    [Fact]
    public async Task DirectChat_WithASuperAdmin_IsNotFound_LikeAnUnknownId()
    {
        var result = await Conversations.GetOrCreateDirectAsync(SuperAdmin);

        result.Succeeded.ShouldBeFalse();
        result.Code.ShouldBe(404);
        result.Message.ShouldBe((await Conversations.GetOrCreateDirectAsync(Ghost)).Message);
        (await DbContext.Set<ConversationMember>().AnyAsync(m => m.UserId == SuperAdmin)).ShouldBeFalse();
    }

    [Fact]
    public async Task DirectChat_WithAColleague_IsCreated()
    {
        var result = await Conversations.GetOrCreateDirectAsync(Colleague);

        result.Succeeded.ShouldBeTrue(result.Message);
        (await DbContext.Set<ConversationMember>().CountAsync(m => m.ConversationId == result.Data!.Id)).ShouldBe(2);
    }
}
