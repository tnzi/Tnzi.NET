using Tnzi.Security.Authorization;

namespace Tnzi.AI.Tests.Rag;

/// <summary>
/// 用户直连 RAG 端点的知识库级授权。
/// </summary>
/// <remarks>
/// <c>DefaultRagController</c> 是自动激活的默认控制器，类级只有一个裸 <c>[ApiAuthorize]</c>。
/// 不带 <c>KnowledgeBaseIds</c> 的请求走 search-all，于是任何一个已登录用户一次
/// <c>POST /api/rag/query</c> 就能命中全部知识库的原文分块（<c>Citations</c> 直接回原文）；
/// 带 id 的请求也只按 id 查，从不判定归属。<c>AgentKnowledgeGrant</c> 只约束 agent 用哪些库，
/// 管不到用户直连这条路。
/// </remarks>
public class RagAccessAuthorizationTests
{
    private static readonly Guid OpenKb = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid PrivateKb = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly Guid DisabledKb = Guid.Parse("33333333-3333-3333-3333-333333333333");

    private readonly Mock<IRepository<KnowledgeBase, Guid>> _kbRepository = new();
    private readonly Mock<IPermissionChecker> _permissionChecker = new();
    private readonly IServiceProvider _serviceProvider;

    public RagAccessAuthorizationTests()
    {
        var all = new List<KnowledgeBase>
        {
            new() { Id = OpenKb, Name = "open", IsEnabled = true, IsUserQueryable = true },
            new() { Id = PrivateKb, Name = "hr", IsEnabled = true, IsUserQueryable = false },
            new() { Id = DisabledKb, Name = "archived", IsEnabled = false, IsUserQueryable = true }
        };

        // 真实地按谓词过滤，而不是无论问什么都回同一批 —— 否则"启用中且可直查"这半个条件
        // 从来没有被跑到过。
        _kbRepository
            .Setup(r => r.ToListAsync(It.IsAny<Expression<Func<KnowledgeBase, bool>>?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Expression<Func<KnowledgeBase, bool>>? predicate, CancellationToken _) =>
                predicate == null ? all : all.Where(predicate.Compile()).ToList());

        // 默认拒绝一切权限码 = 普通已登录用户。
        _permissionChecker.Setup(p => p.IsGrantedAsync(It.IsAny<string>())).ReturnsAsync(false);

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddScoped(_ => _permissionChecker.Object);
        _serviceProvider = services.BuildServiceProvider();
    }

    private void GrantKnowledgeView()
        => _permissionChecker.Setup(p => p.IsGrantedAsync("ai.knowledge.view")).ReturnsAsync(true);

    private DefaultRagAccessAuthorizer CreateAuthorizer()
        => new(_serviceProvider, _kbRepository.Object);

    [Fact]
    public async Task PlainUser_NoKbSpecified_GetsOnlyTheUserQueryableOnes_NotSearchAll()
    {
        var result = await CreateAuthorizer().AuthorizeQueryAsync(null);

        result.Succeeded.ShouldBeTrue();
        // 关键：返回的是一个显式的 id 列表，不是"空 = 查全部"。
        result.Data!.ShouldBe([OpenKb]);
    }

    [Fact]
    public async Task PlainUser_RequestsAPrivateKb_IsRejected()
    {
        var result = await CreateAuthorizer().AuthorizeQueryAsync([PrivateKb]);

        result.Succeeded.ShouldBeFalse();
        result.Code.ShouldBe(403);
    }

    [Fact]
    public async Task PlainUser_MixesAnAllowedAndAPrivateKb_IsRejectedWholesale()
    {
        // 整条拒绝而不是悄悄剔除：剔除会让"你无权查这个库"和"这个库里没有相关内容"
        // 给出同一个回答。
        var result = await CreateAuthorizer().AuthorizeQueryAsync([OpenKb, PrivateKb]);

        result.Succeeded.ShouldBeFalse();
        result.Code.ShouldBe(403);
    }

    [Fact]
    public async Task PlainUser_RequestsAnUnknownKb_GetsTheSameAnswerAsAPrivateOne()
    {
        // 不存在与无权必须同一个答案，否则按 id 探测就能确认知识库存在。
        var unknown = await CreateAuthorizer().AuthorizeQueryAsync([Guid.NewGuid()]);
        var forbidden = await CreateAuthorizer().AuthorizeQueryAsync([PrivateKb]);

        unknown.Succeeded.ShouldBeFalse();
        unknown.Code.ShouldBe(forbidden.Code);
        unknown.Message.ShouldBe(forbidden.Message);
    }

    [Fact]
    public async Task PlainUser_DisabledKb_IsNotQueryableEvenWhenMarkedUserQueryable()
    {
        var result = await CreateAuthorizer().AuthorizeQueryAsync([DisabledKb]);

        result.Succeeded.ShouldBeFalse();
    }

    [Fact]
    public async Task PlainUser_RequestsAnAllowedKb_IsAuthorized()
    {
        var result = await CreateAuthorizer().AuthorizeQueryAsync([OpenKb]);

        result.Succeeded.ShouldBeTrue();
        result.Data!.ShouldBe([OpenKb]);
    }

    [Fact]
    public async Task PlainUser_NothingIsUserQueryable_IsRejected_NotSilentlyEmpty()
    {
        // 一个都没开时必须 403 并说清楚缺什么，而不是返回空列表 —— 空列表会让
        // "没人给你开权限"看起来像"知识库里没有相关内容"。
        _kbRepository
            .Setup(r => r.ToListAsync(It.IsAny<Expression<Func<KnowledgeBase, bool>>?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);

        var result = await CreateAuthorizer().AuthorizeQueryAsync(null);

        result.Succeeded.ShouldBeFalse();
        result.Code.ShouldBe(403);
        result.Message!.ShouldContain("user-queryable");
    }

    [Fact]
    public async Task OperatorWithKnowledgeView_KeepsSearchAll()
    {
        GrantKnowledgeView();

        var result = await CreateAuthorizer().AuthorizeQueryAsync(null);

        result.Succeeded.ShouldBeTrue();
        // 空 = 不限定，管理调用者的 search-all 保持可用。
        result.Data!.ShouldBeEmpty();
    }

    [Fact]
    public async Task OperatorWithKnowledgeView_ReachesAPrivateKb()
    {
        GrantKnowledgeView();

        var result = await CreateAuthorizer().AuthorizeQueryAsync([PrivateKb]);

        result.Succeeded.ShouldBeTrue();
        result.Data!.ShouldBe([PrivateKb]);
    }

    [Fact]
    public async Task NoPermissionChecker_FailsClosed_TreatsCallerAsAPlainUser()
    {
        // 没装授权能力的宿主拿到的应当是"按 IsUserQueryable 放行"，而不是"人人都是管理员"。
        var services = new ServiceCollection();
        services.AddLogging();
        var bare = services.BuildServiceProvider();

        var result = await new DefaultRagAccessAuthorizer(bare, _kbRepository.Object)
            .AuthorizeQueryAsync([PrivateKb]);

        result.Succeeded.ShouldBeFalse();
        result.Code.ShouldBe(403);
    }
}
