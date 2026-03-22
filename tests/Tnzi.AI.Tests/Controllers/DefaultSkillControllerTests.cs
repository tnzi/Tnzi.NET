using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Tnzi.AI.Controllers;
using Tnzi.AspNetCore.Extensions;
using Tnzi.AspNetCore.Models;

namespace Tnzi.AI.Tests.Controllers;

/// <summary>
/// DefaultSkillController 单元测试 — 覆盖参数传递、Result → ApiResult 转换
/// </summary>
public class DefaultSkillControllerTests
{
    private readonly Mock<ISkillService> _skillService = new();
    private readonly DefaultSkillController _controller;

    public DefaultSkillControllerTests()
    {
        _controller = new DefaultSkillController(_skillService.Object);

        var serviceProvider = new Mock<IServiceProvider>();
        var loggerFactory = new Mock<ILoggerFactory>();
        loggerFactory.Setup(f => f.CreateLogger(It.IsAny<string>())).Returns(new Mock<ILogger>().Object);
        serviceProvider.Setup(sp => sp.GetService(typeof(ILoggerFactory))).Returns(loggerFactory.Object);

        var httpContext = new DefaultHttpContext { RequestServices = serviceProvider.Object };
        _controller.ControllerContext = new ControllerContext { HttpContext = httpContext };
    }

    // -------------------------------------------------------------------------
    // GetAvailable
    // -------------------------------------------------------------------------

    [Fact]
    public async Task GetAvailable_ReturnsSkillList()
    {
        var skills = new List<SkillSummaryDto>
        {
            new() { Slug = "skill-a", Name = "Skill A" },
            new() { Slug = "skill-b", Name = "Skill B" }
        };
        _skillService.Setup(s => s.GetAvailableAsync())
            .ReturnsAsync(Result<List<SkillSummaryDto>>.Success(skills));

        var result = await _controller.GetAvailable();

        result.Succeeded.ShouldBeTrue();
        result.Data.ShouldNotBeNull();
        result.Data!.Count.ShouldBe(2);
    }

    [Fact]
    public async Task GetAvailable_ServiceFails_ReturnsFailedResult()
    {
        _skillService.Setup(s => s.GetAvailableAsync())
            .ReturnsAsync(Result<List<SkillSummaryDto>>.Failure("Error", 500));

        var result = await _controller.GetAvailable();

        result.Succeeded.ShouldBeFalse();
    }

    // -------------------------------------------------------------------------
    // GetBySlug
    // -------------------------------------------------------------------------

    [Fact]
    public async Task GetBySlug_PassesSlugToService()
    {
        var detail = new SkillDetailDto { Slug = "test-skill", Name = "Test" };
        _skillService.Setup(s => s.GetBySlugAsync("test-skill"))
            .ReturnsAsync(Result<SkillDetailDto>.Success(detail));

        var result = await _controller.GetBySlug("test-skill");

        result.Succeeded.ShouldBeTrue();
        result.Data!.Slug.ShouldBe("test-skill");
    }

    [Fact]
    public async Task GetBySlug_NotFound_ReturnsFailedResult()
    {
        _skillService.Setup(s => s.GetBySlugAsync("nonexistent"))
            .ReturnsAsync(Result<SkillDetailDto>.Failure("Skill not found", 404));

        var result = await _controller.GetBySlug("nonexistent");

        result.Succeeded.ShouldBeFalse();
        result.Code.ShouldBe(404);
    }

    // -------------------------------------------------------------------------
    // Search
    // -------------------------------------------------------------------------

    [Fact]
    public async Task Search_PassesQueryAndMaxResultsToService()
    {
        var skills = new List<SkillSummaryDto> { new() { Slug = "found-skill" } };
        _skillService.Setup(s => s.SearchAsync("coding", 5))
            .ReturnsAsync(Result<List<SkillSummaryDto>>.Success(skills));

        var result = await _controller.Search("coding", 5);

        result.Succeeded.ShouldBeTrue();
        result.Data!.Count.ShouldBe(1);
        _skillService.Verify(s => s.SearchAsync("coding", 5), Times.Once);
    }

    [Fact]
    public async Task Search_DefaultMaxResults_Is10()
    {
        _skillService.Setup(s => s.SearchAsync("test", 10))
            .ReturnsAsync(Result<List<SkillSummaryDto>>.Success([]));

        await _controller.Search("test");

        _skillService.Verify(s => s.SearchAsync("test", 10), Times.Once);
    }

    // -------------------------------------------------------------------------
    // Activate
    // -------------------------------------------------------------------------

    [Fact]
    public async Task Activate_WithParameters_PassesToService()
    {
        var parameters = new Dictionary<string, string> { ["lang"] = "csharp" };
        var input = new SkillActivateDto { Parameters = parameters };
        var activation = new SkillActivationResult { RenderedContent = "Use C#" };
        _skillService.Setup(s => s.ActivateAsync("code-review", parameters))
            .ReturnsAsync(Result<SkillActivationResult>.Success(activation));

        var result = await _controller.Activate("code-review", input);

        result.Succeeded.ShouldBeTrue();
        result.Data!.RenderedContent.ShouldBe("Use C#");
    }

    [Fact]
    public async Task Activate_WithoutInput_PassesNullParameters()
    {
        var activation = new SkillActivationResult { RenderedContent = "Content" };
        _skillService.Setup(s => s.ActivateAsync("simple-skill", null))
            .ReturnsAsync(Result<SkillActivationResult>.Success(activation));

        var result = await _controller.Activate("simple-skill", null);

        result.Succeeded.ShouldBeTrue();
        _skillService.Verify(s => s.ActivateAsync("simple-skill", null), Times.Once);
    }

    [Fact]
    public async Task Activate_ServiceFails_ReturnsFailedResult()
    {
        _skillService.Setup(s => s.ActivateAsync("broken", null))
            .ReturnsAsync(Result<SkillActivationResult>.Failure("Skill not found", 404));

        var result = await _controller.Activate("broken", null);

        result.Succeeded.ShouldBeFalse();
        result.Code.ShouldBe(404);
    }

    // -------------------------------------------------------------------------
    // Create
    // -------------------------------------------------------------------------

    [Fact]
    public async Task Create_PassesInputToService()
    {
        var input = new CreateSkillDto { Name = "New Skill", Slug = "new-skill" };
        var created = new SkillDetailDto { Slug = "new-skill", Name = "New Skill" };
        _skillService.Setup(s => s.CreateAsync(input))
            .ReturnsAsync(Result<SkillDetailDto>.Success(created));

        var result = await _controller.Create(input);

        result.Succeeded.ShouldBeTrue();
        result.Data!.Slug.ShouldBe("new-skill");
        _skillService.Verify(s => s.CreateAsync(input), Times.Once);
    }

    // -------------------------------------------------------------------------
    // Update
    // -------------------------------------------------------------------------

    [Fact]
    public async Task Update_PassesIdAndInputToService()
    {
        var id = Guid.NewGuid();
        var input = new UpdateSkillDto { Name = "Updated" };
        var updated = new SkillDetailDto { Name = "Updated" };
        _skillService.Setup(s => s.UpdateAsync(id, input))
            .ReturnsAsync(Result<SkillDetailDto>.Success(updated));

        var result = await _controller.Update(id, input);

        result.Succeeded.ShouldBeTrue();
        result.Data!.Name.ShouldBe("Updated");
        _skillService.Verify(s => s.UpdateAsync(id, input), Times.Once);
    }

    [Fact]
    public async Task Update_NotFound_ReturnsFailedResult()
    {
        var id = Guid.NewGuid();
        _skillService.Setup(s => s.UpdateAsync(id, It.IsAny<UpdateSkillDto>()))
            .ReturnsAsync(Result<SkillDetailDto>.Failure("Skill not found", 404));

        var result = await _controller.Update(id, new UpdateSkillDto());

        result.Succeeded.ShouldBeFalse();
        result.Code.ShouldBe(404);
    }

    // -------------------------------------------------------------------------
    // Delete
    // -------------------------------------------------------------------------

    [Fact]
    public async Task Delete_PassesIdToService()
    {
        var id = Guid.NewGuid();
        _skillService.Setup(s => s.DeleteAsync(id)).ReturnsAsync(Result.Success());

        var result = await _controller.Delete(id);

        result.Succeeded.ShouldBeTrue();
        _skillService.Verify(s => s.DeleteAsync(id), Times.Once);
    }

    [Fact]
    public async Task Delete_ServiceFails_ReturnsFailedResult()
    {
        var id = Guid.NewGuid();
        _skillService.Setup(s => s.DeleteAsync(id)).ReturnsAsync(Result.Failure("Not found", 404));

        var result = await _controller.Delete(id);

        result.Succeeded.ShouldBeFalse();
    }
}
