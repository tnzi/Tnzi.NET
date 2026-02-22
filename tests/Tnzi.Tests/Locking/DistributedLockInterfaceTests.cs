
namespace Tnzi.Tests.Locking;

public class DistributedLockInterfaceTests
{
    [Fact]
    public void IDistributedLock_Interface_ShouldHaveAcquireAsync()
    {
        var method = typeof(IDistributedLock).GetMethod(nameof(IDistributedLock.AcquireAsync));

        Assert.NotNull(method);
        Assert.Equal(typeof(Task<IDistributedLockHandle?>), method.ReturnType);
    }

    [Fact]
    public void IDistributedLock_Interface_ShouldHaveTryAcquireAsync()
    {
        var method = typeof(IDistributedLock).GetMethod(nameof(IDistributedLock.TryAcquireAsync));

        Assert.NotNull(method);
    }

    [Fact]
    public void IDistributedLockHandle_ShouldBeAsyncDisposable()
    {
        Assert.True(typeof(IAsyncDisposable).IsAssignableFrom(typeof(IDistributedLockHandle)));
    }

    [Fact]
    public void IDistributedLockHandle_ShouldHaveExtendAsync()
    {
        var method = typeof(IDistributedLockHandle).GetMethod(nameof(IDistributedLockHandle.ExtendAsync));

        Assert.NotNull(method);
        Assert.Equal(typeof(Task<bool>), method.ReturnType);
    }
}