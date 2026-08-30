global using Microsoft.Extensions.DependencyInjection;
global using Microsoft.Extensions.Options;
global using Moq;
global using Tnzi.Domain.Repositories;
global using Tnzi.Modules;
global using Tnzi.Results;
global using Tnzi.Security.Authorization;
global using Tnzi.Settings;

// 父模块：留在那里的优惠券契约与三个 DTO、支付/退款实体、渠道同步契约、错误码与常量、统计 DTO。
global using Tnzi.Payment.Dtos;
global using Tnzi.Payment.Entities;
global using Tnzi.Payment.Metadata;
global using Tnzi.Payment.Providers;
global using Tnzi.Payment.Services;

// 本模块
global using Tnzi.Payment.Promotions.Dtos;
global using Tnzi.Payment.Promotions.Entities;
global using Tnzi.Payment.Promotions.Metadata;
global using Tnzi.Payment.Promotions.Options;
global using Tnzi.Payment.Promotions.Services;
