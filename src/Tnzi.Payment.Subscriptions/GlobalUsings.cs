// System
global using System;
global using System.Collections.Generic;
global using System.ComponentModel.DataAnnotations;
global using System.Linq;
global using System.Text.Json;
global using System.Threading;
global using System.Threading.Tasks;

// Microsoft
global using Microsoft.AspNetCore.Mvc;
global using Microsoft.EntityFrameworkCore;
global using Microsoft.EntityFrameworkCore.Metadata.Builders;
global using Microsoft.Extensions.DependencyInjection;
global using Microsoft.Extensions.Logging;
global using Microsoft.Extensions.Options;

// Tnzi framework
global using Tnzi.Application;
global using Tnzi.AspNetCore.Extensions;
global using Tnzi.AspNetCore.Models;
global using Tnzi.AspNetCore.Mvc;
global using Tnzi.Data;
global using Tnzi.Data.Snows;
global using Tnzi.Domain.Entities;
global using Tnzi.Domain.Repositories;
global using Tnzi.EFCore;
global using Tnzi.EFCore.Extensions;
global using Tnzi.EFCore.Internal;
global using Tnzi.EventBus;
global using Tnzi.Extensions;
global using Tnzi.Mapping;
global using Tnzi.Mapster;
global using Tnzi.Modules;
global using Tnzi.Options;
global using Tnzi.Results;
global using Tnzi.Security.Authorization;
global using Tnzi.Security.Claims;
global using Tnzi.Settings;
global using Tnzi.Utilities;

// 续费提醒 / 扣款失败告警。这条依赖是续费域自己的，父模块里它的使用者本来就只有订阅服务。
global using Tnzi.Notification;
global using Tnzi.Notification.Dtos;
global using Tnzi.Notification.Services;

// Payment 核心：支付服务与渠道工厂、绑卡服务、支付事件、错误码与常量、支付侧选项。
// 方向恒为「本模块 → 父模块」，反过来一条都没有。
global using Tnzi.Payment.Dtos;
global using Tnzi.Payment.Entities;
global using Tnzi.Payment.Events;
global using Tnzi.Payment.Metadata;
global using Tnzi.Payment.Options;
global using Tnzi.Payment.Providers;
global using Tnzi.Payment.Services;

// 注意：`Payment` 这个标识符在本程序集的命名空间里会先解析到 `Tnzi.Payment` 这个**命名空间**
// （类型查找从内层命名空间逐级向外，命名空间成员先于 using 命中）。本模块目前不直接用支付实体
// （删掉那条死导航之后，与支付的关联走 BusinessOrderNo == SubscriptionNo），
// 真要用时请像 Tnzi.Payment.Billing 那样起一个 `PaymentEntity` 别名，不要写裸的 `Payment`。

// 本模块
global using Tnzi.Payment.Subscriptions.Dtos;
global using Tnzi.Payment.Subscriptions.Entities;
global using Tnzi.Payment.Subscriptions.Events;
global using Tnzi.Payment.Subscriptions.Extensions;
global using Tnzi.Payment.Subscriptions.Metadata;
global using Tnzi.Payment.Subscriptions.Options;
global using Tnzi.Payment.Subscriptions.Permissions;
global using Tnzi.Payment.Subscriptions.Services;
