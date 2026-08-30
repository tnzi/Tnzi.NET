global using System.Collections.Generic;
global using System.Linq;
global using System.Net;
global using System.Threading;
global using System.Threading.Tasks;

global using Microsoft.Extensions.DependencyInjection;
global using Microsoft.Extensions.DependencyInjection.Extensions;
global using Microsoft.Extensions.Logging;
global using Microsoft.Extensions.Options;

global using Tnzi.Modules;
global using Tnzi.Options;
global using Tnzi.Results;
global using Tnzi.Utilities;

// Tnzi.Payment 本身不用写：本程序集的命名空间都在它下面，父命名空间的成员天然可见。
global using Tnzi.Payment.Dtos;
global using Tnzi.Payment.Metadata;
global using Tnzi.Payment.Providers;

global using Tnzi.Payment.Stripe.Options;
global using Tnzi.Payment.Stripe.Providers;
global using Tnzi.Payment.Stripe.Services;

// ★ 本程序集叫 Tnzi.Payment.Stripe，于是命名空间 Tnzi.Payment 下多出一个成员 Stripe，
// 它在本程序集的每个文件里都**遮蔽**厂商的顶层命名空间 Stripe：写 Stripe.RefundService
// 会被解析成 Tnzi.Payment.Stripe.RefundService 而编译不过。
// 简单名（StripeClient / StripeException / PaymentIntentService …）经下面这条 using 照常可用；
// 需要显式限定厂商类型时一律写 global::Stripe.X，不要写 Stripe.X。
global using Stripe;

// Tnzi.Payment.Metadata 里有自己的 PaymentMethod 枚举，与 Stripe 的支付方式实体同名。
// 两边都要用，故各给一个别名，file-level using 不再重复声明。
global using PaymentMethodEnum = Tnzi.Payment.Metadata.PaymentMethod;
global using StripePaymentMethod = global::Stripe.PaymentMethod;
