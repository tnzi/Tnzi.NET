# Tnzi.Identity 集成测试

> **目的**: 补充单元测试中跳过的 EF Core 复杂查询测试

---

## 📋 测试范围

本集成测试项目专注于以下场景：

### 1. EF Core Include 查询
- `UserService.GetByIdAsync` - 包含关联数据的用户查询
- `UserService.FindByPhoneNumberAsync` - 通过手机号查询用户

### 2. 组织树查询
- `OrganizationService.GetTreeAsync` - 组织树形结构查询
- `OrganizationService.MoveAsync` - 组织移动操作

### 3. 会话管理
- `SessionService.GetUserSessionsAsync` - 用户会话查询
- `SessionService.RevokeAllSessionsAsync` - 批量撤销会话

### 4. 登录日志
- `LoginLogService.GetUserLoginLogsAsync` - 分页查询登录日志
- `LoginLogService.DeleteExpiredLogsAsync` - 删除过期日志

### 5. Token 管理
- `AuthTokenService.GetTokenAsync` - Token 查询和验证
- `AuthTokenService.CleanExpiredTokensAsync` - 清理过期 Token

---

## 🔧 技术栈

- **数据库**: In-Memory Database (Microsoft.EntityFrameworkCore.InMemory)
- **测试框架**: xUnit
- **Mock 框架**: Moq
- **.NET 版本**: 10.0

---

## 🚀 运行测试

```bash
# 运行所有集成测试
dotnet test tests/Tnzi.Identity.IntegrationTests

# 运行特定测试类
dotnet test --filter "FullyQualifiedName~UserServiceIntegrationTests"

# 生成代码覆盖率报告
dotnet test --collect:"XPlat Code Coverage"
```

---

## 📁 项目结构

```
Tnzi.Identity.IntegrationTests/
├── IntegrationTestBase.cs          # 测试基类（提供内存数据库）
├── Services/
│   ├── UserServiceIntegrationTests.cs
│   ├── OrganizationServiceIntegrationTests.cs
│   ├── SessionServiceIntegrationTests.cs
│   └── ...
└── README.md
```

---

## 💡 编写新测试

继承 `IntegrationTestBase` 并重写 `ConfigureServices` 方法：

```csharp
public class MyServiceIntegrationTests : IntegrationTestBase
{
    protected override void ConfigureServices(IServiceCollection services)
    {
        // 注册必要的 Repository 和 Service
        services.AddScoped<IRepository<MyEntity, Guid>, 
            EfCoreRepository<IdentityDbContext, MyEntity, Guid>>();
        services.AddScoped<IMyService, MyService>();
    }

    [Fact]
    public async Task MyTest()
    {
        var service = GetService<IMyService>();
        // ... 测试逻辑
    }
}
```

---

## 🎯 与单元测试的区别

| 特性 | 单元测试 | 集成测试 |
|-----|---------|---------|
| 数据库 | Mock Repository | In-Memory Database |
| EF Core 查询 | 无法完全模拟 | 真实执行 |
| 测试速度 | 快 (~1ms) | 较慢 (~10ms) |
| 适用场景 | 业务逻辑 | 复杂查询 |

---

**最后更新**: 2025-12-21
