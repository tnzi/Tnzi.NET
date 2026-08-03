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
global using Microsoft.Extensions.DependencyInjection.Extensions;
global using Microsoft.Extensions.Hosting;
global using Microsoft.Extensions.Logging;
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
global using Tnzi.EFCore.Extensions;
global using Tnzi.EFCore.Internal;
global using Tnzi.Extensions;
global using Tnzi.Modules;
global using Tnzi.MultiTenancy;
global using Tnzi.Options;
global using Tnzi.Results;
global using Tnzi.Security.Authorization;
global using Tnzi.Settings;
global using Tnzi.Utilities;

// Finance 核心：往来方、单据服务契约（生成一律委托它们）、来源令牌
global using Tnzi.Finance.Dtos;
global using Tnzi.Finance.Entities;
global using Tnzi.Finance.Metadata;

// 本模块
global using Tnzi.Finance.Recurring.Dtos;
global using Tnzi.Finance.Recurring.Entities;
global using Tnzi.Finance.Recurring.Metadata;
global using Tnzi.Finance.Recurring.Options;
global using Tnzi.Finance.Recurring.Permissions;
global using Tnzi.Finance.Recurring.Services;
global using Tnzi.Finance.Recurring.Services.Internal;
global using Tnzi.Finance.Services;
