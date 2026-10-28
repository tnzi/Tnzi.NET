// System
global using System;
global using System.Collections.Generic;
global using System.ComponentModel.DataAnnotations;
global using System.Linq;
global using System.Linq.Expressions;
global using System.Security.Cryptography;
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
global using Tnzi.Domain.Entities;
global using Tnzi.Domain.Repositories;
global using Tnzi.EFCore;
global using Tnzi.EFCore.Extensions;
global using Tnzi.EFCore.Internal;
global using Tnzi.Extensions;
global using Tnzi.Mapping;
global using Tnzi.Mapster;
global using Tnzi.Modules;
global using Tnzi.Options;
global using Tnzi.Results;
global using Tnzi.Security.Authorization;
global using Tnzi.Settings;
global using Tnzi.Utilities;

// Payment 核心：留在父模块的优惠券契约与它的三个 DTO、渠道侧同步契约、
// 错误码与常量、币种精度工具。方向恒为「本模块 → 父模块」，反过来一条都没有。
global using Tnzi.Payment.Dtos;
global using Tnzi.Payment.Metadata;
global using Tnzi.Payment.Options;
global using Tnzi.Payment.Providers;
global using Tnzi.Payment.Services;

// 注意：`Payment` 这个标识符在本程序集的命名空间里会先解析到 `Tnzi.Payment` 这个**命名空间**
// （类型查找从内层命名空间逐级向外，命名空间成员先于 using 命中），所以支付实体一律经下面的别名引用。
// 与支付的关联仍走 CouponUsage.PaymentId 这个无约束标量；唯一读支付表的地方是归还核销记录前
// 确认同一张业务单上没有别的支付还在用这次折扣（CouponService.ReleaseUsageAsync）。方向是子 → 父，合法。
global using PaymentEntity = Tnzi.Payment.Entities.Payment;

// 本模块
global using Tnzi.Payment.Promotions.Dtos;
global using Tnzi.Payment.Promotions.Entities;
global using Tnzi.Payment.Promotions.Extensions;
global using Tnzi.Payment.Promotions.Metadata;
global using Tnzi.Payment.Promotions.Options;
global using Tnzi.Payment.Promotions.Permissions;
global using Tnzi.Payment.Promotions.Services;
