// System
global using System;
global using System.Collections.Generic;
global using System.ComponentModel.DataAnnotations;
global using System.IO;
global using System.Linq;
global using System.Net;
global using System.Text;
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
global using Tnzi.Mapster;
global using Tnzi.Modules;
global using Tnzi.Options;
global using Tnzi.Results;
global using Tnzi.Security.Authorization;
global using Tnzi.Security.Claims;
global using Tnzi.Settings;
global using Tnzi.Utilities;

// 发票 PDF 的渲染 / 转换 / 落地 / 投递。这四条依赖是发票域自己的，
// 不是支付域的 —— 父模块里它们的唯一使用者就是搬到本模块来的这个服务。
global using Tnzi.Notification;
global using Tnzi.Notification.Dtos;
global using Tnzi.Notification.Metadata;
global using Tnzi.Notification.Services;
global using Tnzi.Storage;
global using Tnzi.Storage.Services;
global using Tnzi.Template;
global using Tnzi.Template.Models;
global using Tnzi.Template.Services;

// Payment 核心：支付完成事件、支付状态枚举、错误码与常量。方向恒为「本模块 → 父模块」。
// 支付实体本身不 using 整个 Tnzi.Payment.Entities，只走下面那条别名（见别名处的注释）。
global using Tnzi.Payment.Events;
global using Tnzi.Payment.Metadata;
global using Tnzi.Payment.Options;

// `Payment` 这个标识符在本程序集的命名空间里会先解析到 `Tnzi.Payment` 这个**命名空间**
// （类型查找从内层命名空间逐级向外，命名空间成员先于 using 命中），因此支付实体一律走别名。
global using PaymentEntity = Tnzi.Payment.Entities.Payment;

// 本模块
global using Tnzi.Payment.Billing.Dtos;
global using Tnzi.Payment.Billing.Entities;
global using Tnzi.Payment.Billing.Events;
global using Tnzi.Payment.Billing.Extensions;
global using Tnzi.Payment.Billing.Metadata;
global using Tnzi.Payment.Billing.Options;
global using Tnzi.Payment.Billing.Permissions;
global using Tnzi.Payment.Billing.Services;
