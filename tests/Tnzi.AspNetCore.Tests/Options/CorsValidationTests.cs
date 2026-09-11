namespace Tnzi.AspNetCore.Tests.Options;

/// <summary>
/// CORS 策略的启动期校验。
///
/// 守的是一条部署很久之后才显形的线：策略是**惰性构建**的，
/// 非法组合不会让应用启动失败 —— 启动正常、健康检查正常，
/// 直到第一个跨域请求打进来才在 CorsPolicyBuilder 里抛异常，
/// 于是**每一个跨域请求都是 500**，现场看起来像「后端挂了」不像「配置写错了」。
/// </summary>
public class CorsValidationTests
{
    private static ValidateOptionsResult Validate(CorsOptions cors)
        => new AspNetCoreOptionsValidator().Validate(name: null, new AspNetCoreOptions { Cors = cors });

    private static CorsOptions Enabled() => new()
    {
        Enabled = true,
        PolicyName = "DefaultPolicy",
        WithOrigins = ["https://example.com"]
    };

    [Fact]
    public void AWildcardOriginWithCredentials_IsRejected()
    {
        var cors = Enabled();
        cors.AllowAnyOrigin = true;
        cors.AllowCredentials = true;

        var result = Validate(cors);

        Assert.True(result.Failed);
        Assert.Contains("AllowAnyOrigin", result.FailureMessage);
    }

    [Fact]
    public void TheCombinationReallyDoesBlowUpAtRequestTime()
    {
        // 防锈：证明被拒的是一个真会炸的组合，而不是我们自己发明的一条规矩。
        // 少了这条，「校验太严」与「校验挡下了真问题」在测试上分不出来。
        var builder = new Microsoft.AspNetCore.Cors.Infrastructure.CorsPolicyBuilder();
        builder.AllowAnyOrigin();
        builder.AllowCredentials();

        Assert.Throws<InvalidOperationException>(() => builder.Build());
    }

    [Fact]
    public void ContradictoryCredentialFlags_AreRejected()
    {
        var cors = Enabled();
        cors.AllowCredentials = true;
        cors.DisallowCredentials = true;

        var result = Validate(cors);

        Assert.True(result.Failed);
        Assert.Contains("DisallowCredentials", result.FailureMessage);
    }

    [Fact]
    public void AnEmptyPolicyName_IsRejected()
    {
        var cors = Enabled();
        cors.PolicyName = "";

        Assert.True(Validate(cors).Failed);
    }

    [Fact]
    public void AWildcardOriginWithoutCredentials_IsFine()
    {
        var cors = Enabled();
        cors.AllowAnyOrigin = true;

        Assert.True(Validate(cors).Succeeded);
    }

    [Fact]
    public void ANamedOriginWithCredentials_IsFine()
    {
        // 带凭据的正确写法：逐条列出来源。
        var cors = Enabled();
        cors.AllowCredentials = true;

        Assert.True(Validate(cors).Succeeded);
    }

    [Fact]
    public void ADisabledPolicy_IsNotChecked()
    {
        // 关掉的策略里写什么都不该挡住启动 —— 它一行都不会被构建。
        var cors = new CorsOptions
        {
            Enabled = false,
            AllowAnyOrigin = true,
            AllowCredentials = true,
            PolicyName = ""
        };

        Assert.True(Validate(cors).Succeeded);
    }

    [Fact]
    public void NoOriginAtAll_IsAWarningNotAFailure()
    {
        // 「什么都不允许」是一条合法策略，也可能是 WithOrigins 忘了填 ——
        // 那两件事只有部署方分得清，所以告警而不是失败。
        var cors = new CorsOptions { Enabled = true, PolicyName = "DefaultPolicy" };

        Assert.True(Validate(cors).Succeeded);

        var warnings = new List<string>();
        var validator = new WarningProbe();
        validator.Collect(new AspNetCoreOptions { Cors = cors }, warnings);

        Assert.Contains(warnings, w => w.Contains("no origin is allowed", StringComparison.Ordinal));
    }

    /// <summary>把 protected 的告警收集口暴露出来。</summary>
    private sealed class WarningProbe : AspNetCoreOptionsValidator
    {
        public void Collect(AspNetCoreOptions options, List<string> warnings) => CollectWarnings(options, warnings);
    }
}
