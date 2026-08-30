global using Microsoft.EntityFrameworkCore;
global using Microsoft.Extensions.DependencyInjection;
global using Microsoft.Extensions.Options;
global using Moq;
global using Tnzi.Data;
global using Tnzi.Domain.Repositories;
global using Tnzi.EFCore;
global using Tnzi.Modules;
global using Tnzi.Results;
global using Tnzi.Security.Authorization;
global using Tnzi.Settings;
global using Tnzi.TestBase;

// Payment 核心（支付实体 / 服务契约 / 事件 / 选项 / 权限），方向恒为「本模块 → 父模块」
global using Tnzi.Payment.Dtos;
global using Tnzi.Payment.Entities;
global using Tnzi.Payment.Events;
global using Tnzi.Payment.Metadata;
global using Tnzi.Payment.Options;
global using Tnzi.Payment.Providers;
global using Tnzi.Payment.Services;
global using PaymentEntity = Tnzi.Payment.Entities.Payment;

// 折扣包（仅测试引用，见 csproj 里的说明）：促销实体与它的服务、枚举、选项。
global using Tnzi.Payment.Promotions.Entities;
global using Tnzi.Payment.Promotions.Metadata;
global using Tnzi.Payment.Promotions.Options;
global using Tnzi.Payment.Promotions.Services;

// 本模块
global using Tnzi.Payment.Subscriptions.Dtos;
global using Tnzi.Payment.Subscriptions.Entities;
global using Tnzi.Payment.Subscriptions.Events;
global using Tnzi.Payment.Subscriptions.Metadata;
global using Tnzi.Payment.Subscriptions.Options;
global using Tnzi.Payment.Subscriptions.Permissions;
global using Tnzi.Payment.Subscriptions.Services;
