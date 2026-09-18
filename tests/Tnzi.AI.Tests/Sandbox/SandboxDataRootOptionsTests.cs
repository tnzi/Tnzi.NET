using static Tnzi.AI.Tests.Sandbox.SandboxTestSupport;

namespace Tnzi.AI.Tests.Sandbox;

/// <summary>
/// <c>AI:Sandbox:DataRoot</c>：默认值落在应用目录之外；配到内容根之下只警告不拒绝；解析不出默认值时拒绝启动并指名要配。
/// </summary>
/// <remarks>
/// 此前默认是内容根下的相对路径 <c>.tnzi-ai/threads</c>：把发布目录整体同步到生产主机的部署把每个线程的工作区
/// 一并同步了出去，某消费方一天里 92,000 个文件把一个 62 字节的 app_offline 标记拖到 24 分钟才送达。
/// </remarks>
public class SandboxDataRootOptionsTests
{
    [Fact]
    public void DefaultDataRoot_IsAbsolute_AndOutsideTheContentRoot()
    {
        var options = new SandboxModuleOptions();
        var contentRoot = AppContext.BaseDirectory;

        options.DataRoot.ShouldBe(SandboxModuleOptions.DefaultDataRoot);
        Path.IsPathRooted(options.DataRoot).ShouldBeTrue("the default must not depend on the process working directory");
        options.DataRoot.ShouldNotContain(".tnzi-ai");
        SandboxModuleOptionsValidator.IsUnderContentRoot(options.DataRoot, contentRoot, out _, out _)
            .ShouldBeFalse($"'{options.DataRoot}' must not resolve under the content root '{contentRoot}'");
        options.DataRoot.ShouldEndWith(Path.Combine("Tnzi", "ai-threads"));
    }

    [Fact]
    public void Validator_ExplicitDataRoot_IsKeptVerbatim()
    {
        var explicitRoot = Path.Combine(Path.GetTempPath(), "explicit-threads");
        var options = new SandboxModuleOptions { DataRoot = explicitRoot };

        new SandboxModuleOptionsValidator().Validate(null, options).Succeeded.ShouldBeTrue();
        options.DataRoot.ShouldBe(explicitRoot);
    }

    [Fact]
    public void Validator_DataRootUnderContentRoot_WarnsButDoesNotFail()
    {
        var contentRoot = AppContext.BaseDirectory;
        var logs = new CapturingLoggerFactory();
        var validator = new SandboxModuleOptionsValidator(new TestHostEnvironment { ContentRootPath = contentRoot }, logs);
        var options = new SandboxModuleOptions { DataRoot = Path.Combine(contentRoot, ".tnzi-ai", "threads") };

        var result = validator.Validate(null, options);

        result.Succeeded.ShouldBeTrue("an explicit choice is the consumer's; it is warned about, not refused");
        var (level, message) = logs.Entries.ShouldHaveSingleItem();
        level.ShouldBe(LogLevel.Warning);
        message.ShouldContain("inside the application content root");
        message.ShouldContain("AI:Sandbox:DataRoot");
    }

    [Fact]
    public void Validator_RelativeDataRoot_IsResolvedAgainstTheWorkingDirectory_BeforeTheCheck()
    {
        // 与 VirtualPathTranslator 同一口径：相对路径按进程当前目录解析
        var cwd = Directory.GetCurrentDirectory();
        var logs = new CapturingLoggerFactory();
        var validator = new SandboxModuleOptionsValidator(new TestHostEnvironment { ContentRootPath = cwd }, logs);

        validator.Validate(null, new SandboxModuleOptions { DataRoot = ".tnzi-ai/threads" }).Succeeded.ShouldBeTrue();

        logs.Entries.ShouldHaveSingleItem().Message.ShouldContain(Path.Combine(cwd, ".tnzi-ai", "threads"));
    }

    [Fact]
    public void Validator_DataRootOutsideContentRoot_DoesNotWarn()
    {
        var logs = new CapturingLoggerFactory();
        var validator = new SandboxModuleOptionsValidator(new TestHostEnvironment { ContentRootPath = AppContext.BaseDirectory }, logs);

        validator.Validate(null, new SandboxModuleOptions { DataRoot = Path.Combine(Path.GetTempPath(), "elsewhere") }).Succeeded.ShouldBeTrue();

        logs.Entries.ShouldBeEmpty();
    }

    [Fact]
    public void Validator_WithoutHostEnvironment_SkipsTheContentRootCheck()
    {
        var logs = new CapturingLoggerFactory();
        var validator = new SandboxModuleOptionsValidator(hostEnvironment: null, logs);

        validator.Validate(null, new SandboxModuleOptions { DataRoot = ".tnzi-ai/threads" }).Succeeded.ShouldBeTrue();

        logs.Entries.ShouldBeEmpty();
    }

    [Fact]
    public void Validator_EmptyDataRoot_FailsAndNamesTheSetting()
    {
        var result = new SandboxModuleOptionsValidator().Validate(null, new SandboxModuleOptions { DataRoot = string.Empty });

        result.Failed.ShouldBeTrue();
        result.FailureMessage.ShouldContain("AI:Sandbox:DataRoot");
        result.FailureMessage.ShouldContain("LocalApplicationData");
    }

    [Theory]
    [InlineData("app", true)]
    [InlineData("app/threads", true)]
    [InlineData("app/threads/deeper", true)]
    [InlineData("sibling", false)]
    [InlineData("app-suffix", false)]
    public void IsUnderContentRoot_ComparesWholePathSegments(string candidateRelativeToRoot, bool expected)
    {
        var root = Path.Combine(Path.GetTempPath(), "content-root-probe");
        var contentRoot = Path.Combine(root, "app");
        var candidate = Path.Combine(root, candidateRelativeToRoot.Replace('/', Path.DirectorySeparatorChar));

        SandboxModuleOptionsValidator.IsUnderContentRoot(candidate, contentRoot, out _, out _).ShouldBe(expected);
    }

    private sealed class CapturingLoggerFactory : ILoggerFactory
    {
        public List<(LogLevel Level, string Message)> Entries { get; } = [];

        public ILogger CreateLogger(string categoryName) => new CapturingLogger(Entries);

        public void AddProvider(ILoggerProvider provider)
        {
        }

        public void Dispose()
        {
        }

        private sealed class CapturingLogger(List<(LogLevel, string)> entries) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
            public bool IsEnabled(LogLevel logLevel) => true;

            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
                => entries.Add((logLevel, formatter(state, exception)));
        }
    }
}
