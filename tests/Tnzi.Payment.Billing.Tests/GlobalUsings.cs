global using Microsoft.EntityFrameworkCore;
global using Microsoft.Extensions.DependencyInjection;
global using Moq;
global using Tnzi.Data;
global using Tnzi.Domain.Repositories;
global using Tnzi.EFCore;
global using Tnzi.Modules;
global using Tnzi.Results;
global using Tnzi.Security.Authorization;
global using Tnzi.TestBase;

// 发票的投递与落地：通知请求形状与存储契约（可选注入，测试里按用例注册替身）
global using Tnzi.Notification.Dtos;
global using Tnzi.Notification.Services;
global using Tnzi.Storage.Services;

// Payment 核心（支付实体 / 事件 / 选项 / 权限），方向恒为「本模块 → 父模块」
global using Tnzi.Payment.Events;
global using Tnzi.Payment.Metadata;
global using Tnzi.Payment.Options;
global using PaymentEntity = Tnzi.Payment.Entities.Payment;

// 本模块
global using Tnzi.Payment.Billing.Dtos;
global using Tnzi.Payment.Billing.Entities;
global using Tnzi.Payment.Billing.Metadata;
global using Tnzi.Payment.Billing.Options;
global using Tnzi.Payment.Billing.Permissions;
global using Tnzi.Payment.Billing.Services;
