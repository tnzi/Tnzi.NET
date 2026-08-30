// System
global using System;
global using System.Collections.Generic;
global using System.Linq;
global using System.Threading;
global using System.Threading.Tasks;

// Microsoft
global using Microsoft.AspNetCore.Mvc;
global using Microsoft.EntityFrameworkCore;
global using Microsoft.EntityFrameworkCore.Metadata.Builders;
global using Microsoft.Extensions.DependencyInjection;
global using Microsoft.Extensions.Options;

// Tnzi framework
global using Tnzi.Application;
global using Tnzi.AspNetCore.Extensions;
global using Tnzi.AspNetCore.Models;
global using Tnzi.AspNetCore.Mvc;
global using Tnzi.Data;
global using Tnzi.Domain.Entities;
global using Tnzi.Domain.Repositories;
global using Tnzi.EFCore;
global using Tnzi.EFCore.DocumentNumbering;
global using Tnzi.EFCore.Extensions;
global using Tnzi.EFCore.Internal;
global using Tnzi.EventBus;
global using Tnzi.Extensions;
global using Tnzi.Mapster;
global using Tnzi.Modules;
global using Tnzi.Options;
global using Tnzi.Results;
global using Tnzi.Security.Authorization;
global using Tnzi.Settings;
global using Tnzi.Utilities;

// Finance 核心：往来方与目录主数据、发票/账单服务契约（转换一律委托它们）、
// 来源令牌、行构建所依赖的共享工具与配置。方向恒为「本模块 → 核心」。
global using Tnzi.Finance.Dtos;
global using Tnzi.Finance.Entities;
global using Tnzi.Finance.Metadata;
global using Tnzi.Finance.Options;
global using Tnzi.Finance.Services;
global using Tnzi.Finance.Services.Internal;

// 本模块
global using Tnzi.Finance.Offers.Dtos;
global using Tnzi.Finance.Offers.Entities;
global using Tnzi.Finance.Offers.Events;
global using Tnzi.Finance.Offers.Extensions;
global using Tnzi.Finance.Offers.Metadata;
global using Tnzi.Finance.Offers.Options;
global using Tnzi.Finance.Offers.Permissions;
global using Tnzi.Finance.Offers.Services;
global using Tnzi.Finance.Offers.Services.Internal;
