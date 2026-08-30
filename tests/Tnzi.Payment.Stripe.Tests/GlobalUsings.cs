global using System.Threading.Tasks;

global using Microsoft.Extensions.Logging;
global using Microsoft.Extensions.Options;
global using Moq;

global using Tnzi.Payment.Dtos;
global using Tnzi.Payment.Metadata;
global using Tnzi.Payment.Providers;
global using Tnzi.Payment.Stripe.Options;
global using Tnzi.Payment.Stripe.Providers;
global using Tnzi.Payment.Stripe.Services;

// 与被测程序集同一个坑：本程序集叫 Tnzi.Payment.Stripe.Tests，命名空间 Tnzi.Payment 下的成员
// Stripe 会遮蔽厂商的顶层命名空间。这里刻意不 using 厂商命名空间（用到时写 global::Stripe.X），
// 因为 Stripe 的 PaymentMethod 与 Tnzi.Payment.Metadata.PaymentMethod 同名，导进来只会更乱。
global using PaymentMethodEnum = Tnzi.Payment.Metadata.PaymentMethod;
