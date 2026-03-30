namespace Tnzi.AI.Tests.Sandbox;

public class VirtualPathTranslatorTests : IDisposable
{
    private readonly string _tempDir;
    private readonly VirtualPathTranslator _translator;
    private readonly Guid _threadId = Guid.Parse("00000000-0000-0000-0000-000000000001");

    public VirtualPathTranslatorTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"tnzi-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
        _translator = new VirtualPathTranslator(_tempDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, recursive: true);
    }

    [Fact]
    public void ToPhysical_WorkspacePath_ReturnsCorrectPath()
    {
        var result = _translator.ToPhysical("/mnt/workspace/file.txt", _threadId);
        var expected = Path.GetFullPath(Path.Combine(_tempDir, _threadId.ToString("N"), "workspace", "file.txt"));
        Assert.Equal(expected, result);
    }

    [Fact]
    public void ToPhysical_UploadsPath_ReturnsCorrectPath()
    {
        var result = _translator.ToPhysical("/mnt/uploads/doc.pdf", _threadId);
        Assert.Contains("uploads", result);
        Assert.EndsWith("doc.pdf", result);
    }

    [Fact]
    public void ToPhysical_OutputsPath_ReturnsCorrectPath()
    {
        var result = _translator.ToPhysical("/mnt/outputs/report.md", _threadId);
        Assert.Contains("outputs", result);
        Assert.EndsWith("report.md", result);
    }

    [Fact]
    public void ToPhysical_PathTraversal_ThrowsSecurityException()
    {
        Assert.Throws<System.Security.SecurityException>(() =>
            _translator.ToPhysical("/mnt/workspace/../../etc/passwd", _threadId));
    }

    [Fact]
    public void ToPhysical_AbsoluteEscape_ThrowsSecurityException()
    {
        Assert.Throws<System.Security.SecurityException>(() =>
            _translator.ToPhysical("/mnt/workspace/../../../tmp/evil", _threadId));
    }

    [Fact]
    public void ToPhysical_InvalidPrefix_ThrowsSecurityException()
    {
        Assert.Throws<System.Security.SecurityException>(() =>
            _translator.ToPhysical("/etc/passwd", _threadId));
    }

    [Fact]
    public void ToVirtual_PhysicalPath_ReturnsVirtualPath()
    {
        var physicalPath = Path.GetFullPath(Path.Combine(_tempDir, _threadId.ToString("N"), "workspace", "file.txt"));
        var result = _translator.ToVirtual(physicalPath, _threadId);
        Assert.Equal("/mnt/workspace/file.txt", result);
    }

    [Fact]
    public void IsValidVirtualPath_ValidPaths_ReturnsTrue()
    {
        Assert.True(_translator.IsValidVirtualPath("/mnt/workspace"));
        Assert.True(_translator.IsValidVirtualPath("/mnt/uploads/file.pdf"));
        Assert.True(_translator.IsValidVirtualPath("/mnt/outputs/result.txt"));
    }

    [Fact]
    public void IsValidVirtualPath_InvalidPaths_ReturnsFalse()
    {
        Assert.False(_translator.IsValidVirtualPath("/tmp/file"));
        Assert.False(_translator.IsValidVirtualPath("workspace/file"));
        Assert.False(_translator.IsValidVirtualPath(""));
    }
}
