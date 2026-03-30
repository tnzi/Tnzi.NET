namespace Tnzi.AI.Sandbox.Abstractions;

public interface IVirtualPathTranslator
{
    string ToPhysical(string virtualPath, Guid threadId);
    string ToVirtual(string physicalPath, Guid threadId);
    bool IsValidVirtualPath(string virtualPath);
    string GetThreadDirectory(Guid threadId);
    void EnsureThreadDirectories(Guid threadId);
}
