using System.ComponentModel.DataAnnotations;
using System.Reflection;
using Tnzi.AI.Controllers.Admin;
using Tnzi.AspNetCore.Models;

namespace Tnzi.AI.Tests.Controllers;

/// <summary>
/// 管理端读端点的结果条数有上限：查询参数不设界时，一次请求就能把整段长线程或全部评估运行读进内存。
/// 上限经 <see cref="RangeAttribute"/> 交给 MVC 模型验证（越界即 400）。
/// </summary>
public class AdminQueryBoundsTests
{
    [Theory]
    [InlineData(typeof(DefaultThreadAdminController), nameof(DefaultThreadAdminController.GetDetail), "messageLimit", 1, 200)]
    [InlineData(typeof(DefaultEvaluationAdminController), nameof(DefaultEvaluationAdminController.GetTrend), "lastNRuns", 1, 100)]
    public void QueryParameter_IsBounded(Type controller, string action, string parameter, int min, int max)
    {
        var range = controller.GetMethod(action)!
            .GetParameters()
            .Single(p => p.Name == parameter)
            .GetCustomAttribute<RangeAttribute>();

        range.ShouldNotBeNull($"{controller.Name}.{action}({parameter}) must declare an upper bound");
        range.Minimum.ShouldBe(min);
        range.Maximum.ShouldBe(max);
    }

    [Fact]
    public void TasksByStatus_IsPaged()
    {
        var method = typeof(DefaultTaskAdminController).GetMethod(nameof(DefaultTaskAdminController.GetByStatus))!;

        method.GetParameters().ShouldContain(p => typeof(PagedQueryDto).IsAssignableFrom(p.ParameterType));
        method.ReturnType.ShouldBe(typeof(Task<ApiResult<IPagedList<AgentTaskDto>>>));
    }
}
