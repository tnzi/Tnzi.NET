
namespace Tnzi.Tests.BackgroundJobs;

public class BackgroundJobInterfaceTests
{
    [Fact]
    public void IBackgroundJobManager_ShouldHaveEnqueue()
    {
        var method = typeof(IBackgroundJobManager).GetMethod(nameof(IBackgroundJobManager.Enqueue));

        Assert.NotNull(method);
        Assert.True(method.IsGenericMethod);
    }

    [Fact]
    public void IBackgroundJobManager_ShouldHaveSchedule_WithTimeSpan()
    {
        var methods = typeof(IBackgroundJobManager).GetMethods()
            .Where(m => m.Name == nameof(IBackgroundJobManager.Schedule))
            .ToList();

        Assert.True(methods.Count >= 1);
    }

    [Fact]
    public void IBackgroundJobManager_ShouldHaveDelete()
    {
        var method = typeof(IBackgroundJobManager).GetMethod(nameof(IBackgroundJobManager.Delete));

        Assert.NotNull(method);
        Assert.Equal(typeof(bool), method.ReturnType);
    }

    [Fact]
    public void IBackgroundJob_ShouldHaveExecuteAsync()
    {
        var method = typeof(IBackgroundJob<>).GetMethod(nameof(IBackgroundJob<object>.ExecuteAsync));

        Assert.NotNull(method);
        Assert.Equal(typeof(Task), method.ReturnType);
    }
}