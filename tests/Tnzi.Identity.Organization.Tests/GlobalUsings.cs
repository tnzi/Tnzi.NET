// 注意：这里刻意**没有** `global using Tnzi.Identity.Organization.Entities;`。
// 本测试程序集的命名空间同样落在 `Tnzi.Identity` 之下，而被测程序集
// `Tnzi.Identity.Organization` 让 `Tnzi.Identity` 多了一个名叫 `Organization` 的
// 命名空间成员 —— 裸写 `Organization` 会命中它并报 CS0118。需要实体的文件
// 各自在**命名空间体内**写一行 using（同 src 侧的 OrganizationService.cs）。
global using Mapster;
global using MapsterMapper;
global using Microsoft.AspNetCore.Identity;
global using Microsoft.EntityFrameworkCore;
global using Microsoft.Extensions.DependencyInjection;
global using Microsoft.Extensions.Logging;
global using Moq;
global using Shouldly;
global using System;
global using System.Linq;
global using System.Threading;
global using System.Threading.Tasks;
global using Tnzi.Domain.Repositories;
global using Tnzi.EFCore;
global using Tnzi.EventBus;
global using Tnzi.Identity.Dtos;
global using Tnzi.Identity.Entities;
global using Tnzi.Identity.Organization.Services;
global using Tnzi.Identity.Services;
global using Tnzi.Data;
global using Tnzi.Modules;
global using Tnzi.Mapster;
global using Tnzi.Security.Claims;
global using Tnzi.TestBase;
global using Xunit;
