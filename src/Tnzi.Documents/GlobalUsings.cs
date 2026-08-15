// System（System / System.Collections.Generic / System.IO / System.Linq / System.Threading[.Tasks]
// 由 ImplicitUsings 提供，不在此重复）
global using System.Collections.Concurrent;
global using System.Diagnostics;
global using System.Globalization;
global using System.Net.WebSockets;
global using System.Runtime.InteropServices;
global using System.Text;
global using System.Text.Json;
global using System.Text.Json.Serialization;
global using System.Text.RegularExpressions;

// Microsoft
global using Microsoft.Extensions.DependencyInjection;
global using Microsoft.Extensions.DependencyInjection.Extensions;
global using Microsoft.Extensions.Logging;
global using Microsoft.Extensions.Options;

// Tnzi framework（命名空间 Tnzi 本身不必导入：本包的命名空间都在 Tnzi.* 之下，父命名空间天然可见）
global using Tnzi.Exceptions;
global using Tnzi.Modules;
global using Tnzi.Options;
global using Tnzi.Settings;
global using Tnzi.Utilities;

// Tnzi.Documents
// 说明：PdfSharp / PdfPig 的命名空间刻意**不**全局导入 —— 两者都有 PdfDocument、
// 都有各自的矩形类型，全局导入必然撞名。第三方 PDF 命名空间一律文件级 using。
global using Tnzi.Documents.Exceptions;
global using Tnzi.Documents.Metadata;
global using Tnzi.Documents.Models;
global using Tnzi.Documents.Options;
global using Tnzi.Documents.Services;
global using Tnzi.Documents.Services.Internal;
global using PdfSharp.Drawing;
