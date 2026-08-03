// System（System.IO 由 ImplicitUsings 提供，不在此重复）
global using System;
global using System.Collections.Generic;
global using System.Globalization;
global using System.Linq;
global using System.Net;
global using System.Text;
global using System.Threading;
global using System.Threading.Tasks;

// Microsoft
global using Microsoft.EntityFrameworkCore;
global using Microsoft.Extensions.DependencyInjection;
global using Microsoft.Extensions.DependencyInjection.Extensions;
global using Microsoft.Extensions.Logging;

// Tnzi framework
global using Tnzi.Data;
global using Tnzi.Domain.Repositories;
global using Tnzi.Modules;
global using Tnzi.Results;
global using Tnzi.Utilities;

// Tnzi.Finance core
global using Tnzi.Finance.Metadata;

// Tnzi.Finance.Banking（支票渲染契约 ICheckDocumentRenderer + 版式枚举 + Internal 经
// InternalsVisibleTo 可见的 MicrLineComposer —— 支票是银行票据，渲染契约随银行域走）
global using Tnzi.Finance.Banking;
global using Tnzi.Finance.Banking.Metadata;
global using Tnzi.Finance.Banking.Services.Internal;

// Tnzi.Template（模板驱动渲染：存储 + 渲染编排 + 模板类型枚举）
global using Tnzi.Template;
global using Tnzi.Template.Entities;
global using Tnzi.Template.Exceptions;
global using Tnzi.Template.Services;

// Tnzi.Finance.Documents
global using Tnzi.Finance.Documents.Metadata;
global using Tnzi.Finance.Documents.Models;
global using Tnzi.Finance.Documents.Services;
global using Tnzi.Finance.Documents.Services.Internal;
global using Tnzi.Finance.Dtos;
global using Tnzi.Finance.Services;
global using Tnzi.Finance.Banking.Services;
