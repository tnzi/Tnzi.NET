using System.Net;
using Microsoft.AspNetCore.DataProtection;

namespace Tnzi.AI.Tests.Services;

/// <summary>
/// 租户行的 Provider 端点由租户管理员填写，服务器会替它发请求（每次对话、每次列模型）：
/// 指向环回 / 内网 / 链路本地 / 云元数据地址的端点必须在写入时被拒，列模型前也要再查一次。
/// System 行由宿主管理员维护（本地 Ollama / vLLM 是常见用法），不检查。
/// </summary>
public class ProviderServiceTenantEndpointTests
{
    private readonly Mock<IRepository<Provider, Guid>> _repository = new();
    private readonly IDataProtectionProvider _dataProtection = new EphemeralDataProtectionProvider();
    private readonly Mock<IHttpClientFactory> _httpClientFactory = new(MockBehavior.Strict);
    private readonly IServiceProvider _serviceProvider;
    private readonly Guid _tenantId = Guid.NewGuid();

    public ProviderServiceTenantEndpointTests()
    {
        MapperExtensions.SetMapper(new Mapper(new TypeAdapterConfig()));
        _serviceProvider = new ServiceCollection().AddLogging().BuildServiceProvider();
        _repository.Setup(r => r.InsertAsync(It.IsAny<Provider>(), It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        _repository.Setup(r => r.UpdateAsync(It.IsAny<Provider>(), It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
    }

    [Theory]
    [InlineData("http://127.0.0.1:8080/v1")]
    [InlineData("http://169.254.169.254/latest/meta-data")]
    [InlineData("http://10.0.0.5/v1")]
    [InlineData("file:///etc/passwd")]
    public async Task CreateAsync_TenantRowWithInternalEndpoint_IsRejected(string endpoint)
    {
        SetupQueryable([]);

        var result = await CreateService(_tenantId).CreateAsync(new CreateProviderDto
        {
            Name = "OpenAI",
            ProviderType = "OpenAI",
            Endpoint = endpoint
        });

        result.Succeeded.ShouldBeFalse();
        result.Code.ShouldBe(400);
        result.Message.ShouldNotBeNull().ShouldStartWith("Provider endpoint is not allowed");
        _repository.Verify(r => r.InsertAsync(It.IsAny<Provider>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task CreateAsync_SystemRowWithLocalEndpoint_IsAllowed()
    {
        SetupQueryable([]);

        var result = await CreateService(tenantId: null).CreateAsync(new CreateProviderDto
        {
            Name = "Ollama",
            ProviderType = "OpenAI",
            Endpoint = "http://127.0.0.1:11434/v1"
        });

        result.Succeeded.ShouldBeTrue();
    }

    [Fact]
    public async Task UpdateAsync_TenantRowRepointedAtAnInternalAddress_IsRejectedAndLeftUnchanged()
    {
        var row = TenantRow("https://api.tenant.example/v1");
        SetupQueryable([row]);

        var result = await CreateService(_tenantId).UpdateAsync(row.Id, new UpdateProviderDto
        {
            Name = "Renamed",
            Endpoint = "http://192.168.1.10/v1"
        });

        result.Succeeded.ShouldBeFalse();
        result.Code.ShouldBe(400);
        row.Endpoint.ShouldBe("https://api.tenant.example/v1");
        row.Name.ShouldBe("OpenAI");
        _repository.Verify(r => r.UpdateAsync(It.IsAny<Provider>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ListModelsAsync_TenantRowWithInternalEndpoint_DoesNotIssueTheRequest()
    {
        // 存量行（守卫之前登记的）或登记后把 DNS 改指内网：列模型不能替它发这个请求
        var row = TenantRow("http://127.0.0.1:9/v1");
        SetupQueryable([row]);

        var result = await CreateService(_tenantId).ListModelsAsync(row.Id);

        result.Succeeded.ShouldBeTrue();
        result.Data!.Source.ShouldBe("fallback");
        _httpClientFactory.Verify(f => f.CreateClient(It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task ListModelsAsync_TenantRowWithAPublicEndpoint_ConnectsThroughTheEgressGuardedClient()
    {
        // 写入时与列模型前的检查都只是那一刻的 DNS 结果；真正连接（含端点回 3xx）时同样要过检查，
        // 所以租户自填的端点走带连接期守卫、不跟随重定向的客户端，而不是共享的 Fallback 客户端
        var row = TenantRow("http://203.0.113.10/v1");
        SetupQueryable([row]);
        _httpClientFactory.Setup(f => f.CreateClient(ResilientHttpClientNames.TenantEndpoint))
            .Returns(() => new HttpClient(new ModelsHandler()));

        var result = await CreateService(_tenantId).ListModelsAsync(row.Id);

        result.Succeeded.ShouldBeTrue(result.Message);
        result.Data!.Source.ShouldBe("live");
        result.Data.Models.ShouldContain("tenant-model");
        _httpClientFactory.Verify(f => f.CreateClient(ResilientHttpClientNames.TenantEndpoint), Times.Once);
        _httpClientFactory.Verify(f => f.CreateClient(ResilientHttpClientNames.Fallback), Times.Never);
    }

    [Fact]
    public async Task ListModelsAsync_SystemRow_KeepsTheDeploymentClient()
    {
        var row = TenantRow("http://127.0.0.1:11434/v1");
        row.Scope = ResourceScope.System;
        row.TenantId = null;
        SetupQueryable([row]);
        _httpClientFactory.Setup(f => f.CreateClient(ResilientHttpClientNames.Fallback))
            .Returns(() => new HttpClient(new ModelsHandler()));

        var result = await CreateService(tenantId: null).ListModelsAsync(row.Id);

        result.Data!.Source.ShouldBe("live");
        _httpClientFactory.Verify(f => f.CreateClient(ResilientHttpClientNames.TenantEndpoint), Times.Never);
    }

    private sealed class ModelsHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("""{"data":[{"id":"tenant-model"}]}""")
            });
    }

    private ProviderService CreateService(Guid? tenantId)
        => new(_repository.Object, _dataProtection, _httpClientFactory.Object, _serviceProvider,
            tenantId.HasValue ? new StubCurrentTenant(tenantId.Value) : null);

    private Provider TenantRow(string endpoint) => new()
    {
        Id = Guid.NewGuid(),
        Name = "OpenAI",
        ProviderType = "OpenAI",
        Endpoint = endpoint,
        DefaultModel = "gpt-4o",
        IsEnabled = true,
        Scope = ResourceScope.Tenant,
        TenantId = _tenantId,
        CreationTime = DateTime.UtcNow
    };

    private void SetupQueryable(List<Provider> data)
    {
        var mock = data.BuildMock();
        _repository.Setup(r => r.AsQueryable(It.IsAny<bool>())).Returns(mock);
    }

    private sealed class StubCurrentTenant(Guid id) : ICurrentTenant
    {
        public Guid? Id { get; } = id;
        public string? Name => null;
        public bool IsAvailable => true;
        public IDisposable Change(Guid? tenantId, string? tenantName = null) => new NoOp();

        private sealed class NoOp : IDisposable
        {
            public void Dispose()
            {
            }
        }
    }
}
