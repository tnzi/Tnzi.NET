using Tnzi.AI.Coder.Options;

namespace Tnzi.AI.Tests.Coder;

/// <summary>
/// PathValidator 单元测试 — 路径安全校验（路径遍历防护、拒绝模式、符号链接检测）
/// </summary>
public class PathValidatorTests : IDisposable
{
    private readonly string _projectRoot;

    public PathValidatorTests()
    {
        _projectRoot = Path.Combine(Path.GetTempPath(), $"tnzi-pathval-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_projectRoot);
    }

    public void Dispose()
    {
        if (Directory.Exists(_projectRoot))
        {
            Directory.Delete(_projectRoot, true);
        }
    }

    private PathValidator CreateValidator(Action<CoderOptions>? configure = null)
    {
        var options = new CoderOptions { ProjectRoot = _projectRoot };
        configure?.Invoke(options);
        return new PathValidator(
            Microsoft.Extensions.Options.Options.Create(options),
            Mock.Of<ILogger<PathValidator>>());
    }

    #region 基本路径验证

    [Fact]
    public async Task ValidateAsync_EmptyPath_ReturnsFailure()
    {
        var validator = CreateValidator();

        var result = await validator.ValidateAsync("");

        result.IsValid.ShouldBeFalse();
        var error = result.Error;
        error.ShouldNotBeNull();
        error.ShouldContain("empty");
    }

    [Fact]
    public async Task ValidateAsync_WhitespacePath_ReturnsFailure()
    {
        var validator = CreateValidator();

        var result = await validator.ValidateAsync("   ");

        result.IsValid.ShouldBeFalse();
        var error = result.Error;
        error.ShouldNotBeNull();
        error.ShouldContain("empty");
    }

    [Fact]
    public async Task ValidateAsync_RelativePathInProjectRoot_ReturnsSuccess()
    {
        // 在项目根目录内创建一个文件
        var testFile = Path.Combine(_projectRoot, "test.txt");
        File.WriteAllText(testFile, "content");
        var validator = CreateValidator();

        var result = await validator.ValidateAsync("test.txt");

        result.IsValid.ShouldBeTrue();
        result.ResolvedPath.ShouldNotBeNull();
    }

    [Fact]
    public async Task ValidateAsync_AbsolutePathInProjectRoot_ReturnsSuccess()
    {
        var testFile = Path.Combine(_projectRoot, "absolute.txt");
        File.WriteAllText(testFile, "content");
        var validator = CreateValidator();

        var result = await validator.ValidateAsync(testFile);

        result.IsValid.ShouldBeTrue();
    }

    [Fact]
    public async Task ValidateAsync_SubdirectoryPath_ReturnsSuccess()
    {
        var subDir = Path.Combine(_projectRoot, "src", "app");
        Directory.CreateDirectory(subDir);
        var validator = CreateValidator();

        // 非文件路径也应该可以验证（路径不存在但在允许范围内）
        var result = await validator.ValidateAsync("src/app/main.cs");

        result.IsValid.ShouldBeTrue();
    }

    #endregion

    #region 路径遍历防护

    [Fact]
    public async Task ValidateAsync_PathTraversal_DotDot_ReturnsFailure()
    {
        var validator = CreateValidator();

        var result = await validator.ValidateAsync("../../../etc/passwd");

        result.IsValid.ShouldBeFalse();
        var error = result.Error;
        error.ShouldNotBeNull();
        error.ShouldContain("outside");
    }

    [Fact]
    public async Task ValidateAsync_PathTraversal_AbsoluteOutside_ReturnsFailure()
    {
        var validator = CreateValidator();

        // 使用临时目录以外的绝对路径
        var outsidePath = Path.Combine(Path.GetTempPath(), "outside-project", "secret.txt");
        var result = await validator.ValidateAsync(outsidePath);

        result.IsValid.ShouldBeFalse();
        var error = result.Error;
        error.ShouldNotBeNull();
        error.ShouldContain("outside");
    }

    [Fact]
    public async Task ValidateAsync_PathTraversal_EncodedDotDot_ReturnsFailure()
    {
        var validator = CreateValidator();

        // 嵌套的 .. 路径（Path.GetFullPath 会规范化）
        var result = await validator.ValidateAsync("subdir/../../..");

        result.IsValid.ShouldBeFalse();
    }

    #endregion

    #region 拒绝模式匹配

    [Fact]
    public async Task ValidateAsync_EnvFile_MatchesDeniedPattern()
    {
        var validator = CreateValidator();

        var result = await validator.ValidateAsync(".env");

        result.IsValid.ShouldBeFalse();
        var error = result.Error;
        error.ShouldNotBeNull();
        error.ShouldContain("denied pattern");
    }

    [Fact]
    public async Task ValidateAsync_EnvLocalFile_MatchesDeniedPattern()
    {
        var validator = CreateValidator();

        var result = await validator.ValidateAsync(".env.local");

        result.IsValid.ShouldBeFalse();
        var error = result.Error;
        error.ShouldNotBeNull();
        error.ShouldContain("denied pattern");
    }

    [Fact]
    public async Task ValidateAsync_EnvProductionFile_MatchesDeniedPattern()
    {
        var validator = CreateValidator();

        var result = await validator.ValidateAsync(".env.production");

        result.IsValid.ShouldBeFalse();
        var error = result.Error;
        error.ShouldNotBeNull();
        error.ShouldContain("denied pattern");
    }

    [Fact]
    public async Task ValidateAsync_KeyFile_MatchesDeniedPattern()
    {
        var validator = CreateValidator();

        var result = await validator.ValidateAsync("server.key");

        result.IsValid.ShouldBeFalse();
        var error = result.Error;
        error.ShouldNotBeNull();
        error.ShouldContain("denied pattern");
    }

    [Fact]
    public async Task ValidateAsync_PemFile_MatchesDeniedPattern()
    {
        var validator = CreateValidator();

        var result = await validator.ValidateAsync("cert.pem");

        result.IsValid.ShouldBeFalse();
        var error = result.Error;
        error.ShouldNotBeNull();
        error.ShouldContain("denied pattern");
    }

    [Fact]
    public async Task ValidateAsync_CredentialsFile_MatchesDeniedPattern()
    {
        var validator = CreateValidator();

        var result = await validator.ValidateAsync("credentials.json");

        result.IsValid.ShouldBeFalse();
        var error = result.Error;
        error.ShouldNotBeNull();
        error.ShouldContain("denied pattern");
    }

    [Fact]
    public async Task ValidateAsync_NormalFile_NotDenied()
    {
        var validator = CreateValidator();

        var result = await validator.ValidateAsync("app.config");

        result.IsValid.ShouldBeTrue();
    }

    [Fact]
    public async Task ValidateAsync_CustomDeniedPattern_ExtensionMatch()
    {
        var validator = CreateValidator(opts =>
        {
            opts.Sandbox.DeniedPatterns = ["*.secret"];
        });

        var result = await validator.ValidateAsync("database.secret");

        result.IsValid.ShouldBeFalse();
        var error = result.Error;
        error.ShouldNotBeNull();
        error.ShouldContain("denied pattern");
    }

    [Fact]
    public async Task ValidateAsync_EmptyDeniedPatterns_AllowsAll()
    {
        var validator = CreateValidator(opts =>
        {
            opts.Sandbox.DeniedPatterns = [];
        });

        var result = await validator.ValidateAsync(".env");

        result.IsValid.ShouldBeTrue();
    }

    #endregion

    #region AllowedDirectories 配置

    [Fact]
    public async Task ValidateAsync_AdditionalAllowedDirectory_Permits()
    {
        var extraDir = Path.Combine(Path.GetTempPath(), $"tnzi-extra-{Guid.NewGuid():N}");
        Directory.CreateDirectory(extraDir);
        try
        {
            var validator = CreateValidator(opts =>
            {
                opts.Sandbox.AllowedDirectories = [".", extraDir];
                opts.Sandbox.DeniedPatterns = [];
            });

            var testFile = Path.Combine(extraDir, "allowed.txt");
            var result = await validator.ValidateAsync(testFile);

            result.IsValid.ShouldBeTrue();
        }
        finally
        {
            Directory.Delete(extraDir, true);
        }
    }

    [Fact]
    public async Task ValidateAsync_NotInAnyAllowedDirectory_ReturnsFailure()
    {
        var validator = CreateValidator(opts =>
        {
            // 仅允许一个不存在的子目录
            opts.Sandbox.AllowedDirectories = ["restricted-only"];
        });

        var result = await validator.ValidateAsync("outside.txt");

        result.IsValid.ShouldBeFalse();
        var error = result.Error;
        error.ShouldNotBeNull();
        error.ShouldContain("outside");
    }

    #endregion

    #region 边界情况

    [Fact]
    public async Task ValidateAsync_ResolvedPathNormalizesSlashes()
    {
        var validator = CreateValidator();

        // 使用反斜杠的路径
        var result = await validator.ValidateAsync("src\\app\\file.cs");

        result.IsValid.ShouldBeTrue();
        // 解析后的路径应该使用正斜杠
        result.ResolvedPath.ShouldNotBeNull();
        var resolvedPath = result.ResolvedPath;
        resolvedPath.ShouldNotBeNull();
        resolvedPath.ShouldContain("/");
    }

    [Fact]
    public async Task ValidateAsync_DeniedPattern_CaseInsensitive()
    {
        var validator = CreateValidator();

        // .ENV (大写) 也应该匹配 .env 模式
        var result = await validator.ValidateAsync(".ENV");

        result.IsValid.ShouldBeFalse();
    }

    #endregion
}
