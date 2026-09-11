global using System.Globalization;
global using System.Net;
global using System.Net.Http.Headers;
global using System.Net.Http.Json;
global using System.Text;
global using System.Text.Json;
global using System.Text.Json.Serialization;

global using Microsoft.Extensions.DependencyInjection;
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

global using Tnzi.Payment.PayPal.Options;
global using Tnzi.Payment.PayPal.Providers;
global using System.Collections.Concurrent;
global using Tnzi.Payment.PayPal.Providers.Models;
