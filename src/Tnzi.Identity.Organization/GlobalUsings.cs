// ★ 这里刻意**没有** `global using Tnzi.Identity.Organization.Entities;`。
//
// 本程序集名是 Tnzi.Identity.Organization，于是命名空间 Tnzi.Identity 里多了一个
// 名叫 `Organization` 的成员（就是本程序集的根命名空间）。C# 的名称查找在每一层
// 先看该层的**命名空间成员**，再看类型，最后才看该层 using 引入的类型；而
// `global using` 属于编译单元层，排在所有外层命名空间之后。所以在
// `Tnzi.Identity.Organization.Services` 这类命名空间里写裸的 `Organization`，
// 查到 `Tnzi.Identity` 这一层时会先命中那个**命名空间**，编译期报
// `CS0118: 'Organization' is a namespace but is used like a type`——
// 全局 using 在这条路径上永远抢不到。
//
// 解法是把 using 写进**命名空间体内**（文件作用域命名空间声明之后那一行），
// 它属于该命名空间这一层，因此在到达 `Tnzi.Identity` 之前就命中类型。
// 需要 Organization 实体的文件（`Services/OrganizationService.cs`）自己写这一行。
// 漏写的表现是编译失败，不是静默错误 —— 这是可接受的代价，也是不改实体类名
// （改名会连带改表名，与"零迁移"直接冲突）与不改程序集名的必然结果。
global using IdentityConstants = Tnzi.Identity.Metadata.IdentityConstants;
global using Microsoft.AspNetCore.Identity;
global using Microsoft.AspNetCore.Mvc;
global using Microsoft.EntityFrameworkCore;
global using Microsoft.EntityFrameworkCore.Metadata.Builders;
global using Microsoft.Extensions.DependencyInjection;
global using Microsoft.Extensions.Options;
global using System;
global using System.Collections.Generic;
global using System.Linq;
global using System.Threading;
global using System.Threading.Tasks;
global using Tnzi.Application;
global using Tnzi.AspNetCore.Extensions;
global using Tnzi.AspNetCore.Models;
global using Tnzi.AspNetCore.Mvc;
global using Tnzi.Caching;
global using Tnzi.Data;
global using Tnzi.Domain.Entities;
global using Tnzi.Domain.Repositories;
global using Tnzi.EFCore;
global using Tnzi.EFCore.Extensions;
global using Tnzi.EFCore.Internal;
global using Tnzi.EventBus;
global using Tnzi.Exceptions;
global using Tnzi.Extensions;
global using Tnzi.Identity.Dtos;
global using Tnzi.Identity.Entities;
global using Tnzi.Identity.Extensions;
global using Tnzi.Identity.Services;
global using Tnzi.Identity.Organization.Events;
global using Tnzi.Identity.Organization.Permissions;
global using Tnzi.Identity.Organization.Services;
global using Tnzi.Mapping;
global using Tnzi.Mapster;
global using Tnzi.Modules;
global using Tnzi.MultiTenancy;
global using Tnzi.Results;
global using Tnzi.Security.Authorization;
global using Tnzi.Security.Claims;
global using Tnzi.Utilities;
