global using System.Net;
global using System.Text.Json;
global using System.Threading.Tasks;

global using Microsoft.Extensions.Configuration;
global using Microsoft.Extensions.DependencyInjection;
global using Microsoft.Extensions.Logging.Abstractions;
global using Moq;

global using Tnzi.Modules;
global using Tnzi.Payment.Dtos;
global using Tnzi.Payment.Metadata;
global using Tnzi.Payment.Providers;

global using Tnzi.Payment.PayPal.Options;
global using Tnzi.Payment.PayPal.Providers;

// Tnzi.Payment.PayPal.Options 是本程序集的**外围命名空间成员**，简单名 Options 因此指向它，
// 遮住了 Microsoft.Extensions.Options.Options 静态类（拆包前遮它的是 Tnzi.Payment.Options，
// 同一个坑换了个位置）。用例要 Options.Create(...) 造一份 IOptions，故起个别名。
global using MsOptions = Microsoft.Extensions.Options.Options;
